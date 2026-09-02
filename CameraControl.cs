using Microsoft.Win32;
namespace HonorPCHelper;
internal static class CameraControl
{
    private const string KeyPath = @"HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings";
    private const string ValueName = "CameraStatus";
    internal static bool IsCameraEnabled()
    {
        try
        {
            var value = Registry.GetValue(KeyPath, ValueName, 1);
            return value is int i ? i != 0 : true;
        }
        catch
        {
            return true;
        }
    }
    internal static void SetCamera(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\HONOR\ASvidDMFT\Settings", writable: true);
            key?.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            AppLog.Info($"Camera set to {(enabled ? "ON" : "OFF")}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Camera registry write failed", exception);
        }
    }
}
