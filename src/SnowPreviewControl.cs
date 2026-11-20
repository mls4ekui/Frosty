using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Point = System.Windows.Point;

namespace Frosty;

public sealed class PreviewWindowTracker : IWindowTracker
{
    public static readonly IntPtr FakeWindowHandle = new(0x7F000001);
    public static readonly IntPtr FakeTaskbarHandle = new(0x7F000002);

    private TrackedSurface _window;
    private TrackedSurface _taskbar;

    public TrackedSurface Window => _window;
    public TrackedSurface Taskbar => _taskbar;
    public IntPtr ActivelyDraggedWindow => IntPtr.Zero;

    public void Layout(int width, int height)
    {
        int taskbarH = Math.Clamp((int)(height * 0.09f), 28, 48);
        int taskbarTop = height - taskbarH;

        _taskbar = new TrackedSurface
        {
            Handle = FakeTaskbarHandle,
            Left = 0,
            Right = width,
            TopY = taskbarTop,
            BottomY = height,
            IsTaskbar = true
        };

        _window = new TrackedSurface
        {
            Handle = FakeWindowHandle,
            Left = (int)(width * 0.10f),
            Right = (int)(width * 0.90f),
            TopY = (int)(height * 0.48f),
            BottomY = taskbarTop,
            IsTaskbar = false
        };
    }

    public bool IsWindowMinimizingOrHidden(IntPtr hwnd) => false;

    public void SyncSurfacesForFrame(List<TrackedSurface> destination)
    {
        destination.Clear();
        if (_window.Handle == IntPtr.Zero) return;
        destination.Add(_window);
        destination.Add(_taskbar);
    }

    public void RefreshMovedWindowImmediate(List<TrackedSurface> snapshot)
    {
    }
}

public sealed class SnowPreviewControl : SKElement
{
    private const double ReferenceArea = 1920.0 * 1080.0;

    private readonly PreviewWindowTracker _tracker = new();
    private readonly Stopwatch _clock = new();

    private SnowSimulationEngine? _engine;
    private AppSettings? _pendingSettings;
    private int _pxWidth;
    private int _pxHeight;
    private int _originX = int.MinValue;
    private int _originY = int.MinValue;
    private TimeSpan _lastFrame;
    private bool _running;
    private int _paintW;
    private int _paintH;

    public SnowPreviewControl()
    {
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Start(); else Stop();
        };

        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonDown += OnRightDown;
        MouseRightButtonUp += OnRightUp;
    }

    public void ApplySettings(AppSettings settings)
    {
        _pendingSettings = settings;
        if (_engine != null) ApplyPending();
    }

    public void ClearSnow() => _engine?.ClearAllSnow();

    public void Shutdown()
    {
        Stop();
        _engine?.Dispose();
        _engine = null;
    }

    private void Start()
    {
        if (_running || !IsLoaded || !IsVisible) return;
        _running = true;
        _clock.Restart();
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        int w = _paintW;
        int h = _paintH;
        if (w < 64 || h < 64)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
            h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
            if (w < 64 || h < 64) return;
            InvalidateVisual();
        }

        EnsureEngine(w, h);
        if (_engine == null) return;

        TimeSpan now = _clock.Elapsed;
        float dt = (float)(now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        dt = Math.Clamp(dt, 0.001f, 0.05f);

        SyncOrigin();
        _engine.Update(dt);
        InvalidateVisual();
    }

    private void EnsureEngine(int w, int h)
    {
        if (_engine == null)
        {
            _pxWidth = w;
            _pxHeight = h;
            _tracker.Layout(w, h);
            _engine = new SnowSimulationEngine(0, 0, w, h, _tracker);
            ApplyPending();
            return;
        }

        if (w != _pxWidth || h != _pxHeight)
        {
            _pxWidth = w;
            _pxHeight = h;
            _tracker.Layout(w, h);
            _originX = int.MinValue;
            ApplyPending();
        }
    }

    private void SyncOrigin()
    {
        if (_engine == null) return;

        Point p = PointToScreen(new Point(0, 0));
        int ox = (int)Math.Round(p.X);
        int oy = (int)Math.Round(p.Y);
        if (ox == _originX && oy == _originY) return;

        _originX = ox;
        _originY = oy;
        _engine.UpdateScreenBounds(ox, oy, _pxWidth, _pxHeight);
    }

    private void ApplyPending()
    {
        if (_engine == null || _pendingSettings == null) return;

        AppSettings s = _pendingSettings;
        s.ApplyToEngine(_engine);

        double area = (double)_pxWidth * _pxHeight;
        double factor = Math.Clamp(area / ReferenceArea, 0.02, 1.0);
        _engine.SetPreviewParticleCount((int)Math.Round(s.ParticleCount * factor));

        _engine.SetPowerSaving(false);
        _engine.SetKeepSnowOnHiddenWindows(true);
    }

    private (float X, float Y) LocalCursor()
    {
        NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
        return (pt.X - _originX, pt.Y - _originY);
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_engine == null) return;
        SyncOrigin();
        var (x, y) = LocalCursor();
        if (_engine.OnMouseDown(x, y))
        {
            CaptureMouse();
        }
        e.Handled = true;
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_engine == null) return;
        var (x, y) = LocalCursor();
        _engine.OnMouseUp(x, y);
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (_engine == null) return;
        SyncOrigin();
        var (x, y) = LocalCursor();
        _engine.OnRightMouseDown(x, y);
        e.Handled = true;
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        int w = e.Info.Width;
        int h = e.Info.Height;
        _paintW = w;
        _paintH = h;

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, w, h), SKClipOperation.Intersect, true);

        if (_engine != null)
        {
            _engine.Render(canvas, w, h);
        }
        else
        {
            canvas.Clear(SKColors.Transparent);
        }

        DrawMockDesktopBehind(canvas, w, h);
        canvas.Restore();
    }

    private void DrawMockDesktopBehind(SKCanvas canvas, int w, int h)
    {
        if (_tracker.Window.Handle == IntPtr.Zero) return;

        TrackedSurface win = _tracker.Window;
        TrackedSurface bar = _tracker.Taskbar;
        float u = Math.Max(1f, h / 380f);

        using (var line = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(51, 65, 85, 130) })
        {
            float lx = win.Left + 16 * u;
            float ly = win.TopY + 40 * u;
            float maxW = win.Right - win.Left - 32 * u;
            float[] widths = { 0.78f, 0.55f, 0.66f, 0.40f };
            foreach (float k in widths)
            {
                if (ly + 6 * u > win.BottomY - 6 * u) break;
                canvas.DrawRect(lx, ly, maxW * k, 6 * u, line);
                ly += 16 * u;
            }
        }

        using (var topBar = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(56, 189, 248) })
        {
            canvas.DrawRect(win.Left, win.TopY, win.Right - win.Left, 2 * u, topBar);
        }

        using (var title = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(21, 32, 51) })
        {
            canvas.DrawRect(win.Left, win.TopY + 2 * u, win.Right - win.Left, 26 * u, title);
        }

        using (var body = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(15, 23, 38) })
        using (var border = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f * u, Color = new SKColor(34, 48, 71, 220) })
        {
            var rect = new SKRect(win.Left, win.TopY, win.Right, win.BottomY + 12 * u);
            canvas.DrawRect(rect, border);
            canvas.DrawRect(rect, body);
        }

        using (var tb = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(10, 16, 28) })
        using (var tbBorder = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(34, 48, 71) })
        {
            canvas.DrawRect(0, bar.TopY, w, 1.2f * u, tbBorder);
            canvas.DrawRect(0, bar.TopY, w, h - bar.TopY, tb);
        }

        using (var icon = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(56, 189, 248, 200) })
        using (var icon2 = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.DstOver, Color = new SKColor(34, 48, 71, 200) })
        {
            float cy = bar.TopY + (h - bar.TopY) / 2f;
            float s = Math.Min(16f * u, (h - bar.TopY) * 0.42f);
            for (int i = 0; i < 4; i++)
            {
                float x = w / 2f - 2.2f * s + i * 1.6f * s;
                canvas.DrawRect(x, cy - s / 2f, s, s, i == 0 ? icon : icon2);
            }
        }

        using var bg = new SKPaint
        {
            BlendMode = SKBlendMode.DstOver,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, h),
                new[] { new SKColor(10, 16, 29), new SKColor(14, 24, 43), new SKColor(6, 10, 18) },
                new[] { 0f, 0.65f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, w, h, bg);
    }
}