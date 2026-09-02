namespace HonorPCHelper;
internal static class PerformanceModePolicy
{
    internal static bool CanEnable(out string reason)
    {
        var status = SystemInformation.PowerStatus;
        const string ru = "Компьютер должен быть подключен к источнику питания и заряд аккумулятора больше 20 процентов";
        if (status.PowerLineStatus != PowerLineStatus.Online)
        {
            reason = L.T(ru, "The computer must be connected to a power source and the battery charge must be above 20 percent", "电脑必须连接电源，且电池电量高于20%");
            return false;
        }
        if (status.BatteryLifePercent >= 0 && status.BatteryLifePercent < 0.20f)
        {
            reason = L.T(ru, "The computer must be connected to a power source and the battery charge must be above 20 percent", "电脑必须连接电源，且电池电量高于20%");
            return false;
        }
        if (status.BatteryLifePercent < 0)
            AppLog.Info("Battery charge is unknown; performance mode allowed because AC power is connected");
        reason = string.Empty;
        return true;
    }
}
