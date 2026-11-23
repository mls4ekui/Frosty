using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Frosty;

public interface IWindowTracker
{
    IntPtr ActivelyDraggedWindow { get; }
    bool IsWindowMinimizingOrHidden(IntPtr hwnd);
    void SyncSurfacesForFrame(List<TrackedSurface> destination);
    void RefreshMovedWindowImmediate(List<TrackedSurface> snapshot);
}

public struct TrackedSurface
{
    public IntPtr Handle;
    public int Left;
    public int Right;
    public int TopY;
    public int BottomY;
    public bool IsTaskbar;
    public bool IsMaximizedOrTopDocked;
}

public sealed class WindowTrackerService : IWindowTracker, IDisposable
{
    private struct WindowMargin
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
        public bool IsTaskbar;
    }

    private readonly IntPtr _overlayHwnd;
    private readonly uint _ownPid;

    private int _originX;
    private int _originY;
    private int _screenWidth;
    private int _screenHeight;

    private readonly List<TrackedSurface> _surfaces = new(64);
    private readonly Dictionary<IntPtr, WindowMargin> _marginCache = new(128);
    private readonly HashSet<IntPtr> _minimizingOrHiddenHwnds = new(64);
    private readonly StringBuilder _classNameBuffer = new(128);

    private readonly NativeMethods.EnumWindowsProc _enumProc;
    private readonly NativeMethods.WinEventDelegate _winEventDelegate;

    private IntPtr _systemEventHook;
    private IntPtr _objectEventHook;
    private IntPtr _movingHwnd;
    private int _movingInitWidth;
    private int _movingInitHeight;
    private bool _isResizingMove;
    private bool _isPaused;
    private bool _needsFullEnum = true;
    private long _lastFullEnumTick;

    public IntPtr ActivelyDraggedWindow => (_movingHwnd != IntPtr.Zero && !_isResizingMove) ? _movingHwnd : IntPtr.Zero;

    public event Action? ActiveWindowMoved;
                                                                                                                                         
    public event Action<IntPtr, int, int, int, bool>? DraggedWindowPositionChanged;

    public WindowTrackerService(IntPtr overlayHwnd, int originX, int originY, int screenWidth, int screenHeight)
    {
        _overlayHwnd = overlayHwnd;
        _ownPid = (uint)Environment.ProcessId;
        _originX = originX;
        _originY = originY;
        _screenWidth = Math.Max(1, screenWidth);
        _screenHeight = Math.Max(1, screenHeight);

        _enumProc = EnumWindowCallback;
        _winEventDelegate = OnWinEvent;

        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;

        _systemEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            flags);

        _objectEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY,
            NativeMethods.EVENT_OBJECT_UNCLOAKED,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            flags);

        FullEnumWindows();
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_isPaused || hwnd == IntPtr.Zero || hwnd == _overlayHwnd)
            return;

        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            _minimizingOrHiddenHwnds.Remove(hwnd);
            _needsFullEnum = true;
            ActiveWindowMoved?.Invoke();
            return;
        }

        if (idObject != NativeMethods.OBJID_WINDOW)
            return;

        switch (eventType)
        {
            case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
            case NativeMethods.EVENT_OBJECT_HIDE:
            case NativeMethods.EVENT_OBJECT_CLOAKED:
            case NativeMethods.EVENT_OBJECT_DESTROY:
                _minimizingOrHiddenHwnds.Add(hwnd);
                if (_movingHwnd == hwnd)
                {
                    _movingHwnd = IntPtr.Zero;
                    DraggedWindowPositionChanged?.Invoke(hwnd, 0, 0, 0, true);
                }
                _needsFullEnum = true;
                ActiveWindowMoved?.Invoke();
                break;

            case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
            case NativeMethods.EVENT_OBJECT_SHOW:
            case NativeMethods.EVENT_OBJECT_UNCLOAKED:
                _minimizingOrHiddenHwnds.Remove(hwnd);
                _needsFullEnum = true;
                ActiveWindowMoved?.Invoke();
                break;

            case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                _movingHwnd = hwnd;
                _isResizingMove = false;
                if (TryGetAccurateWindowBounds(hwnd, false, out int sl, out int st, out int sr, out int sb, out bool topDocked) && !topDocked)
                {
                    _movingInitWidth = sr - sl;
                    _movingInitHeight = sb - st;
                    UpdateSurfaceCoordsInMemory(hwnd, sl, st, sr, sb, topDocked);
                    DraggedWindowPositionChanged?.Invoke(hwnd, sl, st, sr, false);
                }
                else
                {
                    _movingHwnd = IntPtr.Zero;
                }
                ActiveWindowMoved?.Invoke();
                break;

            case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                IntPtr ended = _movingHwnd;
                _movingHwnd = IntPtr.Zero;
                _isResizingMove = false;
                if (ended != IntPtr.Zero)
                {
                    DraggedWindowPositionChanged?.Invoke(ended, 0, 0, 0, true);
                }
                _needsFullEnum = true;
                ActiveWindowMoved?.Invoke();
                break;

            case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                if (_movingHwnd != IntPtr.Zero && hwnd == _movingHwnd)
                {
                    if (TryGetAccurateWindowBounds(hwnd, false, out int l, out int t, out int r, out int b, out bool td))
                    {
                        int cw = r - l;
                        int ch = b - t;
                        if (td || Math.Abs(cw - _movingInitWidth) > 4 || Math.Abs(ch - _movingInitHeight) > 4)
                        {
                            _isResizingMove = true;
                            DraggedWindowPositionChanged?.Invoke(hwnd, 0, 0, 0, true);
                        }
                        else if (!_isResizingMove)
                        {
                            UpdateSurfaceCoordsInMemory(hwnd, l, t, r, b, td);
                            DraggedWindowPositionChanged?.Invoke(hwnd, l, t, r, false);
                        }
                    }
                }
                ActiveWindowMoved?.Invoke();
                break;
        }
    }

    private void UpdateSurfaceCoordsInMemory(IntPtr hwnd, int l, int t, int r, int b, bool topDocked)
    {
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (_surfaces[i].Handle == hwnd)
            {
                TrackedSurface s = _surfaces[i];
                s.Left = l;
                s.TopY = t;
                s.Right = r;
                s.BottomY = b;
                s.IsMaximizedOrTopDocked = topDocked;
                _surfaces[i] = s;
                break;
            }
        }
    }

    public bool IsWindowMinimizingOrHidden(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;
        return _minimizingOrHiddenHwnds.Contains(hwnd) || NativeMethods.IsIconic(hwnd) || !NativeMethods.IsWindowVisible(hwnd);
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        if (paused && _movingHwnd != IntPtr.Zero)
        {
            IntPtr prev = _movingHwnd;
            _movingHwnd = IntPtr.Zero;
            DraggedWindowPositionChanged?.Invoke(prev, 0, 0, 0, true);
        }
        if (!paused) _needsFullEnum = true;
    }

    public void UpdateScreenBounds(int originX, int originY, int width, int height)
    {
        _originX = originX;
        _originY = originY;
        _screenWidth = Math.Max(1, width);
        _screenHeight = Math.Max(1, height);
        _marginCache.Clear();
        _needsFullEnum = true;
    }

    public void SyncSurfacesForFrame(List<TrackedSurface> destination)
    {
        destination.Clear();
        if (_isPaused) return;

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastFullEnumTick) * 1000.0 / Stopwatch.Frequency;

        if (_needsFullEnum || (_movingHwnd == IntPtr.Zero && elapsedMs > 220.0) || _surfaces.Count == 0)
        {
            FullEnumWindows();
            _needsFullEnum = false;
            _lastFullEnumTick = now;
        }
        else
        {
            FastRefreshExistingSurfaces();
        }

        destination.AddRange(_surfaces);
    }

    public void RefreshMovedWindowImmediate(List<TrackedSurface> snapshot)
    {
        if (_isPaused || _movingHwnd == IntPtr.Zero) return;

        IntPtr moving = _movingHwnd;
        for (int i = 0; i < snapshot.Count; i++)
        {
            TrackedSurface s = snapshot[i];
            if (s.Handle != moving) continue;

            if (TryGetAccurateWindowBounds(s.Handle, s.IsTaskbar, out int l, out int t, out int r, out int b, out bool topDocked))
            {
                s.Left = l;
                s.TopY = t;
                s.Right = r;
                s.BottomY = b;
                s.IsMaximizedOrTopDocked = topDocked;
                snapshot[i] = s;
            }
            break;
        }
    }

    private void FastRefreshExistingSurfaces()
    {
        for (int i = _surfaces.Count - 1; i >= 0; i--)
        {
            TrackedSurface s = _surfaces[i];
            if (_minimizingOrHiddenHwnds.Contains(s.Handle) || !NativeMethods.IsWindowVisible(s.Handle) || NativeMethods.IsIconic(s.Handle))
            {
                _surfaces.RemoveAt(i);
                _needsFullEnum = true;
                continue;
            }

            if (TryGetAccurateWindowBounds(s.Handle, s.IsTaskbar, out int l, out int t, out int r, out int b, out bool topDocked))
            {
                s.Left = l;
                s.TopY = t;
                s.Right = r;
                s.BottomY = b;
                s.IsMaximizedOrTopDocked = topDocked;
                _surfaces[i] = s;
            }
            else
            {
                _surfaces.RemoveAt(i);
            }
        }
    }

    private void FullEnumWindows()
{
    _surfaces.Clear();

    if (_marginCache.Count > 250) _marginCache.Clear();
    if (_minimizingOrHiddenHwnds.Count > 128) _minimizingOrHiddenHwnds.Clear();

    NativeMethods.EnumWindows(_enumProc, IntPtr.Zero);
	
    IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
    if (fgHwnd != IntPtr.Zero && fgHwnd != _overlayHwnd && !_minimizingOrHiddenHwnds.Contains(fgHwnd))
    {
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (_surfaces[i].Handle == fgHwnd)
                return;
        }

        if (TryBuildSurface(fgHwnd, allowIgnoreTransparentFlag: true, out TrackedSurface newFgSurf) && !newFgSurf.IsTaskbar)
        {
            _surfaces.Insert(0, newFgSurf);
        }
    }
}

    private bool EnumWindowCallback(IntPtr hWnd, IntPtr lParam)
    {
        if (TryBuildSurface(hWnd, allowIgnoreTransparentFlag: false, out TrackedSurface surf))
        {
            _surfaces.Add(surf);
        }
        return true;
    }

    private bool TryBuildSurface(IntPtr hWnd, bool allowIgnoreTransparentFlag, out TrackedSurface surface)
    {
        surface = default;

        if (hWnd == IntPtr.Zero || hWnd == _overlayHwnd) return false;
        if (_minimizingOrHiddenHwnds.Contains(hWnd)) return false;
        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return false;

        if (!allowIgnoreTransparentFlag)
        {
            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0) return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        _classNameBuffer.Clear();
        bool isTaskbar = false;

        if (NativeMethods.GetClassName(hWnd, _classNameBuffer, _classNameBuffer.Capacity) > 0)
        {
            string cls = _classNameBuffer.ToString();

            if (string.Equals(cls, "Progman", StringComparison.Ordinal) ||
                string.Equals(cls, "WorkerW", StringComparison.Ordinal) ||
                string.Equals(cls, "Shell_InputSwitchTopLevelWindow", StringComparison.Ordinal) ||
                string.Equals(cls, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal) ||
                string.Equals(cls, "XamlExplorerHostIslandWindow", StringComparison.Ordinal) ||
                string.Equals(cls, "TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal) ||
                string.Equals(cls, "SysShadow", StringComparison.Ordinal) ||
                string.Equals(cls, "Frosty_DragCompanion_v1", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(cls, "Shell_TrayWnd", StringComparison.Ordinal) ||
                string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                isTaskbar = true;
            }
        }

        if (!isTaskbar)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == _ownPid) return false;
        }

        if (!TryGetAccurateWindowBounds(hWnd, isTaskbar, out int localLeft, out int localTop, out int localRight, out int localBottom, out bool isTopDocked))
            return false;

        surface = new TrackedSurface
        {
            Handle = hWnd,
            Left = localLeft,
            Right = localRight,
            TopY = localTop,
            BottomY = localBottom,
            IsTaskbar = isTaskbar,
            IsMaximizedOrTopDocked = isTopDocked
        };

        return true;
    }

    private bool TryGetAccurateWindowBounds(
        IntPtr hWnd,
        bool isTaskbar,
        out int localLeft,
        out int localTop,
        out int localRight,
        out int localBottom,
        out bool isTopDocked)
    {
        localLeft = localTop = localRight = localBottom = 0;
        isTopDocked = false;

        if (!NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rawRect))
            return false;

        bool isMaximized = !isTaskbar && NativeMethods.IsZoomed(hWnd);

        if (!_marginCache.TryGetValue(hWnd, out WindowMargin m))
        {
            m = ComputeWindowMargin(hWnd, in rawRect, isTaskbar);
            _marginCache[hWnd] = m;
        }

        localLeft = (rawRect.Left + m.Left) - _originX;
        localRight = (rawRect.Right - m.Right) - _originX;
        localTop = (rawRect.Top + m.Top) - _originY;
        localBottom = (rawRect.Bottom - m.Bottom) - _originY;

        int width = localRight - localLeft;
        int height = localBottom - localTop;

        if (isTaskbar ? (width < 20 || height < 20) : (width < 80 || height < 20)) return false;
        if (localRight <= 0 || localLeft >= _screenWidth || localBottom <= 0 || localTop >= _screenHeight)
            return false;

        bool coversScreen = width >= _screenWidth - 40 && height >= _screenHeight - 80;
        isTopDocked = !isTaskbar && (isMaximized || coversScreen);

        return true;
    }

    private static WindowMargin ComputeWindowMargin(IntPtr hWnd, in NativeMethods.RECT rawRect, bool isTaskbar)
    {
        int marginLeft = 0, marginRight = 0, marginTop = 0, marginBottom = 0;

        int hr = NativeMethods.DwmGetWindowAttribute(
            hWnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT dwmRect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0 && dwmRect.Right > dwmRect.Left && dwmRect.Bottom > dwmRect.Top)
        {
            marginLeft = Math.Clamp(dwmRect.Left - rawRect.Left, 0, 32);
            marginRight = Math.Clamp(rawRect.Right - dwmRect.Right, 0, 32);
            marginTop = Math.Clamp(dwmRect.Top - rawRect.Top, 0, 32);
            marginBottom = Math.Clamp(rawRect.Bottom - dwmRect.Bottom, 0, 32);
        }

        return new WindowMargin
        {
            Left = marginLeft,
            Right = marginRight,
            Top = marginTop,
            Bottom = marginBottom,
            IsTaskbar = isTaskbar
        };
    }

    public void Dispose()
    {
        if (_systemEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_systemEventHook);
            _systemEventHook = IntPtr.Zero;
        }

        if (_objectEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_objectEventHook);
            _objectEventHook = IntPtr.Zero;
        }
    }
}