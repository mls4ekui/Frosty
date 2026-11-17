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
        using (var clip = new SKRoundRect(new SKRect(0, 0, w, h), 14f * (float)(w / Math.Max(1.0, ActualWidth))))
        {
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
        }

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

        using (var line = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(71, 85, 105, 110) })
        {
            float lx = win.Left + 16 * u;
            float ly = win.TopY + 40 * u;
            float maxW = win.Right - win.Left - 32 * u;
            float[] widths = { 0.78f, 0.55f, 0.66f, 0.40f };
            foreach (float k in widths)
            {
                if (ly + 6 * u > win.BottomY - 6 * u) break;
                canvas.DrawRoundRect(lx, ly, maxW * k, 6 * u, 3 * u, 3 * u, line);
                ly += 16 * u;
            }
        }

        using (var dot = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver })
        {
            SKColor[] colors = { new(74, 222, 128), new(250, 204, 21), new(248, 113, 113) };
            for (int i = 0; i < 3; i++)
            {
                dot.Color = colors[2 - i];
                canvas.DrawCircle(win.Left + (14 + (2 - i) * 13) * u, win.TopY + 14 * u, 4 * u, dot);
            }
        }

        using (var title = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(40, 48, 64) })
        {
            canvas.DrawRoundRect(win.Left, win.TopY, win.Right - win.Left, 28 * u, 8 * u, 8 * u, title);
        }

        using (var body = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(28, 34, 46) })
        using (var border = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f * u, Color = new SKColor(71, 85, 105, 180) })
        {
            var rect = new SKRect(win.Left, win.TopY, win.Right, win.BottomY + 12 * u);
            canvas.DrawRoundRect(rect, 8 * u, 8 * u, border);
            canvas.DrawRoundRect(rect, 8 * u, 8 * u, body);
        }

        using (var tb = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(15, 20, 30) })
        {
            canvas.DrawRect(0, bar.TopY, w, h - bar.TopY, tb);
        }
        using (var icon = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(56, 189, 248, 190) })
        using (var icon2 = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOver, Color = new SKColor(100, 116, 139, 160) })
        {
            float cy = bar.TopY + (h - bar.TopY) / 2f;
            float s = Math.Min(16f * u, (h - bar.TopY) * 0.42f);
            for (int i = 0; i < 4; i++)
            {
                float x = w / 2f - 2.2f * s + i * 1.6f * s;
                canvas.DrawRoundRect(x, cy - s / 2f, s, s, s * 0.25f, s * 0.25f, i == 0 ? icon : icon2);
            }
        }

        using var bg = new SKPaint
        {
            BlendMode = SKBlendMode.DstOver,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, h),
                new[] { new SKColor(15, 23, 42), new SKColor(30, 41, 70), new SKColor(23, 32, 56) },
                new[] { 0f, 0.7f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, w, h, bg);
    }
}