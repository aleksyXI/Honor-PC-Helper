namespace HonorPCHelper;

/// <summary>
/// Частота обновления встроенного экрана.
///
/// Windows её не запоминает: после сна и перезагрузки панель возвращается к
/// прежней, а на батарее держит максимальную и лишний раз греется. Выбранную
/// частоту приложение хранит у себя и возвращает на место; в автовыборе она
/// следует источнику питания. Прав администратора не нужно - режим меняет
/// сам пользователь.
/// </summary>
internal static class ScreenControl
{
    private const int BatteryRate = 60;

    internal static bool IsAuto() => HardwareSettings.ScreenRateAuto;

    internal static void SetAuto(bool auto) => HardwareSettings.ScreenRateAuto = auto;

    /// <summary>Максимальная частота панели, доступная в текущем режиме экрана.</summary>
    internal static int GetMaxRate()
    {
        try
        {
            return ScreenNative.GetMaxRefreshRate(ScreenNative.FindLaptopScreen());
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the maximum refresh rate", exception);
            return 0;
        }
    }

    internal static int GetCurrentRate()
    {
        try
        {
            return ScreenNative.GetRefreshRate(ScreenNative.FindLaptopScreen());
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the current refresh rate", exception);
            return -1;
        }
    }

    private static int GetSavedRate()
    {
        try
        {
            return HardwareSettings.ScreenRefreshRate ?? 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not read the saved refresh rate", exception);
            return 0;
        }
    }

    /// <summary>
    /// Переключает частоту. Режим применяется не мгновенно, поэтому плашка
    /// показывается после короткой паузы - иначе на ней была бы прежняя частота.
    /// </summary>
    internal static void SetRate(int frequency, bool showOsd = true, bool save = true)
    {
        try
        {
            var screen = ScreenNative.FindLaptopScreen();
            if (screen is null)
                return;

            var current = ScreenNative.GetRefreshRate(screen);
            if (frequency <= 0)
            {
                if (showOsd && current > 0)
                    ShowOsd(current);
                return;
            }

            if (save)
                HardwareSettings.ScreenRefreshRate = frequency;

            if (frequency == current)
            {
                if (showOsd)
                    ShowOsd(frequency);
                return;
            }

            ScreenNative.SetRefreshRate(screen, frequency);
            Thread.Sleep(700);
            if (showOsd)
                ShowOsd(frequency);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not set the refresh rate", exception);
        }
    }

    /// <summary>
    /// Возвращает частоту после сна, смены питания и запуска приложения: в
    /// автовыборе - по источнику питания, иначе - выбранную человеком.
    /// </summary>
    internal static void Restore()
    {
        try
        {
            var online = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            var target = IsAuto()
                ? online ? GetMaxRate() : BatteryRate
                : GetSavedRate();

            if (target > 0 && target != GetCurrentRate())
                SetRate(target, showOsd: false, save: false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not restore the refresh rate", exception);
        }
    }

    private static void ShowOsd(int frequency)
        => Program.ShowOsd?.Invoke("HZChange", "🖥️", $"{frequency} Гц", null);
}
