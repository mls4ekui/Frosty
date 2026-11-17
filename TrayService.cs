using System.Drawing;
using System.Windows.Forms;

namespace Frosty;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly SnowSimulationEngine _engine;
    private readonly Action _onOpenSettings;
    private readonly Action _onManualPauseToggle;
    private readonly Action _onExit;
    private readonly IntPtr _iconHandle;
    private readonly Icon _trayIcon;

    private ToolStripMenuItem? _settingsMenuItem;
    private ToolStripMenuItem? _pauseMenuItem;
    private ToolStripMenuItem? _clearMenuItem;
    private ToolStripMenuItem? _exitMenuItem;
    private bool _lastManualPaused;
    private bool _lastAutoPaused;
    private bool _isDisposed;

    public TrayService(
        SnowSimulationEngine engine,
        Action onOpenSettings,
        Action onManualPauseToggle,
        Action onExit)
    {
        _engine = engine;
        _onOpenSettings = onOpenSettings;
        _onManualPauseToggle = onManualPauseToggle;
        _onExit = onExit;

        TryEnableWin32DarkModeMenus();

        (_trayIcon, _iconHandle) = AppIcon.CreateIcon(32);
        _contextMenu = BuildCompactContextMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = Loc.Tr("Frosty — Снег на рабочем столе", "Frosty — Desktop Snow"),
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _onOpenSettings();
            }
        };

        Loc.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
    }

    private void OnLanguageChanged()
    {
        if (_isDisposed) return;
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        _notifyIcon.Text = Loc.Tr("Frosty — Снег на рабочем столе", "Frosty — Desktop Snow");
        if (_settingsMenuItem != null)
            _settingsMenuItem.Text = Loc.Tr("⚙️ Настройки...", "⚙️ Settings...");
        if (_clearMenuItem != null)
            _clearMenuItem.Text = Loc.Tr("🧹 Очистить весь снег (Ctrl+Alt+C)", "🧹 Clear all snow (Ctrl+Alt+C)");
        if (_exitMenuItem != null)
            _exitMenuItem.Text = Loc.Tr("✖️ Выход", "✖️ Exit");

        UpdateState(_lastManualPaused, _lastAutoPaused);
    }

    public bool TryGetVisibleMenuBounds(out NativeMethods.RECT rect)
    {
        if (!_isDisposed && _contextMenu.Visible)
        {
            Rectangle b = _contextMenu.Bounds;
            if (b.Width > 0 && b.Height > 0)
            {
                rect = new NativeMethods.RECT
                {
                    Left = b.Left,
                    Top = b.Top,
                    Right = b.Right,
                    Bottom = b.Bottom
                };
                return true;
            }
        }

        rect = default;
        return false;
    }

    private static void TryEnableWin32DarkModeMenus()
    {
        try
        {
            NativeMethods.SetPreferredAppMode(2);
            NativeMethods.FlushMenuThemes();
        }
        catch
        {
        }
    }

    public void UpdatePauseMenuText(bool isManualPaused)
    {
        UpdateState(isManualPaused, isAutoPaused: false);
    }

    public void UpdateState(bool isManualPaused, bool isAutoPaused)
    {
        _lastManualPaused = isManualPaused;
        _lastAutoPaused = isAutoPaused;

        if (_pauseMenuItem == null) return;

        if (isManualPaused)
        {
            _pauseMenuItem.Text = Loc.Tr("▶️ Возобновить снегопад (Ctrl+Alt+S)", "▶️ Resume snowfall (Ctrl+Alt+S)");
        }
        else if (isAutoPaused)
        {
            _pauseMenuItem.Text = Loc.Tr("⏸️ На автопаузе (запущена игра)", "⏸️ Auto-paused (fullscreen app)");
        }
        else
        {
            _pauseMenuItem.Text = Loc.Tr("⏸️ Приостановить снегопад (Ctrl+Alt+S)", "⏸️ Pause snowfall (Ctrl+Alt+S)");
        }
    }

    private ContextMenuStrip BuildCompactContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(24, 28, 36),
            ForeColor = Color.FromArgb(243, 244, 246),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
        };

        _settingsMenuItem = new ToolStripMenuItem("⚙️ Настройки...", null, (_, _) => _onOpenSettings())
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Padding = new Padding(6, 4, 6, 4)
        };
        menu.Items.Add(_settingsMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        _pauseMenuItem = new ToolStripMenuItem("⏸️ Приостановить снегопад (Ctrl+Alt+S)", null, (_, _) => _onManualPauseToggle())
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(6, 4, 6, 4)
        };
        menu.Items.Add(_pauseMenuItem);

        _clearMenuItem = new ToolStripMenuItem("🧹 Очистить весь снег (Ctrl+Alt+C)", null, (_, _) => _engine.ClearAllSnow())
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(6, 4, 6, 4)
        };
        menu.Items.Add(_clearMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        _exitMenuItem = new ToolStripMenuItem("✖️ Выход", null, (_, _) => _onExit())
        {
            ForeColor = Color.FromArgb(248, 113, 113),
            Padding = new Padding(6, 4, 6, 4)
        };
        menu.Items.Add(_exitMenuItem);

        return menu;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem && e.Item.Selected)
            {
                e.TextColor = Color.White;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(46, 54, 70), 1f);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(24, 28, 36);
        public override Color MenuBorder => Color.FromArgb(59, 71, 94);
        public override Color MenuItemBorder => Color.FromArgb(56, 189, 248);
        public override Color MenuItemSelected => Color.FromArgb(35, 45, 63);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(35, 45, 63);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(35, 45, 63);
        public override Color ImageMarginGradientBegin => Color.FromArgb(24, 28, 36);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 28, 36);
        public override Color ImageMarginGradientEnd => Color.FromArgb(24, 28, 36);
        public override Color SeparatorDark => Color.FromArgb(46, 54, 70);
        public override Color SeparatorLight => Color.FromArgb(46, 54, 70);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Loc.LanguageChanged -= OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _trayIcon.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
        }
    }
}