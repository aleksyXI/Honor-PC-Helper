using Microsoft.Win32;
using System.Windows.Forms;
namespace HonorPCHelper;
internal static class ScreenControl
{
    private const string RateKey = "screen_rate";
    private const string AutoKey = "screen_auto";
    private const int BatteryRate = 60;
    public static bool IsAuto()
    {
        try { return HardwareSettings.ScreenRateAuto; }
        catch { return false; }
    }
    public static void SetAuto(bool auto)
    {
        try { HardwareSettings.ScreenRateAuto = auto; }
        catch { }
    }
    public static int GetSavedRate()
    {
        try { return HardwareSettings.ScreenRefreshRate ?? 0; }
        catch { return 0; }
    }
    public static int GetCurrentRate()
    {
        try
        {
            var screen = ScreenNative.FindLaptopScreen();
            return ScreenNative.GetRefreshRate(screen);
        }
        catch { return -1; }
    }
    public static int GetMaxRate()
    {
        try
        {
            var screen = ScreenNative.FindLaptopScreen();
            return ScreenNative.GetMaxRefreshRate(screen);
        }
        catch { return 0; }
    }
    public static void SetRate(int frequency, bool showOsd = true, bool save = true)
    {
        try
        {
            var screen = ScreenNative.FindLaptopScreen();
            if (screen is null)
                return;
            var current = ScreenNative.GetRefreshRate(screen);
            if (frequency <= 0)
            {
                if (showOsd && current > 0) ShowOsd(current);
                return;
            }
            if (save) SetSavedRate(frequency);
            if (frequency == current)
            {
                if (showOsd) ShowOsd(frequency);
                return;
            }
            ScreenNative.SetRefreshRate(screen, frequency);
            Thread.Sleep(700);
            if (showOsd) ShowOsd(frequency);
        }
        catch (Exception ex)
        {
            AppLog.Error("SetRate failed", ex);
        }
    }
    private static void SetSavedRate(int frequency)
    {
        try { HardwareSettings.ScreenRefreshRate = frequency; }
        catch { }
    }
    public static void Restore()
    {
        try
        {
            var online = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            if (IsAuto())
            {
                var target = online ? GetMaxRate() : BatteryRate;
                if (target > 0 && target != GetCurrentRate())
                    SetRate(target, showOsd: false, save: false);
            }
            else if (GetSavedRate() is { } saved && saved > 0 && saved != GetCurrentRate())
            {
                SetRate(saved, showOsd: false, save: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("ScreenControl.Restore failed", ex);
        }
    }
    private static void ShowOsd(int frequency)
    {
        try
        {
            Program.ShowOsd?.Invoke("HZChange", "🖥️", $"{frequency} Гц");
        }
        catch { }
    }
}
