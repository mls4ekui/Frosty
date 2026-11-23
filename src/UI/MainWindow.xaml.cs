using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Frosty;

public partial class MainWindow : Window
{
    private const int HotkeyIdTogglePause = 9001;
    private const int HotkeyIdClearSnow = 9002;

    private IntPtr _hwnd;
    private uint _ownProcessId;
    private NativeOverlayWindow? _overlay;
    private DragCompanionWindow? _dragCompanion;
    private WindowTrackerService? _windowTracker;
    private SnowSimulationEngine? _engine;
    private TrayService? _trayService;
    private SettingsWindow? _settingsWindow;
    private WelcomeWindow? _welcomeWindow;
    private AppSettings _settings = new();

    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _hitTestTimer;
    private readonly DispatcherTimer _fullscreenCheckTimer = new();
    private readonly StringBuilder _fgClassBuffer = new(128);

    private long _lastTicks;
    private float _accumulatedDt;
    private bool _isClickThrough = true;
    private bool _isManuallyPaused;
    private bool _isFullscreenGameActive;
    private bool _isSessionLocked;
    private bool _isWizardActive;
    private bool _isShuttingDown;
    private bool _isRenderingSubscribed;
    private bool _hotkeysRegistered;

    private int _physX;
    private int _physY;
    private int _physW;
    private int _physH;

    public MainWindow()
    {
        InitializeComponent();

        _ownProcessId = (uint)Environment.ProcessId;

        _hitTestTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr selfHwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(selfHwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(selfHwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReadVirtualScreenMetrics();

        _overlay = new NativeOverlayWindow(_physX, _physY, _physW, _physH);
        _overlay.FrameRequested += OnOverlayFrame;
        _overlay.LeftButtonDown += OnOverlayLeftButtonDown;
        _overlay.LeftButtonUp += OnOverlayLeftButtonUp;
        _overlay.RightButtonDown += OnOverlayRightButtonDown;
        _overlay.MouseCaptureLost += OnOverlayMouseCaptureLost;
        _overlay.HotkeyPressed += OnOverlayHotkey;
        _hwnd = _overlay.Handle;

        _dragCompanion = new DragCompanionWindow(_physX, _physY);

        _settings = AppSettings.Load();
        Loc.SetLanguage(_settings.IsEnglishLanguage);
        Motion.Enabled = _settings.EnableUiAnimations;
        _windowTracker = new WindowTrackerService(_hwnd, _physX, _physY, _physW, _physH);
        _engine = new SnowSimulationEngine(_physX, _physY, _physW, _physH, _windowTracker);
        _settings.ApplyToEngine(_engine);
        _overlay.SetTargetFps(_settings.TargetFps);
        ApplyVirtualScreenGeometry();
        _windowTracker.UpdateScreenBounds(_physX, _physY, _physW, _physH);
        _engine.UpdateScreenBounds(_physX, _physY, _physW, _physH);

        _windowTracker.ActiveWindowMoved += OnActiveWindowMovedByOs;
        _windowTracker.DraggedWindowPositionChanged += OnDraggedWindowPositionChanged;

        UpdateGlobalHotkeysRegistration();

        _trayService = new TrayService(
            _engine,
            onOpenSettings: ShowSettingsWindow,
            onManualPauseToggle: ToggleManualPause,
            onExit: () =>
            {
                _isShuttingDown = true;
                _welcomeWindow?.Close();
                _settingsWindow?.ForceClose();
                Close();
            },
            onSelectPreset: ApplyPresetFromTray);
        _trayService.UpdateActivePreset(SnowPresets.Detect(_settings));

        _hitTestTimer.Tick += OnHitTestTick;
        _hitTestTimer.Start();

        _fullscreenCheckTimer.Interval = TimeSpan.FromSeconds(1);
        _fullscreenCheckTimer.Tick += OnFullscreenCheckTick;
        _fullscreenCheckTimer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        _stopwatch.Start();
        _lastTicks = _stopwatch.ElapsedTicks;

        _isWizardActive = !_settings.IsFirstRunCompleted;
        UpdateSimulationRunningState();

        if (_isWizardActive)
        {
            ShowWelcomeWindow();
        }

        if (_settings.EnableAutoUpdate)
        {
            _ = CheckForUpdatesInBackgroundAsync();
        }
    }

    private void OnDraggedWindowPositionChanged(IntPtr hwnd, int localLeft, int localTop, int localRight, bool ended)
    {
        if (ended || _engine == null || _isManuallyPaused || _isFullscreenGameActive || _isSessionLocked || _isWizardActive)
        {
            _dragCompanion?.EndDrag();
            return;
        }

        _dragCompanion?.MoveOrBeginDrag(hwnd, localLeft, localTop, localRight, _engine);
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        await Task.Delay(3500);
        if (_isShuttingDown) return;

        UpdateInfo? info = await AutoUpdater.CheckForUpdatesAsync();
        if (info == null || _isShuttingDown) return;

        var res = System.Windows.MessageBox.Show(
            Loc.Tr(
                $"Доступно обновление Frosty ({info.TagVersion})!\nСкачать и установить его сейчас?",
                $"A new Frosty update ({info.TagVersion}) is available!\nDownload and install it now?"),
            "Frosty Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (res == MessageBoxResult.Yes)
        {
            await AutoUpdater.DownloadAndApplyUpdateAsync(info);
        }
    }

    public void ShowWelcomeWindow()
    {
        if (_engine == null) return;

        if (_welcomeWindow == null || !_welcomeWindow.IsLoaded)
        {
            _isWizardActive = true;
            UpdateSimulationRunningState();

            _welcomeWindow = new WelcomeWindow(_settings, OnWelcomeFinished);
        }
        _welcomeWindow.Show();
        if (_welcomeWindow.WindowState == WindowState.Minimized)
        {
            _welcomeWindow.WindowState = WindowState.Normal;
        }
        _welcomeWindow.Activate();
        EnsureSettingsWindowAboveOverlay();
    }

    private void OnWelcomeFinished(bool applied)
    {
        _isWizardActive = false;
        if (_isShuttingDown || _engine == null) return;

        if (applied)
        {
            Loc.SetLanguage(_settings.IsEnglishLanguage);
            _settings.ApplyToEngine(_engine);
            _overlay?.SetTargetFps(_settings.TargetFps);
            ApplyVirtualScreenGeometry();
            _windowTracker?.UpdateScreenBounds(_physX, _physY, _physW, _physH);
            _engine.UpdateScreenBounds(_physX, _physY, _physW, _physH);
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.RefreshFromSettings();
        }

        OnSettingsChangedFromUi();
    }

    private static bool IsUiWindowOpen(Window? w) =>
        w is { IsLoaded: true, IsVisible: true } && w.WindowState != WindowState.Minimized;

    private void OnActiveWindowMovedByOs()
    {
        if (!_isManuallyPaused && !_isFullscreenGameActive && !_isSessionLocked && !_isWizardActive)
        {
            _engine?.WakeFromIdle();
            _overlay?.SetLowPowerMode(false);
        }
    }

    private void ReadVirtualScreenMetrics()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_settings.MonitorMode == MonitorMode.PrimaryOnly)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null)
            {
                _physX = primary.Bounds.X;
                _physY = primary.Bounds.Y;
                _physW = Math.Max(1, primary.Bounds.Width);
                _physH = Math.Max(1, primary.Bounds.Height);
                return;
            }
        }
        else if (_settings.MonitorMode == MonitorMode.SecondaryOnly && screens.Length > 1)
        {
            var secondary = screens.FirstOrDefault(s => !s.Primary) ?? screens[0];
            _physX = secondary.Bounds.X;
            _physY = secondary.Bounds.Y;
            _physW = Math.Max(1, secondary.Bounds.Width);
            _physH = Math.Max(1, secondary.Bounds.Height);
            return;
        }

        _physX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        _physY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        _physW = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN));
        _physH = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
    }

    private void ApplyVirtualScreenGeometry()
    {
        ReadVirtualScreenMetrics();
        _overlay?.Resize(_physX, _physY, _physW, _physH);
        _dragCompanion?.UpdateOrigin(_physX, _physY);
        PositionOverlayWindow();
    }

    private void PositionOverlayWindow()
    {
        if (_hwnd == IntPtr.Zero) return;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            _physX,
            _physY,
            _physW,
            _physH,
            NativeMethods.SWP_NOACTIVATE);

        EnsureSettingsWindowAboveOverlay();
    }

    private void EnsureSettingsWindowAboveOverlay()
    {
        if (_settingsWindow is { IsLoaded: true, IsVisible: true })
        {
            IntPtr settingsHwnd = new WindowInteropHelper(_settingsWindow).Handle;
            if (settingsHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    settingsHwnd,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }

        if (_welcomeWindow is { IsLoaded: true, IsVisible: true })
        {
            IntPtr welcomeHwnd = new WindowInteropHelper(_welcomeWindow).Handle;
            if (welcomeHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    welcomeHwnd,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }

    private void SyncExclusionZonesToEngine()
    {
        if (_engine == null) return;

        NativeMethods.RECT? settingsRect = null;
        if (_settingsWindow is { IsLoaded: true, IsVisible: true })
        {
            IntPtr settingsHwnd = new WindowInteropHelper(_settingsWindow).Handle;
            if (settingsHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(settingsHwnd, out NativeMethods.RECT sr))
            {
                settingsRect = sr;
            }
        }

        NativeMethods.RECT? trayRect = null;
        if (_trayService != null && _trayService.TryGetVisibleMenuBounds(out NativeMethods.RECT tr))
        {
            trayRect = tr;
        }

        NativeMethods.RECT? welcomeRect = null;
        if (_welcomeWindow is { IsLoaded: true, IsVisible: true })
        {
            IntPtr welcomeHwnd = new WindowInteropHelper(_welcomeWindow).Handle;
            if (welcomeHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(welcomeHwnd, out NativeMethods.RECT wr))
            {
                welcomeRect = wr;
            }
        }

        _engine.SetUiExclusionRects(settingsRect, trayRect, welcomeRect);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyVirtualScreenGeometry();
            _windowTracker?.UpdateScreenBounds(_physX, _physY, _physW, _physH);
            _engine?.UpdateScreenBounds(_physX, _physY, _physW, _physH);
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            {
                _isSessionLocked = true;
                UpdateSimulationRunningState();
            }
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            {
                _isSessionLocked = false;
                UpdateSimulationRunningState();
            }
        });
    }

    private void ShowSettingsWindow()
    {
        if (_engine == null) return;

        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings, _engine, OnSettingsChangedFromUi, ShowWelcomeWindow);
        }

        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
        EnsureSettingsWindowAboveOverlay();
    }

    private void OnSettingsChangedFromUi()
    {
        UpdateGlobalHotkeysRegistration();
        _overlay?.SetTargetFps(_settings.TargetFps);
        _trayService?.UpdateActivePreset(SnowPresets.Detect(_settings));

        int prevX = _physX, prevY = _physY, prevW = _physW, prevH = _physH;
        ReadVirtualScreenMetrics();
        if (prevX != _physX || prevY != _physY || prevW != _physW || prevH != _physH)
        {
            _overlay?.Resize(_physX, _physY, _physW, _physH);
            _dragCompanion?.UpdateOrigin(_physX, _physY);
            PositionOverlayWindow();
            _windowTracker?.UpdateScreenBounds(_physX, _physY, _physW, _physH);
            _engine?.UpdateScreenBounds(_physX, _physY, _physW, _physH);
        }

        if (!_settings.PauseInFullscreenGames && _isFullscreenGameActive)
        {
            _isFullscreenGameActive = false;
        }
        UpdateSimulationRunningState();
        EnsureSettingsWindowAboveOverlay();
    }

    private void ApplyPresetFromTray(SnowPreset preset)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_engine == null) return;
            SnowPresets.Apply(_settings, preset);
            _settings.ApplyToEngine(_engine);
            _settings.Save();
            _trayService?.UpdateActivePreset(preset);
            _settingsWindow?.RefreshFromSettings();
        });
    }

    private void ToggleManualPause()
    {
        _isManuallyPaused = !_isManuallyPaused;
        UpdateSimulationRunningState();
    }

    private void UpdateGlobalHotkeysRegistration()
    {
        if (_hwnd == IntPtr.Zero) return;

        if (_settings.EnableGlobalHotkeys && !_hotkeysRegistered)
        {
            uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
            bool sOk = NativeMethods.RegisterHotKey(_hwnd, HotkeyIdTogglePause, mods, NativeMethods.VK_S);
            bool cOk = NativeMethods.RegisterHotKey(_hwnd, HotkeyIdClearSnow, mods, NativeMethods.VK_C);
            _hotkeysRegistered = sOk || cOk;
        }
        else if (!_settings.EnableGlobalHotkeys && _hotkeysRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdTogglePause);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdClearSnow);
            _hotkeysRegistered = false;
        }
    }

    private void OnOverlayHotkey(int id)
    {
        if (id == HotkeyIdTogglePause)
        {
            ToggleManualPause();
        }
        else if (id == HotkeyIdClearSnow)
        {
            _engine?.ClearAllSnow();
        }
    }

    private void OnFullscreenCheckTick(object? sender, EventArgs e)
    {
        if (_isSessionLocked) return;

        if (!_settings.PauseInFullscreenGames)
        {
            if (_isFullscreenGameActive)
            {
                _isFullscreenGameActive = false;
                UpdateSimulationRunningState();
            }
            return;
        }

        bool isGameOrFullscreen = DetectFullscreenOrBorderlessApp();

        if (_isFullscreenGameActive != isGameOrFullscreen)
        {
            _isFullscreenGameActive = isGameOrFullscreen;
            UpdateSimulationRunningState();
        }

        if (!_isManuallyPaused && !_isFullscreenGameActive && !_isWizardActive && _hwnd != IntPtr.Zero && _overlay != null && _overlay.IsShown)
        {
            PositionOverlayWindow();
        }
    }

    private bool DetectFullscreenOrBorderlessApp()
    {
        IntPtr earlyFg = NativeMethods.GetForegroundWindow();
        if (earlyFg != IntPtr.Zero && earlyFg != _hwnd)
        {
            NativeMethods.GetWindowThreadProcessId(earlyFg, out uint earlyPid);
            if (earlyPid != _ownProcessId)
            {
                string? earlyName = TryGetProcessName((int)earlyPid);
                if (!string.IsNullOrEmpty(earlyName))
                {
                    if (MatchesProcessList(earlyName, _settings.ForcePauseProcesses))
                        return true;
                    if (MatchesProcessList(earlyName, _settings.FullscreenWhitelist))
                        return false;
                }
            }
        }

        if (NativeMethods.SHQueryUserNotificationState(out var state) == 0)
        {
            if (state is NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                      or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
            {
                return true;
            }
        }

        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero || fgHwnd == _hwnd)
            return false;

        if (NativeMethods.IsZoomed(fgHwnd))
            return false;

        NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
        if (fgPid == _ownProcessId)
            return false;

        string? fgProcName = TryGetProcessName((int)fgPid);
        if (!string.IsNullOrEmpty(fgProcName))
        {
            if (MatchesProcessList(fgProcName, _settings.ForcePauseProcesses))
                return true;
            if (MatchesProcessList(fgProcName, _settings.FullscreenWhitelist))
                return false;
        }

        if (!NativeMethods.IsWindowVisible(fgHwnd) || NativeMethods.IsIconic(fgHwnd))
            return false;

        _fgClassBuffer.Clear();
        if (NativeMethods.GetClassName(fgHwnd, _fgClassBuffer, _fgClassBuffer.Capacity) > 0)
        {
            string cls = _fgClassBuffer.ToString();
            if (string.Equals(cls, "Progman", StringComparison.Ordinal) ||
                string.Equals(cls, "WorkerW", StringComparison.Ordinal) ||
                string.Equals(cls, "Shell_TrayWnd", StringComparison.Ordinal) ||
                string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!NativeMethods.GetWindowRect(fgHwnd, out NativeMethods.RECT winRect))
            return false;

        IntPtr hMonitor = NativeMethods.MonitorFromWindow(fgHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return false;

        var monInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monInfo))
            return false;

        NativeMethods.RECT monRect = monInfo.rcMonitor;

        bool coversMonitor = winRect.Left <= monRect.Left &&
                             winRect.Top <= monRect.Top &&
                             winRect.Right >= monRect.Right &&
                             winRect.Bottom >= monRect.Bottom;

        return coversMonitor;
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesProcessList(string procName, string csvList)
    {
        if (string.IsNullOrWhiteSpace(csvList)) return false;
        foreach (string raw in csvList.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string clean = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            if (string.Equals(procName, clean, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void UpdateSimulationRunningState()
    {
        _trayService?.UpdateState(_isManuallyPaused, _isFullscreenGameActive);

        bool shouldPause = _isManuallyPaused || _isFullscreenGameActive || _isSessionLocked || _isWizardActive;

        _windowTracker?.SetPaused(shouldPause);

        if (_overlay == null) return;

        if (shouldPause)
        {
            _dragCompanion?.EndDrag();

            if (_isRenderingSubscribed)
            {
                _overlay.StopFrameLoop();
                _isRenderingSubscribed = false;
            }

            if (_overlay.IsMouseCaptured)
            {
                _overlay.ReleaseMouse();
            }

            SetClickThrough(true);
            _overlay.Hide();
        }
        else
        {
            _overlay.Show();

            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    _hwnd,
                    NativeMethods.HWND_TOPMOST,
                    _physX,
                    _physY,
                    _physW,
                    _physH,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

                EnsureSettingsWindowAboveOverlay();
            }

            _engine?.NotifyResume();
            _overlay.SetLowPowerMode(false);
            _lastTicks = _stopwatch.ElapsedTicks;
            _accumulatedDt = 0f;

            if (!_isRenderingSubscribed)
            {
                _overlay.StartFrameLoop();
                _isRenderingSubscribed = true;
            }
        }
    }

    private void OnHitTestTick(object? sender, EventArgs e)
    {
        if (_engine == null || _hwnd == IntPtr.Zero || _isManuallyPaused || _isFullscreenGameActive || _isSessionLocked || _isWizardActive)
            return;

        if (IsUiWindowOpen(_settingsWindow) ||
            IsUiWindowOpen(_welcomeWindow) ||
            (_trayService != null && _trayService.TryGetVisibleMenuBounds(out _)))
        {
            SetClickThrough(true);
            return;
        }

        NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
        float localX = pt.X - _physX;
        float localY = pt.Y - _physY;

        bool shouldIntercept = _engine.ShouldInterceptMouse(localX, localY);
        SetClickThrough(!shouldIntercept);
    }

    private void SetClickThrough(bool transparent)
    {
        if (_isClickThrough == transparent || _hwnd == IntPtr.Zero) return;
        _isClickThrough = transparent;

        int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if (transparent)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
        }
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    private (float x, float y) GetPhysicalMousePos()
    {
        NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
        return (pt.X - _physX, pt.Y - _physY);
    }

    private bool OnOverlayLeftButtonDown()
    {
        if (_engine == null || _isManuallyPaused || _isFullscreenGameActive || _isSessionLocked || _isWizardActive) return false;
        var (x, y) = GetPhysicalMousePos();
        return _engine.OnMouseDown(x, y);
    }

    private bool OnOverlayRightButtonDown()
    {
        if (_engine == null || _isManuallyPaused || _isFullscreenGameActive || _isSessionLocked || _isWizardActive) return false;
        var (x, y) = GetPhysicalMousePos();
        return _engine.OnRightMouseDown(x, y);
    }

    private void OnOverlayLeftButtonUp()
    {
        if (_engine == null) return;
        var (x, y) = GetPhysicalMousePos();
        _engine.OnMouseUp(x, y);
    }

    private void OnOverlayMouseCaptureLost()
    {
        if (_engine != null && _engine.HasActiveInteraction)
        {
            var (x, y) = GetPhysicalMousePos();
            _engine.OnMouseUp(x, y);
        }
    }

    private void OnOverlayFrame()
    {
        if (_overlay == null || !_overlay.IsShown || _engine == null) return;

        long currentTicks = _stopwatch.ElapsedTicks;
        float dt = (float)(currentTicks - _lastTicks) / Stopwatch.Frequency;
        _lastTicks = currentTicks;

        if (dt > 0.066f) dt = 0.066f;
        if (dt < 0.0005f) dt = 0.0005f;

        _accumulatedDt += dt;

        bool isLeftMouseButtonHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        if (isLeftMouseButtonHeld)
        {
            _engine.WakeFromIdle();
        }

        bool sleep = _engine.IsInPowerSaveSleep && !isLeftMouseButtonHeld;
        _overlay.SetLowPowerMode(sleep);

        float stepDt = Math.Clamp(_accumulatedDt, 0.0005f, 0.066f);
        _accumulatedDt = 0f;

        SyncExclusionZonesToEngine();
        _engine.Update(stepDt);

        _overlay.Present(_engine);
        _dragCompanion?.RefreshVisualsIfVisible(_engine);
    }

    public void EmergencyCleanup()
    {
        _trayService?.Dispose();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isShuttingDown = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        if (_windowTracker != null)
        {
            _windowTracker.ActiveWindowMoved -= OnActiveWindowMovedByOs;
            _windowTracker.DraggedWindowPositionChanged -= OnDraggedWindowPositionChanged;
        }

        _hitTestTimer.Stop();
        _fullscreenCheckTimer.Stop();

        if (_hotkeysRegistered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdTogglePause);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdClearSnow);
            _hotkeysRegistered = false;
        }

        _overlay?.StopFrameLoop();
        _isRenderingSubscribed = false;

        _dragCompanion?.Dispose();
        _dragCompanion = null;

        _trayService?.Dispose();
        _windowTracker?.Dispose();
        _engine?.Dispose();

        _overlay?.Dispose();
        _overlay = null;
    }
}