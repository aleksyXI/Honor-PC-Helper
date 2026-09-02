using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
namespace HonorPCHelper;
internal static class MicControl
{
    public static int SetMicMute(bool mute)
    {
        int affected = 0;
        var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            device.AudioEndpointVolume.Mute = mute;
            affected++;
        }
        return affected;
    }
    public static bool IsMicMuted()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                return device.AudioEndpointVolume.Mute;
        }
        catch { }
        return false;
    }
    [DllImport("HardwareHal.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetMicLightStatus(bool muted);
    public static void SetLight(bool muted)
    {
        try { SetMicLightStatus(muted); }
        catch (DllNotFoundException) { AppLog.Info("HardwareHal.dll не найдена рядом с приложением"); }
        catch (EntryPointNotFoundException) { }
    }
}
