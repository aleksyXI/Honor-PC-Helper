namespace HonorPCHelper;
internal enum TouchpadHapticsLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}
internal static class TouchpadHapticsController
{
    internal static bool IsSupported() => TouchpadVendorLink.IsSupported();
    internal static void SetLevel(TouchpadHapticsLevel level)
        => TouchpadVendorLink.Send(TouchpadVendorLink.HapticsCommand, (byte)level);
    internal static void Reapply()
    {
        var level = HardwareSettings.TouchpadHaptics;
        if (level is null)
            return;
        try
        {
            SetLevel(level.Value);
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to reapply touchpad haptics level", exception);
        }
    }
}
