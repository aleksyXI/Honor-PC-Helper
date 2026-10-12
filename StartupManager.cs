using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HonorPCHelper;

/// <summary>
/// Автозапуск при входе в систему.
///
/// Задача планировщика с наивысшими правами поднимает приложение Elevated и
/// без запроса UAC - Run-ключ этого не умеет, а настройки оборудования
/// (службы HONOR, схема питания, камера) требуют прав администратора.
/// Прежние сборки пользовались Run-ключом и ярлыком в папке "Автозагрузка":
/// оба переносятся в задачу при первом запуске, чтобы приложение не оказалось
/// запущенным дважды.
/// </summary>
internal static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HonorPCHelper";
    private const string TaskName = "HonorPCHelper Startup";
    // TASK_TRIGGER_LOGON, TASK_LOGON_INTERACTIVE_TOKEN, TASK_RUNLEVEL_HIGHEST.
    private const int LogonTriggerType = 9;
    private const int LogonTypeInteractiveToken = 3;
    private const int RunLevelHighest = 1;

    private static string LegacyShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HonorPCHelper.lnk");

    internal static bool IsEnabled
    {
        get
        {
            dynamic? service = null;
            dynamic? folder = null;
            dynamic? task = null;
            try
            {
                service = CreateService();
                folder = service.GetFolder("\\");
                task = folder.GetTask(TaskName);
                return task is not null;
            }
            catch
            {
                return false;
            }
            finally
            {
                Release(task);
                Release(folder);
                Release(service);
            }
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
            RegisterTask(GetExecutablePath());
        else
            DeleteTask();

        // Значение Run-ключа и ярлык прежних сборок живут отдельно от задачи:
        // пока они на месте, приложение запускается второй раз, уже без прав.
        RemoveLegacyEntries();
    }

    /// <summary>
    /// Переносит автозапуск прежних сборок в задачу. Если задача уже есть,
    /// старые записи просто удаляются: иначе приложение поднималось бы дважды.
    /// </summary>
    internal static void MigrateLegacyRunEntry()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !HasLegacyEntries())
            return;

        try
        {
            if (!IsEnabled)
                RegisterTask(executable);
            RemoveLegacyEntries();
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not migrate the legacy startup entry", exception);
        }
    }

    private static bool HasLegacyEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(ValueName) is not null || File.Exists(LegacyShortcutPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the startup registry value", exception);
            return false;
        }
    }

    private static void RemoveLegacyEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.DeleteValue(ValueName, false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not remove the startup registry value", exception);
        }

        try
        {
            File.Delete(LegacyShortcutPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not remove the startup shortcut", exception);
        }
    }

    private static void RegisterTask(string executable)
    {
        dynamic? service = null;
        dynamic? folder = null;
        dynamic? definition = null;
        dynamic? trigger = null;
        dynamic? action = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            // Задача перерегистрируется на текущий путь приложения: после
            // самообновления или переноса папки прежний путь уже не существует.
            DeleteTask(folder);

            definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Honor PC Helper autostart";
            var user = WindowsIdentity.GetCurrent().Name;
            definition.Principal.UserId = user;
            definition.Principal.LogonType = LogonTypeInteractiveToken;
            definition.Principal.RunLevel = RunLevelHighest;
            definition.Settings.Enabled = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;

            trigger = definition.Triggers.Create(LogonTriggerType);
            trigger.UserId = user;

            action = definition.Actions.Create(0);
            action.Path = executable;

            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, LogonTypeInteractiveToken, null);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not create the startup task", exception);
            throw;
        }
        finally
        {
            Release(trigger);
            Release(action);
            Release(definition);
            Release(folder);
            Release(service);
        }
    }

    private static void DeleteTask()
    {
        dynamic? service = null;
        dynamic? folder = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            DeleteTask(folder);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not delete the startup task", exception);
            throw;
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    private static void DeleteTask(dynamic folder)
    {
        try
        {
            folder.DeleteTask(TaskName, 0);
        }
        catch (FileNotFoundException)
        {
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070002)
        {
            // Задачи нет - удалять нечего.
        }
    }

    private static dynamic CreateService()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(serviceType)!;
        service.Connect();
        return service;
    }

    private static string GetExecutablePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the path to HonorPCHelper.exe.");

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
