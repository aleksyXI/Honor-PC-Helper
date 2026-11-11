using System.Runtime.InteropServices;

namespace HonorPCHelper;

/// <summary>
/// Работа ноутбука с закрытой крышкой (clamshell).
///
/// Действие закрытия крышки задаётся в активной схеме питания: пока там стоит
/// "сон", ноутбук с подключённым внешним монитором уходит в сон и работу
/// продолжать не может. Значение лежит в разделе кнопок схемы (LIDACTION) и
/// принадлежит системе, поэтому запись идёт через привилегированную задачу.
///
/// Меняется только значение "от сети": на батарее ноутбук обязан засыпать,
/// иначе он остаётся работать в сумке. Действие, которое стояло у человека,
/// запоминается и возвращается, когда режим снимается.
/// </summary>
internal static class ClamshellControl
{
    private static readonly Guid ButtonSubgroup = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid LidActionSetting = new("5CA83367-6E45-459F-A27B-476B1D01C936");
    private const uint LidActionNothing = 0;
    private const uint LidActionSleep = 1;

    [DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetActiveScheme(nint userPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint root, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint root, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, out uint value);

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

    /// <summary>Действие крышки при питании от сети; сон, если схема не читается.</summary>
    internal static uint GetAcAction()
    {
        if (GetActiveScheme() is not { } active)
            return LidActionSleep;
        return PowerReadACValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, out var value) == 0
            ? value
            : LidActionSleep;
    }

    private static uint GetDcAction()
    {
        if (GetActiveScheme() is not { } active)
            return LidActionSleep;
        return PowerReadDCValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, out var value) == 0
            ? value
            : LidActionSleep;
    }

    /// <summary>
    /// Действие, которое вернётся при снятии режима. Запоминается при первом
    /// включении; если человек ничего не выбирал, это сон.
    /// </summary>
    private static uint RestoreAction =>
        HardwareSettings.ClamshellDefaultLidAction is { } saved && saved is >= 1 and <= 3
            ? (uint)saved
            : LidActionSleep;

    /// <summary>
    /// Включает режим: крышка ничего не делает, пока ноутбук на зарядке.
    /// Стоящее до этого действие запоминается - его вернёт выключение.
    /// </summary>
    internal static void Enable()
    {
        var current = GetAcAction();
        if (HardwareSettings.ClamshellDefaultLidAction is null)
            HardwareSettings.ClamshellDefaultLidAction = current is >= 1 and <= 3 ? (int)current : (int)LidActionSleep;

        if (current != LidActionNothing)
            WriteAcAction(LidActionNothing);

        HardwareSettings.ClamshellEngaged = true;
        RepairBatterySide();
    }

    /// <summary>
    /// Снимает режим: возвращает действие, стоявшее до включения. Запись идёт,
    /// только если режим включало приложение - иначе затиралась бы настройка,
    /// выставленная человеком вручную.
    /// </summary>
    internal static void Disable()
    {
        if (!HardwareSettings.ClamshellEngaged)
            return;

        var restore = RestoreAction;
        if (GetAcAction() != restore)
            WriteAcAction(restore);

        RepairBatterySide();
        HardwareSettings.ClamshellEngaged = false;
    }

    /// <summary>
    /// Прежние сборки писали "ничего не делать" и в ветку от батареи, не
    /// запоминая исходное действие. Такой ноутбук не засыпает с закрытой
    /// крышкой ни от сети, ни от батареи - то есть работает в сумке.
    /// Состояние переносится: режим считается включённым, возвращаемое
    /// действие - сон, а ветка от батареи приводится в порядок при снятии.
    /// </summary>
    internal static void AdoptLegacyState()
    {
        if (!HardwareSettings.ClamshellMode
            || HardwareSettings.ClamshellEngaged
            || HardwareSettings.ClamshellDefaultLidAction is not null
            || GetAcAction() != LidActionNothing)
            return;

        HardwareSettings.ClamshellDefaultLidAction = (int)LidActionSleep;
        HardwareSettings.ClamshellEngaged = true;
        AppLog.Info("Adopted the clamshell state left by an older build");
    }

    /// <summary>Точка входа команды --apply-clamshell.</summary>
    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    /// <summary>
    /// Возвращает ветку "от батареи" к действию из настроек, если там осталось
    /// "ничего не делать": от батареи ноутбук обязан засыпать.
    /// </summary>
    private static void RepairBatterySide()
    {
        if (GetDcAction() != LidActionNothing || GetActiveScheme() is not { } active)
            return;
        PowerWriteDCValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, RestoreAction);
        PowerSetActiveScheme(nint.Zero, ref active);
    }

    private static void WriteAcAction(uint action)
    {
        if (GetActiveScheme() is not { } active)
            return;
        PowerWriteACValueIndex(nint.Zero, active, ButtonSubgroup, LidActionSetting, action);
        // Запись в схему не применяется, пока схема не активирована повторно.
        PowerSetActiveScheme(nint.Zero, ref active);
    }
}
