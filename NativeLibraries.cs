using System.Reflection;
using System.Runtime.InteropServices;

namespace HonorPCHelper;

internal static class NativeLibraries
{
    private static readonly string[] HonorLibs = { "HardwareHal.dll", "IPCMessage.dll", "Utils.dll" };
    private static readonly string[] VcLibs = { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
    private static bool _loaded;

    internal static void LoadAll()
    {
        if (_loaded)
            return;
        _loaded = true;

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HonorPCHelper", "native");
            Directory.CreateDirectory(dir);
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var name in HonorLibs.Concat(VcLibs))
            {
                var systemPath = Path.Combine(Environment.SystemDirectory, name);
                if (VcLibs.Contains(name) && File.Exists(systemPath))
                    continue;

                var target = Path.Combine(dir, name);
                try
                {
                    using var stream = assembly.GetManifestResourceStream($"HonorPCHelper.Native.{name}");
                    if (stream is null)
                        continue;
                    if (!File.Exists(target) || new FileInfo(target).Length != stream.Length)
                    {
                        using (stream)
                        using (var fs = File.Create(target))
                            stream.CopyTo(fs);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to extract {name}", ex);
                    continue;
                }

                try
                {
                    NativeMethods.SetDllDirectory(dir);
                    NativeMethods.LoadLibrary(Path.Combine(dir, name));
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load {name}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("NativeLibraries.LoadAll failed", ex);
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadLibrary(string lpFileName);
    }
}
