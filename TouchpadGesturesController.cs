namespace HonorPCHelper;
internal enum TouchpadEdgeGesture : byte
{
    Brightness = TouchpadVendorLink.EdgeBrightnessCommand,
    Volume = TouchpadVendorLink.EdgeVolumeCommand
}
internal static class TouchpadGesturesController
{
    private static readonly TouchpadEdgeGesture[] All =
        [TouchpadEdgeGesture.Brightness, TouchpadEdgeGesture.Volume];
    internal static void SetEnabled(TouchpadEdgeGesture gesture, bool enabled)
        => TouchpadVendorLink.Send((byte)gesture, enabled ? (byte)1 : (byte)0);
    internal static bool IsEnabled(TouchpadEdgeGesture gesture)
        => HardwareSettings.GetTouchpadEdgeGesture(gesture) ?? true;
    internal static void Apply(TouchpadEdgeGesture gesture, bool enabled)
    {
        SetEnabled(gesture, enabled);
        HardwareSettings.SetTouchpadEdgeGesture(gesture, enabled);
    }
    internal static void Reapply()
    {
        foreach (var gesture in All)
        {
            var enabled = HardwareSettings.GetTouchpadEdgeGesture(gesture);
            if (enabled is null)
                continue;
            try
            {
                SetEnabled(gesture, enabled.Value);
            }
            catch (Exception exception)
            {
                AppLog.Error($"Failed to reapply touchpad edge gesture {gesture}", exception);
            }
        }
    }
}
