using System.ComponentModel;
using System.Runtime.InteropServices;
namespace HonorPCHelper
{
    internal static class ScreenNative
    {
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const string DefaultDevice = @"\\.\DISPLAY1";
        private static string? _internalName;
        private static int _maxRateCache;
        public static bool IsExternalDisplayConnected(bool log = false)
        {
            try
            {
                string? internalName = _internalName;
                foreach (var device in GetAllDevices())
                {
                    if (device.outputTechnology != DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL &&
                        device.outputTechnology != DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED &&
                        device.monitorFriendlyDeviceName != internalName)
                    {
                        if (log) AppLog.Info("Found external screen: " + device.monitorFriendlyDeviceName + ":" + device.outputTechnology);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Info(ex.ToString());
            }
            return false;
        }
        private static bool IsInternalDisplay(DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME device)
        {
            string? internalName = _internalName;
            return device.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                || device.outputTechnology == DisplayNative.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                || device.monitorFriendlyDeviceName == internalName;
        }
        public static IEnumerable<DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME> GetAllDevices()
        {
            var err = DisplayNative.GetDisplayConfigBufferSizes(
                DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != 0) throw new Win32Exception(err);
            var paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
            err = DisplayNative.QueryDisplayConfig(
                DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (err != 0) throw new Win32Exception(err);
            for (int i = 0; i < modeCount; i++)
            {
                if (modes[i].infoType != DisplayNative.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
                    continue;
                var deviceName = new DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                deviceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                deviceName.header.size = (uint)Marshal.SizeOf(deviceName);
                deviceName.header.adapterId = modes[i].adapterId;
                deviceName.header.id = modes[i].id;
                err = DisplayNative.DisplayConfigGetDeviceInfo(ref deviceName);
                if (err == 0)
                    yield return deviceName;
                else
                    AppLog.Info("DisplayConfigGetDeviceInfo error: " + new Win32Exception(err).Message);
            }
        }
        public static string? FindLaptopScreen(bool log = false)
        {
            try
            {
                var err = DisplayNative.GetDisplayConfigBufferSizes(
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (err != 0) throw new Win32Exception(err);
                var paths = new DisplayNative.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DisplayNative.DISPLAYCONFIG_MODE_INFO[modeCount];
                err = DisplayNative.QueryDisplayConfig(
                    DisplayNative.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                    ref pathCount, paths, ref modeCount, modes, nint.Zero);
                if (err != 0) throw new Win32Exception(err);
                foreach (var path in paths)
                {
                    var targetName = new DisplayNative.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    targetName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = (uint)Marshal.SizeOf(targetName);
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;
                    if (DisplayNative.DisplayConfigGetDeviceInfo(ref targetName) != 0) continue;
                    if (!IsInternalDisplay(targetName)) continue;
                    if (log) AppLog.Info(targetName.monitorDevicePath + " " + targetName.outputTechnology);
                    _internalName = targetName.monitorFriendlyDeviceName;
                    var sourceName = new DisplayNative.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    sourceName.header.type = DisplayNative.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    sourceName.header.size = (uint)Marshal.SizeOf(sourceName);
                    sourceName.header.adapterId = path.sourceInfo.adapterId;
                    sourceName.header.id = path.sourceInfo.id;
                    if (DisplayNative.DisplayConfigGetDeviceInfo(ref sourceName) == 0)
                        return ExtractDisplay(sourceName.viewGdiDeviceName);
                    return Screen.PrimaryScreen?.DeviceName;
                }
                if (log) AppLog.Info("Internal screen off");
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Info(ex.Message);
                return null;
            }
        }
        public static int GetMaxRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;
            var dm = CreateDevmode();
            int frequency = -1;
            int i = 0;
            while (DisplayNative.EnumDisplaySettingsEx(laptopScreen, i, ref dm) != 0)
            {
                if (dm.dmDisplayFrequency > frequency) frequency = dm.dmDisplayFrequency;
                i++;
            }
            if (frequency > 0) _maxRateCache = frequency;
            else frequency = _maxRateCache;
            return frequency;
        }
        public static int GetRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;
            var dm = CreateDevmode();
            return DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm) != 0
                ? dm.dmDisplayFrequency
                : -1;
        }
        public static int SetRefreshRate(string laptopScreen, int frequency = 120)
        {
            var dm = CreateDevmode();
            if (DisplayNative.EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm) == 0) return 0;
            if (dm.dmDisplayFrequency == frequency) return 0;
            dm.dmDisplayFrequency = frequency;
            int result = DisplayNative.ChangeDisplaySettingsEx(
                laptopScreen, ref dm, IntPtr.Zero, DisplayNative.DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
            AppLog.Info("Screen = " + frequency + "Hz : " + (result == 0 ? "OK" : result));
            return result;
        }
        public static DisplayNative.DEVMODE CreateDevmode()
        {
            var dm = new DisplayNative.DEVMODE
            {
                dmDeviceName = new string(new char[32]),
                dmFormName = new string(new char[32]),
            };
            dm.dmSize = (short)Marshal.SizeOf(dm);
            return dm;
        }
        private static string ExtractDisplay(string input)
        {
            int index = input.IndexOf((char)92, 4);
            return index != -1 ? input.Substring(0, index) : input;
        }
    }
}
