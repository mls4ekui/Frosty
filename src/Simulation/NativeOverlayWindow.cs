using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Frosty;

             
                                                       
                                                                                     
                                                  
              
public sealed class DragCompanionWindow : IDisposable
{
    private const string CompanionClassName = "Frosty_DragCompanion_v1";
    public const int CompanionHeight = 148;
    public const int CompanionBaseOffsetY = 136;                                                                  

    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint ULW_ALPHA = 0x00000002;
    private const int WS_EX_TOPMOST = 0x00000008;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private readonly WndProcDelegate _wndProc;

    private IntPtr _hwnd;
    private IntPtr _hInstance;
    private ushort _classAtom;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private SKSurface? _surface;

    private int _originX;
    private int _originY;
    private int _bufWidth;
    private int _screenX;
    private int _screenY;
    private int _currentWidth;
    private IntPtr _attachedTargetHwnd;
    private bool _isVisible;
    private bool _disposed;

    public IntPtr AttachedWindow => _isVisible ? _attachedTargetHwnd : IntPtr.Zero;

    public DragCompanionWindow(int originX, int originY)
    {
        _originX = originX;
        _originY = originY;
        _wndProc = WndProc;
        _hInstance = NativeOverlayWindow.Win32.GetModuleHandle(null);

        var wc = new NativeOverlayWindow.Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeOverlayWindow.Win32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = CompanionClassName
        };

        _classAtom = NativeOverlayWindow.Win32.RegisterClassEx(ref wc);

        int exStyle = NativeMethods.WS_EX_LAYERED |
                      NativeMethods.WS_EX_TRANSPARENT |
                      NativeMethods.WS_EX_TOOLWINDOW |
                      NativeMethods.WS_EX_NOACTIVATE |
                      WS_EX_TOPMOST;

        _hwnd = NativeOverlayWindow.Win32.CreateWindowEx(
            (uint)exStyle,
            CompanionClassName,
            "FrostyCompanion",
            unchecked((uint)NativeMethods.WS_POPUP),
            0, 0, 256, CompanionHeight,
            IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

        EnsureBufferWidth(512);
    }

    public void UpdateOrigin(int originX, int originY)
    {
        _originX = originX;
        _originY = originY;
    }

                 
                                                                                                             
                  
    public void MoveOrBeginDrag(IntPtr targetHwnd, int localLeft, int localTopY, int localRight, SnowSimulationEngine engine)
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;

        int width = Math.Max(64, localRight - localLeft);
        int newScreenX = _originX + localLeft;
        int newScreenY = _originY + localTopY - CompanionBaseOffsetY;

        bool isNewTarget = !_isVisible || _attachedTargetHwnd != targetHwnd || width != _currentWidth;

        _attachedTargetHwnd = targetHwnd;
        _screenX = newScreenX;
        _screenY = newScreenY;
        _currentWidth = width;

        if (isNewTarget)
        {
            EnsureBufferWidth(width);
            RenderAndCommitBits(engine);
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                _screenX,
                _screenY,
                _currentWidth,
                CompanionHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            _isVisible = true;
        }
        else
        {
                                                                                              
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                _screenX,
                _screenY,
                0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

                                                                                                                          
    public void RefreshVisualsIfVisible(SnowSimulationEngine engine)
    {
        if (!_isVisible || _disposed || _attachedTargetHwnd == IntPtr.Zero) return;
        RenderAndCommitBits(engine);
    }

    public void EndDrag()
    {
        if (!_isVisible || _hwnd == IntPtr.Zero) return;
        _isVisible = false;
        _attachedTargetHwnd = IntPtr.Zero;
        NativeOverlayWindow.Win32.ShowWindow(_hwnd, SW_HIDE);
    }

    private unsafe void RenderAndCommitBits(SnowSimulationEngine engine)
    {
        if (_surface == null || _hwnd == IntPtr.Zero) return;

        SKCanvas canvas = _surface.Canvas;
        engine.RenderSingleWindowCompanion(canvas, _attachedTargetHwnd, _currentWidth, CompanionBaseOffsetY);
        canvas.Flush();

        var dst = new NativeOverlayWindow.Win32.POINT { X = _screenX, Y = _screenY };
        var size = new NativeOverlayWindow.Win32.SIZE { cx = _currentWidth, cy = CompanionHeight };
        var src = new NativeOverlayWindow.Win32.POINT { X = 0, Y = 0 };
        var blend = new NativeOverlayWindow.Win32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };
        var dirty = new NativeOverlayWindow.Win32.RECT { Left = 0, Top = 0, Right = _currentWidth, Bottom = CompanionHeight };

        var info = new NativeOverlayWindow.Win32.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(NativeOverlayWindow.Win32.UPDATELAYEREDWINDOWINFO),
            hdcDst = IntPtr.Zero,
            pptDst = &dst,
            psize = &size,
            hdcSrc = _memDc,
            pptSrc = &src,
            crKey = 0,
            pblend = &blend,
            dwFlags = ULW_ALPHA,
            prcDirty = &dirty
        };

        NativeOverlayWindow.Win32.UpdateLayeredWindowIndirect(_hwnd, ref info);
    }

    private void EnsureBufferWidth(int requiredWidth)
    {
        if (requiredWidth <= _bufWidth && _surface != null) return;

        DisposeBuffer();
        _bufWidth = Math.Max(requiredWidth + 128, 512);

        var bmi = new NativeOverlayWindow.Win32.BITMAPINFO
        {
            bmiHeader = new NativeOverlayWindow.Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeOverlayWindow.Win32.BITMAPINFOHEADER>(),
                biWidth = _bufWidth,
                biHeight = -CompanionHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        IntPtr screenDc = NativeOverlayWindow.Win32.GetDC(IntPtr.Zero);
        try
        {
            _memDc = NativeOverlayWindow.Win32.CreateCompatibleDC(screenDc);
            _dib = NativeOverlayWindow.Win32.CreateDIBSection(screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        }
        finally
        {
            NativeOverlayWindow.Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }

        _oldBitmap = NativeOverlayWindow.Win32.SelectObject(_memDc, _dib);
        var info = new SKImageInfo(_bufWidth, CompanionHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _bits, _bufWidth * 4);
    }

    private void DisposeBuffer()
    {
        _surface?.Dispose();
        _surface = null;

        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
            NativeOverlayWindow.Win32.SelectObject(_memDc, _oldBitmap);
        _oldBitmap = IntPtr.Zero;

        if (_dib != IntPtr.Zero)
        {
            NativeOverlayWindow.Win32.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            NativeOverlayWindow.Win32.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        _bits = IntPtr.Zero;
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x0021) return new IntPtr(3);                                     
        if (msg == 0x0014) return new IntPtr(1);                 
        return NativeOverlayWindow.Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            NativeOverlayWindow.Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        DisposeBuffer();
        if (_classAtom != 0)
        {
            NativeOverlayWindow.Win32.UnregisterClass(CompanionClassName, _hInstance);
            _classAtom = 0;
        }
    }
}

public sealed class NativeOverlayWindow : IDisposable
{
    private const string WindowClassName = "Frosty_NativeOverlay_v1";

    private const uint WM_APP_FRAME = 0x8000 + 0x21;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_ERASEBKGND = 0x0014;

    private const int MA_NOACTIVATE = 3;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int IDC_ARROW = 32512;
    private const uint ULW_ALPHA = 0x00000002;
    private const int WS_EX_TOPMOST = 0x00000008;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly WndProcDelegate _wndProc;

    private IntPtr _hwnd;
    private IntPtr _hInstance;
    private ushort _classAtom;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private SKSurface? _surface;

    private int _x;
    private int _y;
    private int _w;
    private int _h;

    private bool _isShown;
    private bool _disposed;

    private volatile bool _pacerRunning;
    private volatile bool _lowPowerMode;
    private volatile int _targetFps = 60;
    private int _pacerGeneration;
    private int _framePending;

    private int _logCount;

    public event Action? FrameRequested;
    public event Func<bool>? LeftButtonDown;
    public event Action? LeftButtonUp;
    public event Func<bool>? RightButtonDown;
    public event Action? MouseCaptureLost;
    public event Action<int>? HotkeyPressed;

    public IntPtr Handle => _hwnd;
    public bool IsShown => _isShown;
    public bool IsMouseCaptured => _hwnd != IntPtr.Zero && Win32.GetCapture() == _hwnd;

    public void SetLowPowerMode(bool lowPower) => _lowPowerMode = lowPower;
    public void SetTargetFps(int fps) => _targetFps = fps;

    public NativeOverlayWindow(int x, int y, int width, int height)
    {
        _x = x;
        _y = y;
        _w = Math.Max(1, width);
        _h = Math.Max(1, height);

        _wndProc = WndProc;
        _hInstance = Win32.GetModuleHandle(null);

        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = _hInstance,
            hIcon = IntPtr.Zero,
            hCursor = Win32.LoadCursor(IntPtr.Zero, new IntPtr(IDC_ARROW)),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName,
            hIconSm = IntPtr.Zero
        };

        _classAtom = Win32.RegisterClassEx(ref wc);
        if (_classAtom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1410)
            {
                throw new InvalidOperationException($"RegisterClassEx failed, error {err}");
            }
        }

        int exStyle = NativeMethods.WS_EX_LAYERED |
                      NativeMethods.WS_EX_TRANSPARENT |
                      NativeMethods.WS_EX_TOOLWINDOW |
                      NativeMethods.WS_EX_NOACTIVATE |
                      WS_EX_TOPMOST;

        _hwnd = Win32.CreateWindowEx(
            (uint)exStyle,
            WindowClassName,
            "Frosty",
            unchecked((uint)NativeMethods.WS_POPUP),
            _x, _y, _w, _h,
            IntPtr.Zero,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed, error {Marshal.GetLastWin32Error()}");
        }

        CreateBackBuffer();
    }

    public void Show()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        Win32.ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        _isShown = true;
    }

    public void Hide()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        Win32.ShowWindow(_hwnd, SW_HIDE);
        _isShown = false;
    }

    public void Resize(int x, int y, int width, int height)
    {
        if (_disposed) return;

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        bool sizeChanged = width != _w || height != _h;

        _x = x;
        _y = y;
        _w = width;
        _h = height;

        if (sizeChanged)
        {
            CreateBackBuffer();
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            _x, _y, _w, _h,
            NativeMethods.SWP_NOACTIVATE);
    }

    public void ReleaseMouse()
    {
        if (IsMouseCaptured)
        {
            Win32.ReleaseCapture();
        }
    }

    public void StartFrameLoop()
    {
        if (_disposed || _pacerRunning) return;

        _pacerRunning = true;
        int gen = Interlocked.Increment(ref _pacerGeneration);
        Interlocked.Exchange(ref _framePending, 0);

        var thread = new Thread(PacerLoop)
        {
            IsBackground = true,
            Name = "Frosty.VSyncPacer",
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start(gen);
    }

    public void StopFrameLoop()
    {
        _pacerRunning = false;
        Interlocked.Increment(ref _pacerGeneration);
    }

    private void PacerLoop(object? state)
    {
        int gen = state is int g ? g : 0;
        long lastFrameTick = 0;

        while (_pacerRunning && gen == Volatile.Read(ref _pacerGeneration))
        {
            long t0 = Stopwatch.GetTimestamp();
            Win32.DwmFlush();
            long nowTick = Stopwatch.GetTimestamp();
            double ms = (nowTick - t0) * 1000.0 / Stopwatch.Frequency;

            if (ms < 1.0)
            {
                Thread.Sleep(12);
                nowTick = Stopwatch.GetTimestamp();
            }

            if (!_pacerRunning || gen != Volatile.Read(ref _pacerGeneration)) break;

            int desiredFps = _lowPowerMode ? 15 : _targetFps;
            if (desiredFps > 0 && lastFrameTick > 0)
            {
                double elapsedMs = (nowTick - lastFrameTick) * 1000.0 / Stopwatch.Frequency;
                double minIntervalMs = (1000.0 / desiredFps) - 1.2;
                if (elapsedMs < minIntervalMs)
                {
                    continue;
                }
            }

            IntPtr hwnd = _hwnd;
            if (hwnd == IntPtr.Zero) break;

            if (Interlocked.CompareExchange(ref _framePending, 1, 0) == 0)
            {
                lastFrameTick = nowTick;
                if (!Win32.PostMessage(hwnd, WM_APP_FRAME, IntPtr.Zero, IntPtr.Zero))
                {
                    Interlocked.Exchange(ref _framePending, 0);
                }
            }
        }
    }

    public unsafe void Present(SnowSimulationEngine engine)
    {
        if (_disposed || !_isShown || _surface == null || _hwnd == IntPtr.Zero) return;

        SKCanvas canvas = _surface.Canvas;
        engine.Render(canvas, _w, _h);
        canvas.Flush();

        var dst = new Win32.POINT { X = _x, Y = _y };
        var size = new Win32.SIZE { cx = _w, cy = _h };
        var src = new Win32.POINT { X = 0, Y = 0 };
        var blend = new Win32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        var dirty = new Win32.RECT { Left = 0, Top = 0, Right = _w, Bottom = _h };
        var info = new Win32.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(Win32.UPDATELAYEREDWINDOWINFO),
            hdcDst = IntPtr.Zero,
            pptDst = &dst,
            psize = &size,
            hdcSrc = _memDc,
            pptSrc = &src,
            crKey = 0,
            pblend = &blend,
            dwFlags = ULW_ALPHA,
            prcDirty = &dirty
        };

        if (!Win32.UpdateLayeredWindowIndirect(_hwnd, ref info))
        {
            Log($"UpdateLayeredWindowIndirect failed, error {Marshal.GetLastWin32Error()}");
        }
    }

    private void CreateBackBuffer()
    {
        DisposeBackBuffer();

        var bmi = new Win32.BITMAPINFO
        {
            bmiHeader = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = _w,
                biHeight = -_h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            },
            bmiColors = 0
        };

        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        try
        {
            _memDc = Win32.CreateCompatibleDC(screenDc);
            _dib = Win32.CreateDIBSection(screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero || _bits == IntPtr.Zero)
        {
            DisposeBackBuffer();
            throw new InvalidOperationException("Не удалось создать буфер оверлея (CreateDIBSection).");
        }

        _oldBitmap = Win32.SelectObject(_memDc, _dib);

        var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _bits, _w * 4);
        if (_surface == null)
        {
            DisposeBackBuffer();
            throw new InvalidOperationException("Не удалось создать SKSurface поверх DIB-буфера.");
        }
    }

    private void DisposeBackBuffer()
    {
        _surface?.Dispose();
        _surface = null;

        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
        {
            Win32.SelectObject(_memDc, _oldBitmap);
        }
        _oldBitmap = IntPtr.Zero;

        if (_dib != IntPtr.Zero)
        {
            Win32.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            Win32.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        _bits = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_APP_FRAME:
                    try
                    {
                        FrameRequested?.Invoke();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _framePending, 0);
                    }
                    return IntPtr.Zero;

                case WM_LBUTTONDOWN:
                    if (LeftButtonDown?.Invoke() == true)
                    {
                        Win32.SetCapture(hwnd);
                    }
                    return IntPtr.Zero;

                case WM_LBUTTONUP:
                    LeftButtonUp?.Invoke();
                    if (Win32.GetCapture() == hwnd)
                    {
                        Win32.ReleaseCapture();
                    }
                    return IntPtr.Zero;

                case WM_RBUTTONDOWN:
                    RightButtonDown?.Invoke();
                    return IntPtr.Zero;

                case WM_CAPTURECHANGED:
                    MouseCaptureLost?.Invoke();
                    return IntPtr.Zero;

                case WM_MOUSEACTIVATE:
                    return new IntPtr(MA_NOACTIVATE);

                case WM_HOTKEY:
                    HotkeyPressed?.Invoke(wParam.ToInt32());
                    return IntPtr.Zero;

                case WM_ERASEBKGND:
                    return new IntPtr(1);
            }
        }
        catch (Exception ex)
        {
            Log("WndProc exception: " + ex);
            return IntPtr.Zero;
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Log(string message)
    {
        if (_logCount++ > 30) return;
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Frosty");
            System.IO.Directory.CreateDirectory(dir);
            string logFile = System.IO.Path.Combine(dir, "overlay_log.txt");
            if (System.IO.File.Exists(logFile) && new System.IO.FileInfo(logFile).Length > 65536)
            {
                System.IO.File.WriteAllText(logFile, string.Empty);
            }
            System.IO.File.AppendAllText(
                logFile,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopFrameLoop();
        _isShown = false;
        _disposed = true;

        IntPtr hwnd = _hwnd;
        _hwnd = IntPtr.Zero;

        if (hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(hwnd);
        }

        DisposeBackBuffer();

        if (_classAtom != 0)
        {
            Win32.UnregisterClass(WindowClassName, _hInstance);
            _classAtom = 0;
        }
    }

    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct UPDATELAYEREDWINDOWINFO
        {
            public uint cbSize;
            public IntPtr hdcDst;
            public POINT* pptDst;
            public SIZE* psize;
            public IntPtr hdcSrc;
            public POINT* pptSrc;
            public uint crKey;
            public BLENDFUNCTION* pblend;
            public uint dwFlags;
            public RECT* prcDirty;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr GetCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref UPDATELAYEREDWINDOWINFO pULWInfo);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BITMAPINFO pbmi,
            uint usage,
            out IntPtr ppvBits,
            IntPtr hSection,
            uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr ho);

        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();
    }
}