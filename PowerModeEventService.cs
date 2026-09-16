using System.Management;
namespace HonorPCHelper;
internal sealed class PowerModeEventService : IDisposable
{
    private const uint MicKeyCode = 0x287;
    private const uint CameraKeyCode = 0x288;
    private const uint ScreenshotKeyCode = 0x28E;
    private const uint TouchpadOffCode = 0x2A3;
    private const uint TouchpadOnCode = 0x283;
    private const uint HonorAppKeyCode = 0x28A;
    private readonly Action<bool> _onModeChanged;
    private readonly Action<KeyboardBacklightLevel> _onBacklightChanged;
    private readonly Action<bool>? _onCameraChanged;
    private readonly Action<bool>? _onMicChanged;
    private readonly Action? _onScreenshot;
    private readonly Action<bool>? _onTouchpadChanged;
    private readonly Action? _onHonorApp;
    private readonly Func<bool>? _shouldIgnoreBacklightEvent;
    private ManagementEventWatcher? _watcher;
    private long _lastEventTime;
    private volatile bool _currentState = HardwareSettings.PerformanceModeActive;
    internal PowerModeEventService(
        Action<bool> onModeChanged,
        Action<KeyboardBacklightLevel> onBacklightChanged,
        Func<bool>? shouldIgnoreBacklightEvent = null,
        Action<bool>? onCameraChanged = null,
        Action<bool>? onMicChanged = null,
        Action? onScreenshot = null,
        Action<bool>? onTouchpadChanged = null,
        Action? onHonorApp = null)
    {
        _onModeChanged = onModeChanged;
        _onBacklightChanged = onBacklightChanged;
        _shouldIgnoreBacklightEvent = shouldIgnoreBacklightEvent;
        _onCameraChanged = onCameraChanged;
        _onMicChanged = onMicChanged;
        _onScreenshot = onScreenshot;
        _onTouchpadChanged = onTouchpadChanged;
        _onHonorApp = onHonorApp;
    }
    internal void Start()
    {
        var scope = new ManagementScope(@"\\.\root\wmi");
        scope.Connect();
        _watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM OemWMIEvent"));
        _watcher.EventArrived += OnEventArrived;
        _watcher.Start();
    }
    public void Dispose()
    {
        if (_watcher is null)
            return;
        _watcher.EventArrived -= OnEventArrived;
        try
        {
            _watcher.Stop();
        }
        catch (ManagementException)
        {
        }
        _watcher.Dispose();
        _watcher = null;
    }
    private void OnEventArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        try
        {
            ProcessEvent(eventArgs);
        }
        catch (Exception exception)
        {
            AppLog.Error("HONOR WMI event processing failed", exception);
        }
    }
    private void ProcessEvent(EventArrivedEventArgs eventArgs)
    {
        var value = eventArgs.NewEvent.Properties["Force"]?.Value;
        if (value is null)
            return;
        var code = Convert.ToUInt32(value) & 0xFFFF;
        AppLog.Info($"HONOR WMI event: 0x{code:X4}");
        switch (code)
        {
            case CameraKeyCode:
                _cameraEnabled = !_cameraEnabled;
                _onCameraChanged?.Invoke(_cameraEnabled);
                return;
            case MicKeyCode:
                _micEnabled = !_micEnabled;
                _onMicChanged?.Invoke(_micEnabled);
                return;
            case ScreenshotKeyCode:
                _onScreenshot?.Invoke();
                return;
            case TouchpadOffCode:
                _onTouchpadChanged?.Invoke(false);
                return;
            case TouchpadOnCode:
                _onTouchpadChanged?.Invoke(true);
                return;
            case HonorAppKeyCode:
                _onHonorApp?.Invoke();
                return;
        }
        if (code is 0x2B1 or 0x2B2 or 0x2B3)
        {
            if (_shouldIgnoreBacklightEvent?.Invoke() == true)
                return;
            var level = code switch
            {
                0x2B1 => KeyboardBacklightLevel.Off,
                0x2B2 => KeyboardBacklightLevel.Low,
                _ => KeyboardBacklightLevel.High
            };
            HardwareSettings.KeyboardBacklight = level;
            _onBacklightChanged(level);
            return;
        }
        if (code is not (0x2A0 or 0x2A1 or 0x2A6))
            return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastEventTime) < 500)
            return;
        Interlocked.Exchange(ref _lastEventTime, now);
        _currentState = code switch
        {
            0x2A0 => false,
            0x2A1 => true,
            _ => true
        };
        HardwareSettings.PerformanceModeActive = _currentState;
        _onModeChanged(_currentState);
    }
    private volatile bool _cameraEnabled = CameraControl.IsCameraEnabled();
    private volatile bool _micEnabled = !MicControl.IsMicMuted();
}
