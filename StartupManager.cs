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
    // TASK_INSTANCES_IGNORE_NEW: второй экземпляр не поднимается, вместо него
    // молча работает первый.
    private const int InstancesIgnoreNew = 2;
    // Без ограничения по времени: иначе планировщик завершает приложение
    // через трое суток непрерывной работы.
    private const string UnlimitedExecution = "PT0S";
    private const int RestartCount = 3;
    private const string RestartInterval = "PT1M";

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

    /// <summary>
    /// Приводит уже зарегистрированную задачу к текущей сборке.
    ///
    /// Задачу могла создать прежняя сборка: тогда она запускается только при
    /// питании от сети, завершается при переходе на батарею, живёт с лимитом в
    /// трое суток и указывает на прежнюю папку приложения - снаружи это
    /// выглядит как неработающий автозапуск. Регистрация задачи с наивысшими
    /// правами доступна только повышенному процессу, поэтому обычный экземпляр
    /// отдаёт работу фоновой задаче.
    /// </summary>
    internal static async Task SynchronizeAsync()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !IsEnabled || IsUpToDate(executable))
            return;

        if (IsElevated())
        {
            try
            {
                RegisterTask(executable);
                AppLog.Info("Startup task updated for " + executable);
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not update the startup task", exception);
            }
            return;
        }

        // Задача устарела - значит, и фоновая задача может указывать на прежний
        // путь. Тогда единственный оставшийся путь - повышенный экземпляр через
        // UAC; запрос задаётся не чаще раза за сеанс.
        await PrivilegedHardware.TryRunStartupTaskAsync(Elevation.UnlessDeclined);
    }

    /// <summary>
    /// Перерегистрирует задачу на путь этого экземпляра. Точка входа
    /// повышенного запуска: обычному процессу планировщик здесь отказывает.
    /// </summary>
    internal static void RegisterCurrent()
    {
        if (IsEnabled)
            RegisterTask(GetExecutablePath());
    }

    /// <summary>
    /// Совпадает ли задача с тем, что ставит текущая сборка: путь, отсутствие
    /// аргументов, снятые условия питания, снятый лимит времени и повышенный
    /// уровень запуска.
    /// </summary>
    private static bool IsUpToDate(string executable)
    {
        dynamic? service = null;
        dynamic? folder = null;
        dynamic? task = null;
        dynamic? definition = null;
        dynamic? action = null;
        try
        {
            service = CreateService();
            folder = service.GetFolder("\\");
            task = folder.GetTask(TaskName);
            definition = task.Definition;
            action = definition.Actions.Item(1);

            var path = Convert.ToString(action.Path);
            if (string.IsNullOrWhiteSpace(path)
                || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(Convert.ToString(action.Arguments)))
                return false;

            if (Convert.ToBoolean(definition.Settings.DisallowStartIfOnBatteries)
                || Convert.ToBoolean(definition.Settings.StopIfGoingOnBatteries))
                return false;

            if (!string.Equals(Convert.ToString(definition.Settings.ExecutionTimeLimit), UnlimitedExecution,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return Convert.ToInt32(definition.Principal.RunLevel) == RunLevelHighest
                && Convert.ToInt32(definition.Principal.LogonType) == LogonTypeInteractiveToken;
        }
        catch (Exception exception)
        {
            // Задачи нет или она не читается: перерегистрировать её отсюда
            // нельзя, этим займётся повышенный экземпляр.
            AppLog.Error("Could not read the startup task", exception);
            return true;
        }
        finally
        {
            Release(action);
            Release(definition);
            Release(task);
            Release(folder);
            Release(service);
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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

            definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Honor PC Helper autostart";
            var user = WindowsIdentity.GetCurrent().Name;
            definition.Principal.UserId = user;
            definition.Principal.LogonType = LogonTypeInteractiveToken;
            definition.Principal.RunLevel = RunLevelHighest;
            definition.Settings.Enabled = true;
            // Условия питания по умолчанию включены: задача не запускается от
            // батареи и завершается при переходе на неё. Для значка в трее это
            // пропавший автозапуск на выезде.
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = UnlimitedExecution;
            definition.Settings.MultipleInstances = InstancesIgnoreNew;
            definition.Settings.RestartCount = RestartCount;
            definition.Settings.RestartInterval = RestartInterval;

            trigger = definition.Triggers.Create(LogonTriggerType);
            trigger.UserId = user;

            action = definition.Actions.Create(0);
            action.Path = executable;
            action.WorkingDirectory = Path.GetDirectoryName(executable);

            // Флаги 6 - создать или обновить: прежняя задача перезаписывается
            // той же операцией. Отдельное удаление оставило бы приложение без
            // автозапуска, если регистрация почему-то не пройдёт.
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
