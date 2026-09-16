namespace HonorPCHelper;
internal sealed class HelperApplicationContext : ApplicationContext
{
    private const int SensorRefreshIntervalMilliseconds = 5_000;
    private const int TooltipMinIntervalMilliseconds = 750;
    private readonly MessageWindow _window;
    private readonly Control _uiDispatcher;
    private readonly OSDToast? _osdToast;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _trayHoverTimer;
    private readonly IntPtr _tooltipHandle;
    private readonly TouchpadBrightnessService _touchpadService;
    private PowerModeEventService? _powerModeEvents;
    private readonly BacklightScheduleService _backlightSchedule;
    private readonly BatteryProtectionService _batteryProtection;
    private Icon _trayIcon;
    private IntPtr _tooltipText;
    private bool _tooltipAdded;
    private bool _disposed;
    private int _sensorRefreshInProgress;
    private long _lastSensorRefresh = -SensorRefreshIntervalMilliseconds;
    private long _suppressBacklightEventsUntil;
    private long _lastResumeRestore = -ResumeDebounceMilliseconds;
    private long _lastModeErrorTime = -3000;
    private IntPtr _displayNotification;
    private IntPtr _hidNotification;
    private readonly System.Windows.Forms.Timer _deviceChangeTimer;
    private Point _lastTrayMousePosition;
    private string _tooltipCache = string.Empty;
    private long _lastTooltipUpdate;
    private const int ResumeSettleMilliseconds = 8000;
    private const int ResumeDebounceMilliseconds = 10000;
    private readonly Task<IReadOnlyList<DriverComponent>> _driverComponentsTask;
    internal HelperApplicationContext()
    {
        _window = new MessageWindow(OnMenuTooltip, OnDisplayResume, OnHidDeviceChange);
        _uiDispatcher = new Control();
        _ = _uiDispatcher.Handle;
        _tooltipHandle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExTopmost, "tooltips_class32", null,
            NativeMethods.WsPopup | NativeMethods.TtsNoPrefix | NativeMethods.TtsAlwaysTip,
            NativeMethods.CwUseDefault, NativeMethods.CwUseDefault,
            NativeMethods.CwUseDefault, NativeMethods.CwUseDefault,
            _window.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmSetMaxTipWidth, 0, 300);
        NativeMethods.SetWindowPos(
            _tooltipHandle,
            NativeMethods.HwndTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        _backlightSchedule = new BacklightScheduleService();
        _batteryProtection = new BatteryProtectionService();
        // Выбор, сделанный до появления отдельного значения в реестре,
        // переносится до первого опроса датчиков - иначе пороги, потерянные
        // прошивкой, успеют его перезаписать.
        HardwareSettings.SeedPreferredBatteryProtection();
        _trayIcon = TrayIconFactory.Create(HardwareSettings.PerformanceModeActive);
        _tooltipCache = "Honor PC Helper";
        _lastTooltipUpdate = 0;
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = _tooltipCache,
            Visible = true
        };
        _notifyIcon.MouseMove += OnTrayIconMouseMove;
        _notifyIcon.MouseClick += OnTrayIconMouseClick;
        _trayHoverTimer = new System.Windows.Forms.Timer
        {
            Interval = SensorRefreshIntervalMilliseconds
        };
        _trayHoverTimer.Tick += OnTrayHoverTimerTick;
        _deviceChangeTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _deviceChangeTimer.Tick += OnDeviceChangeTimerTick;
        _touchpadService = new TouchpadBrightnessService(ShowError);
        _hidNotification = NativeMethods.RegisterHidDeviceNotification(_window.Handle);
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _displayNotification = NativeMethods.RegisterPowerSettingNotification(
            _window.Handle, NativeMethods.GuidConsoleDisplayState, NativeMethods.DeviceNotifyWindowHandle);
        _osdToast = new OSDToast();
        Program.ShowOsd = (icon, emoji, text) => _osdToast?.Show(icon, emoji, text);
        _backlightSchedule.Start();
        PowerPlanHelper.SyncWithMode(HardwareSettings.PerformanceModeActive);
        _driverComponentsTask = new DriverUpdateService().BuildDeviceListAsync();
        _ = _driverComponentsTask.ContinueWith(
            task => AppLog.Error("Background driver device inventory failed", task.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        _ = Task.Run(StartHardwareServices);
    }

    /// <summary>
    /// Всё, что обращается к WMI, HID и CCD, поднимается в фоне: перечисление
    /// HID-устройств и подключение к root\wmi занимают сотни миллисекунд,
    /// а значок в трее должен появляться сразу после запуска.
    /// </summary>
    private void StartHardwareServices()
    {
        if (_disposed)
            return;
        _touchpadService.Start();
        TouchpadHapticsController.IsSupported();
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
        try
        {
            var events = new PowerModeEventService(
                HandlePowerModeChanged,
                HandleKeyboardBacklightChanged,
                ShouldIgnoreBacklightEvent,
                HandleCameraChanged,
                HandleMicChanged,
                HandleScreenshot,
                HandleTouchpadChanged,
                HandleHonorApp);
            events.Start();
            _powerModeEvents = events;
            if (_disposed)
            {
                events.Dispose();
                return;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not start HONOR WMI event monitoring", exception);
            ShowError(L.T(
                $"Не удалось отслеживать Fn+P: {exception.Message}",
                $"Failed to monitor Fn+P: {exception.Message}",
                $"无法监听 Fn+P：{exception.Message}"));
        }
        ScreenControl.Restore();
        if (HardwareSettings.ClamshellMode)
            ClamshellControl.Enable();
        StopHonorServices();
        _ = RefreshSensorsAsync();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _touchpadService.Dispose();
            _powerModeEvents?.Dispose();
            _osdToast?.Dispose();
            _backlightSchedule.Dispose();
            _batteryProtection.Dispose();
            HideNativeTooltip();
            if (_tooltipHandle != IntPtr.Zero)
                NativeMethods.DestroyWindow(_tooltipHandle);
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_displayNotification != IntPtr.Zero)
            {
                NativeMethods.UnregisterPowerSettingNotification(_displayNotification);
                _displayNotification = IntPtr.Zero;
            }
            if (_hidNotification != IntPtr.Zero)
            {
                NativeMethods.UnregisterDeviceNotification(_hidNotification);
                _hidNotification = IntPtr.Zero;
            }
            _uiDispatcher.Dispose();
            _trayHoverTimer.Dispose();
            _deviceChangeTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _window.DestroyHandle();
        }
        base.Dispose(disposing);
    }
    private NativePopupMenu CreateMenu()
    {
        var menu = new NativePopupMenu();
        var batterySub = menu.AddSubMenu(
            L.T("Ограничение заряда", "Charge limit", "充电限制"),
            L.T("Ограничение диапазона заряда для продления срока службы батареи.",
                "Limit the charge range to extend battery lifespan.",
                "限制充电区间，延长电池寿命。"));
        BatteryProtectionMenu.Build(batterySub);
        var backlightSub = menu.AddSubMenu(
            L.T("Клавиатура", "Keyboard", "键盘"),
            L.T("Уровень подсветки, таймаут и автоматическое расписание.",
                "Backlight level, timeout and automatic schedule.",
                "背光亮度、超时时间和自动计划。"));
        KeyboardBacklightMenu.Build(backlightSub, _backlightSchedule);
        if (TouchpadHapticsController.IsSupported())
        {
            var touchpadSub = menu.AddSubMenu(
                L.T("Тачпад", "Touchpad", "触控板"),
                L.T("Интенсивность виброотклика и жесты на краях тачпада.",
                    "Vibration strength and touchpad edge gestures.",
                    "振动反馈强度和触控板边缘手势。"));
            TouchpadMenu.Build(touchpadSub);
        }
        var rateSub = menu.AddSubMenu(
            L.T("Частота экрана", "Screen refresh rate", "屏幕刷新率"),
            L.T("Частота обновления дисплея (макс. на сети, 60 Гц от батареи).",
                "Display refresh rate (max on AC, 60 Hz on battery).",
                "显示器刷新率（电源时最高，电池时 60Hz）。"));
        int maxRate = ScreenControl.GetMaxRate();
        int currentRate = ScreenControl.GetCurrentRate();
        if (maxRate > 60)
        {
            rateSub.AddItem($"{maxRate} Гц", () => ScreenControl.SetRate(maxRate),
                @checked: currentRate == maxRate);
            rateSub.AddItem("60 Гц", () => ScreenControl.SetRate(60),
                @checked: currentRate == 60);
        }
        else
        {
            rateSub.AddItem("60 Гц", () => ScreenControl.SetRate(60),
                @checked: currentRate == 60);
        }
        rateSub.AddItem(
            L.T("Авто: макс на сети / 60 от батареи", "Auto: max on AC / 60 on battery", "自动：电源最高/电池60"),
            () => ScreenControl.SetAuto(!ScreenControl.IsAuto()),
            @checked: ScreenControl.IsAuto());

        menu.AddItem(
            L.T("Clamshell: работать с закрытой крышкой", "Clamshell mode: work with lid closed", "合盖模式：合盖继续工作"),
            () => SetClamshell(!HardwareSettings.ClamshellMode),
            @checked: HardwareSettings.ClamshellMode);

        PowerUnlockMenu.Build(menu, UpdateTrayIcon);

        menu.AddItem(
            L.T("Драйвера", "Drivers", "驱动程序"),
            ShowDriverManager,
            tooltip: L.T(
                "Проверка и установка драйверов и прошивок с сервера HONOR.",
                "Check and install drivers and firmware from HONOR.",
                "从 HONOR 检查并安装驱动程序和固件。"));
        menu.AddSeparator();
        menu.AddItem(
            L.T("Остановить все службы HONOR", "Stop all HONOR services", "停止所有荣耀服务"),
            StopHonorServices);
        menu.AddItem(
            L.T("Запускать вместе с Windows", "Start with Windows", "开机自启动"),
            ToggleStartup,
            @checked: StartupManager.IsEnabled);
        menu.AddItem(L.T("Выход", "Exit", "退出"), ExitThread);
        return menu;
    }
    private void ShowDriverManager()
    {
        var form = Application.OpenForms.OfType<DriverManagerForm>().FirstOrDefault();
        if (form is null || form.IsDisposed)
        {
            form = new DriverManagerForm(_driverComponentsTask);
            form.Show();
            form.Activate();
            form.BringToFront();
        }
        else
        {
            form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }
    }

    private void SetClamshell(bool enabled)
    {
        if (enabled && !ScreenNative.IsExternalDisplayConnected())
        {
            _osdToast?.Show("imageIcon", "🖥️",
                L.T("Невозможно включить clamshell-режим", "Cannot enable clamshell mode", "无法开启合盖模式"),
                L.T("Подключите внешний монитор", "Connect an external monitor", "请连接外接显示器"));
            return;
        }
        HardwareSettings.ClamshellMode = enabled;
        try
        {
            if (enabled) ClamshellControl.Enable();
            else ClamshellControl.Disable();
        }
        catch (Exception exception)
        {
            AppLog.Error("Clamshell toggle failed", exception);
        }
    }

    private void StopHonorServices()
    {
        HonorServices.StopAll();
    }

    private void ToggleStartup()
    {
        try
        {
            StartupManager.SetEnabled(!StartupManager.IsEnabled);
        }
        catch (Exception exception)
        {
            MessageBox.Show(L.T(
                    $"Не удалось изменить автозапуск: {exception.Message}",
                    $"Failed to change startup setting: {exception.Message}",
                    $"无法修改开机自启动设置：{exception.Message}"),
                "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void ShowError(string message)
    {
        if (_disposed)
            return;
        AppLog.Error(message);
        try
        {
            _uiDispatcher.BeginInvoke(() => _osdToast?.Show(
                "performance", "⚠️",
                L.T("Сбой включения производительного режима", "Failed to enable performance mode", "无法开启高性能模式"),
                message));
        }
        catch { }
    }
    private async void ApplyPowerModeChange(bool enabled)
    {
        if (_disposed)
            return;
        if (enabled && !PerformanceModePolicy.CanEnable(out var reason))
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastModeErrorTime) >= 3000)
            {
                Interlocked.Exchange(ref _lastModeErrorTime, now);
                ShowError(reason);
            }
            _ = DisablePerformanceModeAsync();
            return;
        }
        PowerPlanHelper.SyncWithMode(enabled);
        _osdToast?.Show(
            enabled ? "performance" : "intelligence",
            enabled ? "⚡" : "🛡️",
            enabled
                ? L.T("Производительный режим", "Performance mode", "性能模式")
                : L.T("Умный режим", "Smart mode", "智能模式"));
        UpdateTrayIcon();
    }
    private void HandlePowerModeChanged(bool enabled)
    {
        if (_disposed || _uiDispatcher.IsDisposed || _tooltipHandle == IntPtr.Zero)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() => ApplyPowerModeChange(enabled));
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void HandleCameraChanged(bool enabled)
    {
        if (_disposed || _uiDispatcher.IsDisposed || _osdToast is null)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                try
                {
                    CameraControl.SetCamera(enabled);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Camera control failed", ex);
                }
                _osdToast.Show(enabled ? "OnCamera" : "OffCamera", "📷", enabled
                    ? L.T("Камера включена", "Camera On", "摄像头已开启")
                    : L.T("Камера выключена", "Camera Off", "摄像头已关闭"));
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void HandleMicChanged(bool enabled)
    {
        if (_disposed || _uiDispatcher.IsDisposed || _osdToast is null)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                try
                {
                    MicControl.SetMicMute(!enabled);
                    MicControl.SetLight(!enabled);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Mic mute failed", ex);
                }
                _osdToast.Show(enabled ? "OnMic" : "EnMic", enabled ? "🎙️" : "🔇", enabled
                    ? L.T("Микрофон включен", "Microphone On", "麦克风已开启")
                    : L.T("Микрофон выключен", "Microphone Off", "麦克风已关闭"));
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void HandleScreenshot()
    {
        try
        {
            InputSimulator.PressPrintScreen();
        }
        catch (Exception exception)
        {
            AppLog.Error("Screenshot key emulation failed", exception);
        }
    }
    private void HandleTouchpadChanged(bool enabled)
    {
        if (_disposed || _uiDispatcher.IsDisposed || _osdToast is null)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() =>
                _osdToast.Show(enabled ? "OnTouch" : "EnTouch", "🖱️", enabled
                    ? L.T("Тачпад включен", "Touchpad on", "触控板已开启")
                    : L.T("Тачпад выключен", "Touchpad off", "触控板已关闭")));
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void HandleHonorApp()
    {
        if (_disposed || _uiDispatcher.IsDisposed || _osdToast is null)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() =>
                _osdToast.Show("imageIcon", "🔧", L.T("Honor PC Helper", "Honor PC Helper", "荣耀电脑管家")));
        }
        catch (InvalidOperationException)
        {
        }
    }
    private bool ShouldIgnoreBacklightEvent()
        => Environment.TickCount64 < Interlocked.Read(ref _suppressBacklightEventsUntil);
    private void OnHidDeviceChange(string devicePath)
    {
        if (_disposed || !TouchpadBrightnessService.IsSupportedDevicePath(devicePath))
            return;
        _deviceChangeTimer.Stop();
        _deviceChangeTimer.Start();
    }
    private void OnDeviceChangeTimerTick(object? sender, EventArgs eventArgs)
    {
        _deviceChangeTimer.Stop();
        if (_disposed)
            return;
        // Путь устройства, найденный до переподключения, больше не действителен.
        TouchpadVendorLink.InvalidateCache();
        _touchpadService.Restart();
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
    }
    private void OnDisplayResume()
    {
        if (_disposed)
            return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastResumeRestore) < ResumeDebounceMilliseconds)
            return;
        Interlocked.Exchange(ref _lastResumeRestore, now);
        Interlocked.Exchange(ref _suppressBacklightEventsUntil, now + ResumeSettleMilliseconds);
        AppLog.Info("Display turned on, restoring keyboard backlight");
        _ = _backlightSchedule.RestoreAfterResumeAsync();
        _ = _batteryProtection.RestoreAfterResumeAsync();
        TouchpadHapticsController.Reapply();
        TouchpadGesturesController.Reapply();
    }
    private void HandleKeyboardBacklightChanged(KeyboardBacklightLevel level)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                HardwareSettings.KeyboardBacklight = level;
                _backlightSchedule.SetManualOverride();
                _osdToast?.Show(
                    level switch
                    {
                        KeyboardBacklightLevel.Off => "OffLevel",
                        KeyboardBacklightLevel.Low => "LowLevel",
                        _ => "HighLevel"
                    },
                    "⌨️",
                    level switch
                    {
                        KeyboardBacklightLevel.Off => L.T("Подсветка выключена", "Backlight off", "背光已关闭"),
                        KeyboardBacklightLevel.Low => L.T("Подсветка: низкий уровень", "Backlight low", "低亮度背光"),
                        _ => L.T("Подсветка: высокий уровень", "Backlight high", "高亮度背光")
                    });
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void OnTrayIconMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (_disposed)
            return;
        try
        {
            _lastTrayMousePosition = Control.MousePosition;
            if (!_trayHoverTimer.Enabled)
                _trayHoverTimer.Start();
            UpdateTrayTooltip();
            _ = RefreshSensorsAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("Tray tooltip update failed", exception);
        }
    }
    private void OnTrayHoverTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_disposed || Control.MousePosition != _lastTrayMousePosition)
        {
            _trayHoverTimer.Stop();
            return;
        }
        UpdateTrayTooltip();
        _ = RefreshSensorsAsync();
    }
    private void UpdateTrayTooltip(bool force = false)
    {
        if (_disposed)
            return;
        var now = Environment.TickCount64;
        if (!force && now - _lastTooltipUpdate < TooltipMinIntervalMilliseconds)
            return;
        _lastTooltipUpdate = now;
        var text = DiagnosticsService.BuildCompactToolTip();
        if (text == _tooltipCache)
            return;
        _tooltipCache = text;
        _notifyIcon.Text = text;
    }
    private async Task RefreshSensorsAsync()
    {
        if (_disposed || Environment.TickCount64 - Interlocked.Read(ref _lastSensorRefresh) < SensorRefreshIntervalMilliseconds
            || Interlocked.Exchange(ref _sensorRefreshInProgress, 1) != 0)
            return;
        Interlocked.Exchange(ref _lastSensorRefresh, Environment.TickCount64);
        try
        {
            if (!await PrivilegedHardware.TryReadSensorsTaskAsync() || _disposed || _uiDispatcher.IsDisposed)
                return;
            _uiDispatcher.BeginInvoke(() => UpdateTrayTooltip(force: true));
        }
        catch (Exception exception)
        {
            AppLog.Error("Hardware sensor refresh failed", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _sensorRefreshInProgress, 0);
        }
    }
    private void OnTrayIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (_disposed)
            return;
        if (eventArgs.Button == MouseButtons.Right)
        {
            using var menu = CreateMenu();
            var commandId = menu.Show(_window.Handle);
            HideNativeTooltip();
            var action = menu.GetCallback(commandId);
            if (action is not null)
                _uiDispatcher.BeginInvoke(action);
        }
    }
    private void OnMenuTooltip(string? text)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;
        if (text == null)
        {
            HideNativeTooltip();
            return;
        }
        var mousePos = Control.MousePosition;
        var textPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(text);
        var previousText = _tooltipText;
        _tooltipText = textPtr;
        var ti = new NativeMethods.ToolInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ToolInfo>(),
            Flags = NativeMethods.TtfIdIsHwnd | NativeMethods.TtfTrack | NativeMethods.TtfAbsolute | NativeMethods.TtfTransparent,
            Window = _window.Handle,
            Id = _window.Handle,
            Text = textPtr
        };
        if (_tooltipAdded)
            NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmUpdateTipTextW, 0, ref ti);
        else
        {
            NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmAddToolW, 0, ref ti);
            _tooltipAdded = true;
        }
        var pos = ((mousePos.Y + 20) << 16) | ((mousePos.X + 16) & 0xFFFF);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackPosition, 0, pos);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackActivate, 1, ref ti);
        if (previousText != IntPtr.Zero)
            System.Runtime.InteropServices.Marshal.FreeHGlobal(previousText);
    }
    private void HideNativeTooltip()
    {
        if (_tooltipHandle == IntPtr.Zero || !_tooltipAdded)
            return;
        var ti = new NativeMethods.ToolInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ToolInfo>(),
            Flags = NativeMethods.TtfIdIsHwnd | NativeMethods.TtfTrack | NativeMethods.TtfAbsolute | NativeMethods.TtfTransparent,
            Window = _window.Handle,
            Id = _window.Handle,
            Text = _tooltipText
        };
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmTrackActivate, 0, ref ti);
        NativeMethods.SendMessage(_tooltipHandle, NativeMethods.TtmDelToolW, 0, ref ti);
        _tooltipAdded = false;
        if (_tooltipText != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(_tooltipText);
            _tooltipText = IntPtr.Zero;
        }
    }
    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs eventArgs)
    {
        if (_disposed || _uiDispatcher.IsDisposed)
            return;
        try
        {
            _uiDispatcher.BeginInvoke(UpdateTheme);
        }
        catch (InvalidOperationException)
        {
        }
    }
    private void UpdateTheme() => UpdateTrayIcon();
    private void UpdateTrayIcon()
    {
        var icon = TrayIconFactory.Create(HardwareSettings.PerformanceModeActive);
        if (ReferenceEquals(icon, _trayIcon))
            return;
        _trayIcon = icon;
        _notifyIcon.Icon = icon;
    }
    private async void OnSystemPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            await DisablePerformanceModeAsync();
            return;
        }
        if (eventArgs.Mode is Microsoft.Win32.PowerModes.Resume or Microsoft.Win32.PowerModes.StatusChange)
        {
            ScreenControl.Restore();
        }
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.StatusChange)
        {
            if (HardwareSettings.PerformanceModeActive && !PerformanceModePolicy.CanEnable(out _))
            {
                await DisablePerformanceModeAsync();
                return;
            }
        }
        if (eventArgs.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            HardwareSettings.PowerUnlock = false;
            HardwareSettings.PerformanceModeActive = false;
            HandlePowerModeChanged(false);
            Interlocked.Exchange(ref _suppressBacklightEventsUntil, Environment.TickCount64 + ResumeSettleMilliseconds);
            _ = _backlightSchedule.RestoreAfterResumeAsync();
            _ = _batteryProtection.RestoreAfterResumeAsync();
        }
    }
    private async Task DisablePerformanceModeAsync()
    {
        if (!HardwareSettings.PerformanceModeActive)
            return;
        if (!await PrivilegedHardware.TryRunPowerUnlockTaskAsync(false))
            return;
        HardwareSettings.PowerUnlock = false;
        HardwareSettings.PerformanceModeActive = false;
        PowerPlanHelper.SyncWithMode(false);
        UpdateTrayIcon();
    }
    private sealed class MessageWindow : NativeWindow
    {
        private const int WmMenuSelect = 0x011F;
        private readonly Action<string?> _onTooltip;
        private readonly Action _onDisplayResume;
        private readonly Action<string> _onDeviceChange;
        internal MessageWindow(Action<string?> onTooltip,
            Action onDisplayResume, Action<string> onDeviceChange)
        {
            _onTooltip = onTooltip;
            _onDisplayResume = onDisplayResume;
            _onDeviceChange = onDeviceChange;
            CreateHandle(new CreateParams());
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmPowerBroadcast
                && message.WParam.ToInt32() == NativeMethods.PbtPowerSettingChange
                && message.LParam != IntPtr.Zero)
            {
                var setting = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<NativeMethods.PowerBroadcastSetting>(message.LParam);
                if (setting.PowerSetting == NativeMethods.GuidConsoleDisplayState && setting.Data == 1)
                    _onDisplayResume();
                base.WndProc(ref message);
                return;
            }
            if (message.Msg == NativeMethods.WmDeviceChange && message.LParam != IntPtr.Zero)
            {
                var eventType = (int)message.WParam.ToInt64();
                if (eventType is NativeMethods.DbtDeviceArrival or NativeMethods.DbtDeviceRemoveComplete
                    && System.Runtime.InteropServices.Marshal.ReadInt32(message.LParam, 4)
                        == NativeMethods.DbtDevTypDeviceInterface)
                {
                    var devicePath = System.Runtime.InteropServices.Marshal.PtrToStringUni(
                        message.LParam + NativeMethods.DevBroadcastNameOffset);
                    if (!string.IsNullOrEmpty(devicePath))
                        _onDeviceChange(devicePath);
                }
                base.WndProc(ref message);
                return;
            }
            if (message.Msg == WmMenuSelect)
            {
                var commandId = (int)(message.WParam.ToInt64() & 0xFFFF);
                var flags = (uint)((message.WParam.ToInt64() >> 16) & 0xFFFF);
                var menuHandle = message.LParam;
                if (menuHandle == IntPtr.Zero || commandId == 0xFFFF)
                    _onTooltip(null);
                else if ((flags & NativeMethods.MfPopup) != 0)
                    _onTooltip(NativePopupMenu.GetSubMenuTooltip(menuHandle, commandId));
                else
                    _onTooltip(NativePopupMenu.GetTooltip(commandId));
            }
            base.WndProc(ref message);
        }
    }
}
