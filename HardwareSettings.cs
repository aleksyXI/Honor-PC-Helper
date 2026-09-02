using Microsoft.Win32;
namespace HonorPCHelper;
internal static class HardwareSettings
{
    private const string RegistryPath = @"Software\HonorPCHelper";
    private const string KeyboardBacklightValue = "KeyboardBacklightLevel";
    private const string KeyboardBacklightTimeoutValue = "KeyboardBacklightTimeout";
    private const ushort DefaultBacklightTimeout = 60;
    private const string PerformanceModeValue = "PerformanceModeActive";
    private const string BatteryProtectionValue = "BatteryProtectionMode";
    private const string PreferredBatteryProtectionValue = "PreferredBatteryProtectionMode";
    private const string PowerUnlockValue = "PowerUnlockEnabled";
    private const string BacklightScheduleEnabledValue = "BacklightScheduleEnabled";
    private const string BacklightOnHourValue = "BacklightOnHour";
    private const string BacklightOffHourValue = "BacklightOffHour";
    private const string BacklightScheduleLevelValue = "BacklightScheduleLevel";
    private const string TouchpadHapticsValue = "TouchpadHapticsLevel";
    private const string TouchpadEdgeGesturePrefix = "TouchpadEdgeGesture.";
    private const string ScreenRefreshRateValue = "ScreenRefreshRate";
    private const string ScreenRateAutoValue = "ScreenRateAuto";
    private const string ClamshellModeValue = "ClamshellMode";
    private const string PendingHardwareCommandValue = "PendingHardwareCommand";
    private const string SensorSnapshotValue = "SensorSnapshot";
    private static readonly Lock Gate = new();
    private static RegistryKey? _key;
    private static RegistryKey Key() => _key ??= Registry.CurrentUser.CreateSubKey(RegistryPath, true);
    internal readonly record struct TooltipState(
        string? SensorSnapshot,
        bool PerformanceModeActive,
        KeyboardBacklightLevel? KeyboardBacklight,
        BatteryProtectionMode? BatteryProtection);
    internal static TooltipState ReadTooltipState()
    {
        lock (Gate)
        {
            var key = Key();
            return new TooltipState(
                key.GetValue(SensorSnapshotValue) as string,
                key.GetValue(PerformanceModeValue) as int? is { } performance && performance != 0,
                ParseEnum<KeyboardBacklightLevel>(key.GetValue(KeyboardBacklightValue) as string),
                ParseEnum<BatteryProtectionMode>(key.GetValue(BatteryProtectionValue) as string));
        }
    }
    internal static string? SensorSnapshot
    {
        get => ReadString(SensorSnapshotValue);
        set => WriteString(SensorSnapshotValue, value);
    }
    internal static string? PendingHardwareCommand
    {
        get => ReadString(PendingHardwareCommandValue);
        set => WriteString(PendingHardwareCommandValue, value);
    }
    internal static bool BacklightScheduleEnabled
    {
        get => ReadInt(BacklightScheduleEnabledValue) is { } value && value != 0;
        set => WriteInt(BacklightScheduleEnabledValue, value ? 1 : 0);
    }
    internal static int BacklightOnHour
    {
        get => ReadInt(BacklightOnHourValue) ?? 18;
        set => WriteInt(BacklightOnHourValue, Math.Clamp(value, 0, 23));
    }
    internal static int BacklightOffHour
    {
        get => ReadInt(BacklightOffHourValue) ?? 6;
        set => WriteInt(BacklightOffHourValue, Math.Clamp(value, 0, 23));
    }
    internal static KeyboardBacklightLevel BacklightScheduleLevel
    {
        get => ReadEnum<KeyboardBacklightLevel>(BacklightScheduleLevelValue) is { } level
            && level != KeyboardBacklightLevel.Off
            ? level
            : KeyboardBacklightLevel.Low;
        set => WriteEnum<KeyboardBacklightLevel>(BacklightScheduleLevelValue,
            value == KeyboardBacklightLevel.Off ? KeyboardBacklightLevel.Low : value);
    }

    /// <summary>
    /// Режим, который сейчас в EC. Значение перечитывается при каждом опросе
    /// датчиков, поэтому прошивка, забывшая пороги, его перепишет.
    /// </summary>
    internal static BatteryProtectionMode? BatteryProtection
    {
        get => ReadEnum<BatteryProtectionMode>(BatteryProtectionValue);
        set => WriteEnum(BatteryProtectionValue, value);
    }

    /// <summary>
    /// Режим, выбранный человеком в меню. Опрос датчиков его не трогает -
    /// именно к нему возвращаются пороги после пробуждения.
    /// </summary>
    internal static BatteryProtectionMode? PreferredBatteryProtection
    {
        get => ReadEnum<BatteryProtectionMode>(PreferredBatteryProtectionValue);
        set => WriteEnum(PreferredBatteryProtectionValue, value);
    }

    /// <summary>
    /// Переносит выбор, сделанный до появления отдельного значения: при первом
    /// запуске новой сборки в BatteryProtectionMode лежит последний режим,
    /// который пользователь видел в меню.
    /// </summary>
    internal static void SeedPreferredBatteryProtection()
    {
        lock (Gate)
        {
            var key = Key();
            if (key.GetValue(PreferredBatteryProtectionValue) is not null)
                return;
            if (key.GetValue(BatteryProtectionValue) is string current)
                key.SetValue(PreferredBatteryProtectionValue, current, RegistryValueKind.String);
        }
    }

    internal static bool? PowerUnlock
    {
        get => ReadInt(PowerUnlockValue) is { } value ? value != 0 : null;
        set => WriteInt(PowerUnlockValue, value.HasValue ? value.Value ? 1 : 0 : null);
    }
    internal static KeyboardBacklightLevel? KeyboardBacklight
    {
        get => ReadEnum<KeyboardBacklightLevel>(KeyboardBacklightValue);
        set => WriteEnum(KeyboardBacklightValue, value);
    }
    internal static ushort KeyboardBacklightTimeout
    {
        get => ReadInt(KeyboardBacklightTimeoutValue) is { } value and >= 0 and <= ushort.MaxValue
            ? (ushort)value
            : DefaultBacklightTimeout;
        set => WriteInt(KeyboardBacklightTimeoutValue, value);
    }
    internal static TouchpadHapticsLevel? TouchpadHaptics
    {
        get => ReadEnum<TouchpadHapticsLevel>(TouchpadHapticsValue);
        set => WriteEnum(TouchpadHapticsValue, value);
    }
    internal static bool PerformanceModeActive
    {
        get => ReadInt(PerformanceModeValue) is { } value && value != 0;
        set => WriteInt(PerformanceModeValue, value ? 1 : 0);
    }
    internal static bool? GetTouchpadEdgeGesture(TouchpadEdgeGesture gesture)
        => ReadInt(TouchpadEdgeGesturePrefix + gesture) is { } value ? value != 0 : null;
    internal static void SetTouchpadEdgeGesture(TouchpadEdgeGesture gesture, bool? enabled)
        => WriteInt(TouchpadEdgeGesturePrefix + gesture, enabled.HasValue ? enabled.Value ? 1 : 0 : null);
    internal static int? ScreenRefreshRate
    {
        get
        {
            if (ReadInt(ScreenRefreshRateValue) is { } current)
                return current;
            if (ReadInt("screen_rate") is { } legacy)
            {
                WriteInt(ScreenRefreshRateValue, legacy);
                return legacy;
            }
            return null;
        }
        set => WriteInt(ScreenRefreshRateValue, value);
    }
    internal static bool ScreenRateAuto
    {
        get
        {
            if (ReadInt(ScreenRateAutoValue) is { } current)
                return current != 0;
            return ReadInt("screen_auto") is { } legacy && legacy != 0;
        }
        set => WriteInt(ScreenRateAutoValue, value ? 1 : 0);
    }
    internal static bool ClamshellMode
    {
        get => ReadInt(ClamshellModeValue) is { } value && value != 0;
        set => WriteInt(ClamshellModeValue, value ? 1 : 0);
    }
    private static string? ReadString(string name)
    {
        lock (Gate)
            return Key().GetValue(name) as string;
    }
    private static void WriteString(string name, string? value)
    {
        lock (Gate)
        {
            if (value is null)
                Key().DeleteValue(name, false);
            else
                Key().SetValue(name, value, RegistryValueKind.String);
        }
    }
    private static int? ReadInt(string name)
    {
        lock (Gate)
            return Key().GetValue(name) as int?;
    }
    private static void WriteInt(string name, int? value)
    {
        lock (Gate)
        {
            if (value is null)
                Key().DeleteValue(name, false);
            else
                Key().SetValue(name, value.Value, RegistryValueKind.DWord);
        }
    }
    private static T? ReadEnum<T>(string name) where T : struct, Enum
        => ParseEnum<T>(ReadString(name));
    private static void WriteEnum<T>(string name, T? value) where T : struct, Enum
        => WriteString(name, value?.ToString());
    private static T? ParseEnum<T>(string? value) where T : struct, Enum
        => Enum.TryParse<T>(value, out var parsed) ? parsed : null;
}
