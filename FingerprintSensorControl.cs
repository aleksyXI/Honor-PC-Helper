using System.Diagnostics;
using System.Management;

namespace HonorPCHelper;

/// <summary>
/// Биометрический датчик отпечатка.
///
/// В закрытом ноутбуке датчик на кнопке питания остаётся живым: он будит
/// машину и участвует во входе. Пока крышка закрыта, устройство отключается и
/// включается обратно, как только крышку открыли. Отключение устройства -
/// операция администратора, поэтому команда идёт через привилегированную
/// задачу.
/// </summary>
internal static class FingerprintSensorControl
{
    // Класс устройств Windows Biometric Framework. Ищется по имени класса, а не
    // по GUID: на разных выпусках Windows GUID класса встречается разный.
    private const string BiometricClass = "Biometric";
    // CM_PROB_DISABLED: устройство отключено.
    private const int ProblemDisabled = 22;

    private static bool? _supported;

    /// <summary>Есть ли на машине датчик отпечатка. Ответ кэшируется: меню спрашивает при каждом открытии.</summary>
    internal static bool IsSupported() => _supported ??= FindSensors().Count > 0;

    /// <summary>Датчик сейчас отключён. null - датчика нет или состояние не прочитать.</summary>
    internal static bool? IsDisabled()
    {
        var sensors = FindSensors();
        return sensors.Count == 0 ? null : sensors.All(sensor => sensor.Problem == ProblemDisabled);
    }

    /// <summary>Отключает или включает датчик; повтор уже достигнутого состояния ничего не делает.</summary>
    internal static void SetDisabled(bool disabled)
    {
        var sensors = FindSensors();
        if (sensors.Count == 0)
            throw new InvalidOperationException(L.T(
                "Датчик отпечатка не найден.",
                "Fingerprint sensor not found.",
                "未找到指纹传感器。"));

        var changed = false;
        foreach (var sensor in sensors)
        {
            if ((sensor.Problem == ProblemDisabled) == disabled)
                continue;

            RunPnPUtil(disabled ? "/disable-device" : "/enable-device", sensor.InstanceId);
            changed = true;
        }

        // Включённое устройство система подхватывает не сразу: без повторного
        // сканирования датчик остаётся невидимым для Windows Hello.
        if (changed && !disabled)
            RunPnPUtil("/scan-devices", null);

        // Состояние изменилось - следующий опрос должен его перечитать.
        _supported = null;
    }

    private static List<(string InstanceId, int Problem)> FindSensors()
    {
        var sensors = new List<(string, int)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = '" + BiometricClass + "'");
            using var results = searcher.Get();
            foreach (ManagementObject device in results)
            {
                using (device)
                {
                    if (device["DeviceID"] is string instanceId && !string.IsNullOrWhiteSpace(instanceId))
                        sensors.Add((instanceId, Convert.ToInt32(device["ConfigManagerErrorCode"] ?? 0)));
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not enumerate the fingerprint sensor", exception);
        }

        return sensors;
    }

    private static void RunPnPUtil(string argument, string? instanceId)
    {
        var startInfo = new ProcessStartInfo("pnputil.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(argument);
        if (instanceId is not null)
            startInfo.ArgumentList.Add(instanceId);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pnputil.exe is unavailable.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        var text = output.Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pnputil {argument}: {text}");

        AppLog.Info($"pnputil {argument} {instanceId}: {text}");
    }
}
