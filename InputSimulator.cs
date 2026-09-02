using System.Runtime.InteropServices;
namespace HonorPCHelper;
internal static class InputSimulator
{
    private const uint KeyEventfExtendedKey = 0x0001;
    private const uint KeyEventfKeyUp = 0x0002;
    private const byte VkSnapshot = 0x2C;
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    internal static void PressPrintScreen()
    {
        byte scan = (byte)(MapVirtualKey(VkSnapshot, 0) & 0xFF);
        keybd_event(VkSnapshot, scan, KeyEventfExtendedKey, 0);
        keybd_event(VkSnapshot, scan, KeyEventfExtendedKey | KeyEventfKeyUp, 0);
    }
}
