using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace HonorPCHelper;

/// <summary>
/// Автозапуск при входе в систему. Задача планировщика с наивысшими правами
/// поднимает приложение без запроса UAC, чего не умеет Run-ключ.
/// </summary>
internal static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HonorPCHelper";
    private const string TaskName = "HonorPCHelper Startup";

    internal static bool IsEnabled
    {
        get
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (process is null)
                return false;
            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь приложения.");
        RemoveLegacyRunEntry();
        if (enabled)
            CreateLogonTask(executable);
        else
            DeleteTask();
    }

    /// <summary>
    /// Переносит прежний автозапуск: значение Run-ключа осталось от сборки,
    /// которая поднималась через UAC и могла ссылаться на старую папку.
    /// </summary>
    internal static void MigrateLegacyRunEntry()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !HasLegacyRunEntry())
            return;
        RemoveLegacyRunEntry();
        if (!IsEnabled)
            CreateLogonTask(executable);
    }

    private static bool HasLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read startup registry value", exception);
            return false;
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.DeleteValue(ValueName, false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not remove startup registry value", exception);
        }
    }

    private static void CreateLogonTask(string executable)
    {
        dynamic? service = null;
        dynamic? folder = null;
        dynamic? definition = null;
        dynamic? action = null;
        dynamic? trigger = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Планировщик задач недоступен.");
            service = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Планировщик задач недоступен.");
            service.Connect();
            folder = service.GetFolder("\\");
            DeleteTask(folder);

            definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Honor PC Helper автозапуск";
            var user = WindowsIdentity.GetCurrent().Name;
            definition.Principal.UserId = user;
            definition.Principal.LogonType = 3;
            definition.Principal.RunLevel = 1;

            trigger = definition.Triggers.Create(9);
            trigger.UserId = user;

            action = definition.Actions.Create(0);
            action.Path = executable;

            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
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
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
                return;
            service = Activator.CreateInstance(type);
            if (service is null)
                return;
            service.Connect();
            folder = service.GetFolder("\\");
            DeleteTask(folder);
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
        catch
        {
            // Задачи нет - удалять нечего.
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
