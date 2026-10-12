using System.Runtime.InteropServices;
namespace HonorPCHelper;

/// <summary>
/// Синхронизация схемы питания с режимом работы ноутбука.
///
/// Схему выбирает прошивка по режиму Fn+P, и делает это не всегда правильно:
/// умный режим нередко остаётся на "Высокой производительности", из-за чего
/// ноутбук на батарее держит высокие частоты и греется. Приложение приводит
/// схему к режиму само - на входе в систему, по событию режима и при смене
/// питания. Изменение схемы текущего пользователя прав администратора не
/// требует, поэтому привилегированная задача здесь не нужна.
/// </summary>
internal static class PowerPlanHelper
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetActiveScheme(nint userPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetActiveScheme(nint userPowerKey, ref Guid schemeGuid);

    private static Guid? GetActivePlan()
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

    private static void SetPlan(Guid scheme)
    {
        try
        {
            PowerSetActiveScheme(nint.Zero, ref scheme);
        }
        catch (Exception exception)
        {
            AppLog.Error("Failed to set power plan", exception);
        }
    }

    /// <summary>Умный режим - сбалансированная схема, производительный - высокая.</summary>
    internal static void SyncWithMode(bool performanceMode)
    {
        var active = GetActivePlan();
        var target = performanceMode ? HighPerformance : Balanced;
        if (active != target)
            SetPlan(target);
    }
}

