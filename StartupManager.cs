using System.Diagnostics;
using Microsoft.Win32;
namespace HonorPCHelper;
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
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true))
            key.DeleteValue(ValueName, false);
        var args = enabled
            ? $"/create /tn \"{TaskName}\" /tr \"\\\"{executable}\\\"\" /sc onlogon /rl highest /f"
            : $"/delete /tn \"{TaskName}\" /f";
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Не удалось запустить планировщик задач.");
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"schtasks вернул код {process.ExitCode}");
    }
}
