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
    private readonly Action<SnowPreset>? _onSelectPreset;
    private readonly IntPtr _iconHandle;
    private readonly Icon _trayIcon;

    private ToolStripMenuItem? _settingsMenuItem;
    private ToolStripMenuItem? _pauseMenuItem;
    private ToolStripMenuItem? _clearMenuItem;
    private ToolStripMenuItem? _presetsMenuItem;
    private ToolStripMenuItem? _presetCozyItem;
    private ToolStripMenuItem? _presetClassicItem;
    private ToolStripMenuItem? _presetHeavyItem;
    private ToolStripMenuItem? _presetCyberItem;
    private ToolStripMenuItem? _exitMenuItem;
    private bool _lastManualPaused;
    private bool _lastAutoPaused;
    private SnowPreset? _activePreset;
    private bool _isDisposed;

    public TrayService(
        SnowSimulationEngine engine,
        Action onOpenSettings,
        Action onManualPauseToggle,
        Action onExit,
        Action<SnowPreset>? onSelectPreset = null)
    {
        _engine = engine;
        _onOpenSettings = onOpenSettings;
        _onManualPauseToggle = onManualPauseToggle;
        _onExit = onExit;
        _onSelectPreset = onSelectPreset;

        TryEnableWin32DarkModeMenus();

        (_trayIcon, _iconHandle) = AppIcon.CreateIcon(32);
        _contextMenu = BuildCompactContextMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = Loc.Tr("Frosty – Снег на рабочем столе", "Frosty – Desktop Snow"),
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
        _notifyIcon.Text = Loc.Tr("Frosty – Снег на рабочем столе", "Frosty – Desktop Snow");
        if (_settingsMenuItem != null)
            _settingsMenuItem.Text = Loc.Tr("Пульт снега (Настройки)", "Snow Control Panel (Settings)");
        if (_clearMenuItem != null)
            _clearMenuItem.Text = Loc.Tr("Очистить весь снег (Ctrl+Alt+C)", "Clear all snow (Ctrl+Alt+C)");
        if (_presetsMenuItem != null)
            _presetsMenuItem.Text = Loc.Tr("Пресеты погоды", "Weather presets");
        UpdateActivePreset(_activePreset);
        if (_exitMenuItem != null)
            _exitMenuItem.Text = Loc.Tr("Выход", "Exit");

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

    public void UpdateActivePreset(SnowPreset? activePreset)
    {
        _activePreset = activePreset;
        StylePresetItem(_presetCozyItem, Loc.Tr("Уютный", "Cozy"), activePreset == SnowPreset.Cozy);
        StylePresetItem(_presetClassicItem, Loc.Tr("Классика", "Classic"), activePreset == SnowPreset.Classic);
        StylePresetItem(_presetHeavyItem, Loc.Tr("Снегопад", "Heavy Snow"), activePreset == SnowPreset.Heavy);
        StylePresetItem(_presetCyberItem, Loc.Tr("Киберпанк", "Cyberpunk"), activePreset == SnowPreset.Cyber);
    }

    private static void StylePresetItem(ToolStripMenuItem? item, string baseLabel, bool isActive)
    {
        if (item == null) return;
        item.Checked = false;
        item.Text = isActive ? $"●  {baseLabel}" : $"    {baseLabel}";
        item.ForeColor = isActive ? Color.FromArgb(56, 189, 248) : Color.FromArgb(243, 244, 246);
        item.Font = new Font("Segoe UI", 9.5f, isActive ? FontStyle.Bold : FontStyle.Regular);
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
            _pauseMenuItem.Text = Loc.Tr("Возобновить снегопад (Ctrl+Alt+S)", "Resume snowfall (Ctrl+Alt+S)");
        }
        else if (isAutoPaused)
        {
            _pauseMenuItem.Text = Loc.Tr("На автопаузе (запущена игра)", "Auto-paused (fullscreen app)");
        }
        else
        {
            _pauseMenuItem.Text = Loc.Tr("Приостановить снегопад (Ctrl+Alt+S)", "Pause snowfall (Ctrl+Alt+S)");
        }
    }

    private ContextMenuStrip BuildCompactContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(15, 23, 38),
            ForeColor = Color.FromArgb(243, 244, 246),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(1, 4, 1, 3)
        };

        _settingsMenuItem = new ToolStripMenuItem("Пульт снега (Настройки)", null, (_, _) => _onOpenSettings())
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Padding = new Padding(10, 5, 10, 5)
        };
        menu.Items.Add(_settingsMenuItem);

        _presetsMenuItem = new ToolStripMenuItem("Пресеты погоды")
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(10, 5, 10, 5)
        };
        if (_presetsMenuItem.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.Renderer = new DarkMenuRenderer();
            dropDown.ShowImageMargin = false;
            dropDown.ShowCheckMargin = false;
            dropDown.BackColor = Color.FromArgb(15, 23, 38);
            dropDown.ForeColor = Color.FromArgb(243, 244, 246);
        }

        _presetCozyItem = CreatePresetItem("Уютный", SnowPreset.Cozy);
        _presetClassicItem = CreatePresetItem("Классика", SnowPreset.Classic);
        _presetHeavyItem = CreatePresetItem("Снегопад", SnowPreset.Heavy);
        _presetCyberItem = CreatePresetItem("Киберпанк", SnowPreset.Cyber);

        _presetsMenuItem.DropDownItems.Add(_presetCozyItem);
        _presetsMenuItem.DropDownItems.Add(_presetClassicItem);
        _presetsMenuItem.DropDownItems.Add(_presetHeavyItem);
        _presetsMenuItem.DropDownItems.Add(_presetCyberItem);
        menu.Items.Add(_presetsMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        _pauseMenuItem = new ToolStripMenuItem("Приостановить снегопад (Ctrl+Alt+S)", null, (_, _) => _onManualPauseToggle())
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(10, 5, 10, 5)
        };
        menu.Items.Add(_pauseMenuItem);

        _clearMenuItem = new ToolStripMenuItem("Очистить весь снег (Ctrl+Alt+C)", null, (_, _) => _engine.ClearAllSnow())
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(10, 5, 10, 5)
        };
        menu.Items.Add(_clearMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        _exitMenuItem = new ToolStripMenuItem("Выход", null, (_, _) => _onExit())
        {
            ForeColor = Color.FromArgb(248, 113, 113),
            Padding = new Padding(10, 5, 10, 5)
        };
        menu.Items.Add(_exitMenuItem);

        return menu;
    }

    private ToolStripMenuItem CreatePresetItem(string label, SnowPreset preset)
    {
        return new ToolStripMenuItem(label, null, (_, _) => _onSelectPreset?.Invoke(preset))
        {
            ForeColor = Color.FromArgb(243, 244, 246),
            Padding = new Padding(8, 4, 8, 4)
        };
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(148, 163, 184);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var borderPen = new Pen(Color.FromArgb(34, 48, 71), 1f);
            g.DrawRectangle(borderPen, r);

            // Верхний голубой кант 2px, как у окон на сайте
            using var topAccent = new SolidBrush(Color.FromArgb(56, 189, 248));
            g.FillRectangle(topAccent, 0, 0, e.ToolStrip.Width, 2);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            if (e.Item.Selected)
            {
                bool isExit = e.Item.ForeColor.R > 230 && e.Item.ForeColor.G < 130;
                using var bg = new SolidBrush(isExit ? Color.FromArgb(48, 225, 29, 72) : Color.FromArgb(23, 34, 54));
                using var br = new Pen(isExit ? Color.FromArgb(225, 29, 72) : Color.FromArgb(56, 189, 248), 1f);
                using var bar = new SolidBrush(isExit ? Color.FromArgb(225, 29, 72) : Color.FromArgb(56, 189, 248));
                g.FillRectangle(bg, rect);
                g.DrawRectangle(br, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                g.FillRectangle(bar, rect.X, rect.Y, 3, rect.Height);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem item)
            {
                // Рисуем текст сами и центрируем по всей высоте пункта: штатная раскладка
                // с большими Padding и нестандартным шрифтом поднимает текст вверх.
                Color color = item.Selected ? Color.White : item.ForeColor;
                if (!item.Enabled) color = Color.FromArgb(107, 114, 128);

                var textRect = new Rectangle(
                    item.Padding.Left,
                    0,
                    Math.Max(1, item.Width - item.Padding.Left - item.Padding.Right),
                    item.Height);

                TextRenderer.DrawText(
                    e.Graphics,
                    item.Text,
                    item.Font,
                    textRect,
                    color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                return;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(34, 48, 71), 1f);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(15, 23, 38);
        public override Color MenuBorder => Color.FromArgb(34, 48, 71);
        public override Color MenuItemBorder => Color.FromArgb(56, 189, 248);
        public override Color MenuItemSelected => Color.FromArgb(23, 34, 54);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(23, 34, 54);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(23, 34, 54);
        public override Color ImageMarginGradientBegin => Color.FromArgb(15, 23, 38);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(15, 23, 38);
        public override Color ImageMarginGradientEnd => Color.FromArgb(15, 23, 38);
        public override Color SeparatorDark => Color.FromArgb(34, 48, 71);
        public override Color SeparatorLight => Color.FromArgb(34, 48, 71);
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