using System.ServiceProcess;

namespace HonorPCHelper;

/// <summary>
/// Остановка служб и процессов HONOR (PC Manager, OSD, обновления).
///
/// Службы не отвечают за оборудование ноутбука, а их работа мешает: OSD
/// перекрывает плашку приложения, служба обновлений тянет свои пакеты.
/// Останов и снятие с автозапуска требуют прав администратора, поэтому
/// выполняются привилегированной задачей - при входе в систему и по пункту меню.
/// </summary>
internal static class HonorServices
{
    private static readonly string[] ServiceNames = ["OSDServer", "MBAMainService", "HnUpdateService"];

    internal static bool StopAll()
    {
        var anyError = false;
        foreach (var name in ServiceNames)
        {
            try
            {
                using var service = new ServiceController(name);
                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + name, writable: true);
                key?.SetValue("Start", 4, Microsoft.Win32.RegistryValueKind.DWord);
                AppLog.Info($"Service stopped: {name}");
            }
            catch (Exception exception)
            {
                anyError = true;
                AppLog.Error($"Stop service failed: {name} | {exception.Message}");
            }
        }
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (process.MainModule?.FileName.StartsWith(
                            @"C:\Program Files\HONOR", StringComparison.OrdinalIgnoreCase) == true)
                        process.Kill();
                }
                catch
                {
                    // Чужие процессы или уже завершившиеся.
                }
            }
        }
        catch (Exception exception)
        {
            anyError = true;
            AppLog.Error($"Stop processes failed: {exception.Message}");
        }
        return !anyError;
    }
}
