using System.Management;
namespace HonorPCHelper;
internal static class BrightnessController
{
    private static readonly Lock Gate = new();
    private static ManagementObject? _brightness;
    private static ManagementObject[] _methods = [];
    internal static int Change(int delta)
    {
        lock (Gate)
        {
            try
            {
                return ChangeCore(delta);
            }
            catch (ManagementException)
            {
                Reset();
                return ChangeCore(delta);
            }
        }
    }
    private static int ChangeCore(int delta)
    {
        if (_brightness is null || _methods.Length == 0)
            Initialize();
        if (_brightness is null)
            return -1;
        _brightness.Get();
        var current = Convert.ToInt32(_brightness["CurrentBrightness"]);
        var target = (byte)Math.Clamp(current + delta, 0, 100);
        foreach (var monitor in _methods)
            monitor.InvokeMethod("WmiSetBrightness", new object[] { 0u, target });
        return target;
    }
    private static void Initialize()
    {
        Reset();
        using var brightnessSearcher = new ManagementObjectSearcher(
            @"root\WMI", "SELECT * FROM WmiMonitorBrightness WHERE Active=True");
        _brightness = brightnessSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
        using var methodsSearcher = new ManagementObjectSearcher(
            @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
        _methods = methodsSearcher.Get().Cast<ManagementObject>().ToArray();
    }
    private static void Reset()
    {
        _brightness?.Dispose();
        _brightness = null;
        foreach (var monitor in _methods)
            monitor.Dispose();
        _methods = [];
    }
}
