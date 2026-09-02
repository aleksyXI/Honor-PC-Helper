namespace HonorPCHelper;
internal static class TouchpadMenu
{
    internal static void Build(NativePopupMenu menu)
    {
        menu.AddItem(L.T("Интенсивность вибрации", "Vibration strength", "振动强度"), null, enabled: false);
        TouchpadHapticsMenu.Build(menu);
        menu.AddSeparator();
        menu.AddItem(L.T("Жесты на краях тачпада", "Touchpad edge gestures", "触控板边缘手势"), null, enabled: false);
        TouchpadGesturesMenu.Build(menu);
    }
}
