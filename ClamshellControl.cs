using System.Runtime.InteropServices;

namespace HonorPCHelper;

internal static class ClamshellControl
{
    private static readonly Guid ButtonSubgroup = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid LidActionSetting = new("5CA83367-6E45-459F-A27B-476B1D01C936");
    private const uint LidActionNone = 0;
    private const uint LidActionSleep = 1;

    [DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetActiveScheme(nint userPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint root, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(nint root, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(nint root, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value);

    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetActiveScheme(nint userPowerKey, ref Guid schemeGuid);

    private static Guid? GetActiveScheme()
    {
        if (PowerGetActiveScheme(nint.Zero, out nint ptr) != 0 || ptr == nint.Zero)
            return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void SetLidAction(uint action)
    {
        var scheme = GetActiveScheme();
        if (scheme is not { } active)
            return;
        PowerWriteACValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, action);
        PowerWriteDCValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, action);
        PowerSetActiveScheme(nint.Zero, ref active);
    }

    private static uint GetLidAction()
    {
        if (GetActiveScheme() is not { } active)
            return LidActionSleep;
        return PowerReadACValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, out var value) == 0 ? value : LidActionSleep;
    }

    internal static void Enable()
    {
        if (GetLidAction() != LidActionNone)
            SetLidAction(LidActionNone);
    }

    internal static void Disable()
    {
        if (GetLidAction() != LidActionSleep)
            SetLidAction(LidActionSleep);
    }
}
