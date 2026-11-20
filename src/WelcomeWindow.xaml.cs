using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Frosty;

public partial class WelcomeWindow : Window
{
    private const int TotalSteps = 5;
    private const int FirstSettingsStep = 2;

    private readonly AppSettings _live;
    private readonly AppSettings _settings;
    private readonly Action<bool> _onFinished;
    private readonly bool _isFirstRun;
    private readonly bool _initialAutoStart;

    private bool _autoStart;
    private bool _finished;
    private bool _isSyncing = true;
    private int _currentStep;

    private readonly FrameworkElement[] _stepPanels;
    private readonly Rectangle[] _dots = new Rectangle[TotalSteps];

    private static readonly System.Windows.Media.Color ActiveColor = System.Windows.Media.Color.FromRgb(56, 189, 248);
    private static readonly System.Windows.Media.Color InactiveColor = System.Windows.Media.Color.FromRgb(34, 48, 71);

    private sealed record SliderBinding(
        Slider Slider,
        TextBlock Label,
        Func<AppSettings, double> Get,
        Action<AppSettings, double> Set,
        string Format);

    private readonly List<SliderBinding> _sliders = new();

    public WelcomeWindow(AppSettings liveSettings, Action<bool> onFinished)
    {
        _live = liveSettings;
        _settings = liveSettings.Clone();
        _onFinished = onFinished;
        _isFirstRun = !liveSettings.IsFirstRunCompleted;
        _initialAutoStart = AppSettings.IsAutoStartEnabled();
        _autoStart = _initialAutoStart;

        InitializeComponent();

        Icon = AppIcon.WpfIcon;

        _stepPanels = new FrameworkElement[] { Step1Panel, Step2Panel, Step3Panel, Step4Panel, Step5Panel };

        for (int i = 0; i < TotalSteps; i++)
        {
            var dot = new Rectangle
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 6, 0),
                Fill = new SolidColorBrush(InactiveColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            _dots[i] = dot;
            DotsPanel.Children.Add(dot);
        }

        RegisterSliders();

        Loc.LanguageChanged += ApplyLocalization;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            Loc.LanguageChanged -= ApplyLocalization;
            Preview.Shutdown();
        };
    }

    public WelcomeWindow() : this(new AppSettings(), _ => { })
    {
    }

    private void RegisterSliders()
    {
        Add(DensitySlider, DensityValue, s => s.ParticleCount, (s, v) => s.ParticleCount = (int)Math.Round(v), "0");
        Add(SizeSlider, SizeValue, s => s.SizeMultiplier, (s, v) => s.SizeMultiplier = (float)v, "x");
        Add(SpeedSlider, SpeedValue, s => s.SpeedMultiplier, (s, v) => s.SpeedMultiplier = (float)v, "x");
        Add(WindSlider, WindValue, s => s.WindMultiplier, (s, v) => s.WindMultiplier = (float)v, "x");
        Add(HeightSlider, HeightValue, s => s.MaxSnowHeight, (s, v) => s.MaxSnowHeight = (float)v, "px");
        Add(AccumSlider, AccumValue, s => s.AccumulationRateMultiplier, (s, v) => s.AccumulationRateMultiplier = (float)v, "x");
        Add(SwaySlider, SwayValue, s => s.SwayAmplitudeMultiplier, (s, v) => s.SwayAmplitudeMultiplier = (float)v, "x");
        Add(ScatterPowerSlider, ScatterPowerValue, s => s.CursorScatterPower, (s, v) => s.CursorScatterPower = (float)v, "x");
        Add(GravitySlider, GravityValue, s => s.ObjectGravityMultiplier, (s, v) => s.ObjectGravityMultiplier = (float)v, "x");
    }

    private void Add(Slider slider, TextBlock label, Func<AppSettings, double> get, Action<AppSettings, double> set, string fmt)
    {
        var binding = new SliderBinding(slider, label, get, set, fmt);
        _sliders.Add(binding);
        slider.ValueChanged += (_, e) => OnSliderChanged(binding, e.NewValue);
    }

    private static string FormatValue(SliderBinding b, double v)
    {
        return b.Format switch
        {
            "0" => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture),
            "px" => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) + " px",
            _ => v.ToString("0.0", CultureInfo.InvariantCulture) + "×"
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryApplyWindows11Backdrop();
        SyncControlsFromSettings();
        ApplyLocalization();
        Preview.ApplySettings(_settings);
        ShowStep(0, 0);
        Motion.PlayWindowEntrance(RootBorder);
    }

    private void TryApplyWindows11Backdrop()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int cornerPref = NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch
        {
        }
    }

    public void ApplyLocalization()
    {
        Title = Loc.Tr("Мастер настройки Frosty", "Frosty Setup Wizard");
        HeaderTitleText.Text = Loc.Tr("Первоначальная настройка Frosty", "Frosty Initial Setup");

        S1Title.Text = Loc.Tr("Добро пожаловать в Frosty", "Welcome to Frosty");
        S1Sub.Text = Loc.Tr(
            "Живой снегопад прямо на рабочем столе. За минуту познакомимся и настроим всё под вас.",
            "Interactive snowfall right on your desktop. Let's take a minute to set everything up for you.");
        S1C1Title.Text = Loc.Tr("Снег идёт поверх всех окон", "Snow falls over all windows");
        S1C1Desc.Text = Loc.Tr(
            "Хлопья падают на рабочий стол, ложатся на заголовки окон и панель задач и образуют настоящие сугробы.",
            "Snowflakes fall across your desktop, land on window title bars and the taskbar, and form real snowdrifts.");
        S1C2Title.Text = Loc.Tr("Не мешает работе", "Never gets in your way");
        S1C2Desc.Text = Loc.Tr(
            "Окна, ярлыки и программы работают как обычно. В полноэкранных играх снегопад сам уходит на паузу.",
            "Windows, icons, and apps work as usual. In fullscreen games, snowfall pauses automatically.");
        S1C3Title.Text = Loc.Tr("Живёт в системном трее", "Lives in the system tray");
        S1C3Desc.Text = Loc.Tr(
            "Иконка возле часов открывает настройки. Ctrl+Alt+S – пауза, Ctrl+Alt+C – очистить весь снег.",
            "Click the tray icon near the clock to open settings. Ctrl+Alt+S – pause, Ctrl+Alt+C – clear all snow.");
        S1C4Title.Text = Loc.Tr("Всё видно в окне справа", "Live preview on the right");
        S1C4Desc.Text = Loc.Tr(
            "Пока открыт мастер, снег идёт только в окне предпросмотра. На рабочий стол он выйдет, когда вы нажмёте «Готово».",
            "While the wizard is open, snow falls only in the preview box. It will appear on your desktop once you click Done.");

        S2Title.Text = Loc.Tr("Как играть со снегом", "How to interact with snow");
        S2Sub.Text = Loc.Tr(
            "Снег – не просто картинка. С ним можно взаимодействовать. Попробуйте прямо в окне предпросмотра справа.",
            "Snow isn't just a static overlay – you can play with it. Try it right now in the preview box on the right.");
        S2C1Title.Text = Loc.Tr("Снежки и снеговики", "Snowballs & snowmen");
        S2C1Desc.Text = Loc.Tr(
            "Зажмите левую кнопку мыши на сугробе и потяните – слепится снежок. Сложите 2–3 кома вместе – получится снеговик.",
            "Hold LMB on a snowdrift and drag to roll a snowball. Stack 2–3 snowballs together to build a snowman.");
        S2C2Title.Text = Loc.Tr("Разгоните снег курсором", "Scatter snow with your cursor");
        S2C2Desc.Text = Loc.Tr(
            "Проведите мышью сквозь падающие хлопья – они разлетятся в стороны.",
            "Move your mouse quickly through falling snowflakes to scatter them around.");
        S2C3Title.Text = Loc.Tr("Снег стряхивается", "Shake windows to shed snow");
        S2C3Desc.Text = Loc.Tr(
            "Если резко потрясти окно, снег с него осыплется вниз. Брошенные снежки оставляют следы на экране.",
            "Shake a window rapidly side to side to shed its snowdrift. Thrown snowballs leave melting splatters on screen.");
        S2C4Title.Text = Loc.Tr("Всё это настраивается", "Everything is customizable");
        S2C4Desc.Text = Loc.Tr(
            "На следующих шагах вы выберете внешний вид, физику и поведение – и сразу увидите результат.",
            "In the next steps you'll choose the look, physics, and behavior – and see the result right away.");

        S3Title.Text = Loc.Tr("Погода и внешний вид", "Weather & Appearance");
        S3Sub.Text = Loc.Tr(
            "Выберите пресет и подкрутите детали – предпросмотр обновляется мгновенно.",
            "Pick a preset and fine-tune the details – the preview updates in real time.");
        S3PresetHdr.Text = Loc.Tr("ПРЕСЕТ", "PRESET");
        WPresetCozyTitle.Text = Loc.Tr("Уютный", "Cozy");
        WPresetCozyDesc.Text = Loc.Tr("Редкие, неспешные хлопья.", "Gentle, slow snowflakes.");
        WPresetClassicTitle.Text = Loc.Tr("Классика", "Classic");
        WPresetClassicDesc.Text = Loc.Tr("Сбалансированный вариант.", "Balanced everyday snowfall.");
        WPresetHeavyTitle.Text = Loc.Tr("Снегопад", "Heavy Snow");
        WPresetHeavyDesc.Text = Loc.Tr("Плотный, но спокойный снег.", "Dense yet calm snowfall.");
        WPresetCyberTitle.Text = Loc.Tr("Киберпанк", "Cyberpunk");
        WPresetCyberDesc.Text = Loc.Tr("Неоновые хлопья.", "Glowing neon snowflakes.");

        S3ColorHdr.Text = Loc.Tr("ЦВЕТ СНЕГА", "SNOW COLOR");
        WThemeWhiteTxt.Text = Loc.Tr("Классический", "Classic");
        WThemeNeonTxt.Text = Loc.Tr("Неон", "Neon");
        WThemeGoldTxt.Text = Loc.Tr("Золото", "Gold");
        WThemeConfettiTxt.Text = Loc.Tr("Конфетти", "Confetti");

        S3ParamsHdr.Text = Loc.Tr("ПАРАМЕТРЫ", "PARAMETERS");
        WDensityLbl.Text = Loc.Tr("Плотность снегопада", "Snowfall density");
        WSizeLbl.Text = Loc.Tr("Размер снежинок", "Snowflake size");
        WSpeedLbl.Text = Loc.Tr("Скорость падения", "Fall speed");
        WWindLbl.Text = Loc.Tr("Сила ветра", "Wind strength");

        S4Title.Text = Loc.Tr("Сугробы и физика", "Snowdrifts & Physics");
        S4Sub.Text = Loc.Tr(
            "Как снег копится и как реагирует на вас. Попробуйте потрогать его в предпросмотре.",
            "Configure how snow accumulates and reacts to your cursor. Try interacting with it in the preview.");
        S4DriftsHdr.Text = Loc.Tr("СУГРОБЫ", "SNOWDRIFTS");
        WHeightLbl.Text = Loc.Tr("Максимальная высота сугробов", "Max snowdrift height");
        WAccumLbl.Text = Loc.Tr("Скорость накопления", "Accumulation speed");
        WSwayLbl.Text = Loc.Tr("Покачивание хлопьев", "Snowflake sway");

        S4InterHdr.Text = Loc.Tr("ВЗАИМОДЕЙСТВИЕ", "INTERACTION");
        WCraftTitle.Text = Loc.Tr("Снежки и пятна на экране", "Snowballs & screen splatters");
        WCraftDesc.Text = Loc.Tr("Лепите снежки из сугробов; брошенные снежки оставляют следы.", "Roll snowballs from drifts; fast throws leave melting splatters.");
        WSnowmenTitle.Text = Loc.Tr("Живые снеговики", "Living snowmen");
        WSnowmenDesc.Text = Loc.Tr("Снежки превращаются в снеговиков.", "Stack snowballs into living snowmen.");
        WScatterTitle.Text = Loc.Tr("Разлёт снега от курсора", "Cursor snow scatter");
        WScatterDesc.Text = Loc.Tr("Курсор раздувает падающие хлопья.", "Mouse movement scatters falling snowflakes.");
        WScatterPowLbl.Text = Loc.Tr("Сила разлёта", "Scatter power");
        WGravityLbl.Text = Loc.Tr("Гравитация предметов", "Object gravity");
        WShakeTitle.Text = Loc.Tr("Стряхивание снега с окон", "Shed snow by shaking windows");
        WShakeDesc.Text = Loc.Tr("Встряхните окно – снег с него осыплется.", "Shake a window to make snowdrifts crumble down.");

        S5Title.Text = Loc.Tr("Поведение и система", "Behavior & System");
        S5Sub.Text = Loc.Tr(
            "Последний шаг. Эти параметры действуют на рабочем столе (в предпросмотре виден только снег на панели задач).",
            "Final step. These settings apply to your desktop (the preview shows taskbar snow accumulation).");
        WOcclTitle.Text = Loc.Tr("Снег ложится на окна", "Snow lands on windows");
        WOcclDesc.Text = Loc.Tr("Хлопья скрываются за окнами и копятся на их верхних краях.", "Snowflakes are occluded by foreground windows and pile up on top edges.");
        WTaskbarTitle.Text = Loc.Tr("Сугробы на панели задач", "Snowdrifts on taskbar");
        WTaskbarDesc.Text = Loc.Tr("Снег накапливается на панели задач Windows.", "Snow accumulates along the Windows taskbar.");
        WGamePauseTitle.Text = Loc.Tr("Автопауза в полноэкранных играх", "Auto-pause in fullscreen games");
        WGamePauseDesc.Text = Loc.Tr("Снег скрывается и не расходует ресурсы, пока вы играете или смотрите видео.", "Pauses simulation and hides overlay while playing fullscreen games or watching videos.");
        WPowerTitle.Text = Loc.Tr("Энергосбережение при простое", "Idle power saving");
        WPowerDesc.Text = Loc.Tr("Снижает нагрузку, когда вы долго не двигаете мышь.", "Reduces frame rate when the mouse is idle.");
        WAnimTitle.Text = Loc.Tr("Анимации интерфейса", "Interface animations");
        WAnimDesc.Text = Loc.Tr("Плавные переходы кнопок, переключателей и окон. Можно отключить в любой момент.", "Smooth transitions for buttons, toggles, and windows.");
        WAutoStartTitle.Text = Loc.Tr("Запускать вместе с Windows", "Launch with Windows");
        WAutoStartDesc.Text = Loc.Tr("Frosty будет стартовать в системном трее. Настройки всегда открываются кликом по иконке.", "Frosty will start in the system tray on boot.");

        PreviewBadgeText.Text = Loc.Tr("ПРЕДПРОСМОТР", "LIVE PREVIEW");
        PreviewHintText.Text = Loc.Tr(
            "Лепите снежки зажатой кнопкой мыши, водите курсором сквозь снег.",
            "Hold LMB on snowdrifts to roll snowballs, move cursor through snow.");
        ClearPreviewBtn.Content = Loc.Tr("Очистить", "Clear");

        _isSyncing = true;
        WelcomeLangToggle.IsChecked = Loc.IsEnglish;
        _isSyncing = false;

        UpdateNavigationUi();
    }

    private void OnWelcomeLanguageClicked(object sender, RoutedEventArgs e)
    {
        if (_isSyncing || !IsLoaded) return;
        bool isEn = WelcomeLangToggle.IsChecked == true;
        _settings.IsEnglishLanguage = isEn;
        _live.IsEnglishLanguage = isEn;
        Loc.SetLanguage(isEn);
    }

    private void SyncControlsFromSettings()
    {
        _isSyncing = true;

        WelcomeLangToggle.IsChecked = _settings.IsEnglishLanguage;

        WelcomeCraftingToggle.IsChecked = _settings.EnableSnowballCrafting;
        WelcomeSnowmenToggle.IsChecked = _settings.EnableLivingSnowmen;
        WelcomeScatterToggle.IsChecked = _settings.EnableCursorScatter;
        WelcomeShakeToggle.IsChecked = _settings.EnableWindowShakeShed;

        WelcomeOcclusionToggle.IsChecked = _settings.EnableWindowOcclusion;
        WelcomeTaskbarToggle.IsChecked = _settings.AccumulateOnTaskbar;
        WelcomeGamePauseToggle.IsChecked = _settings.PauseInFullscreenGames;
        WelcomePowerSaveToggle.IsChecked = _settings.EnablePowerSaving;
        WelcomeAnimationsToggle.IsChecked = _settings.EnableUiAnimations;
        WelcomeAutoStartToggle.IsChecked = _autoStart;

        SyncThemeRadios();
        SyncPresetRadios();
        SyncSliders();

        _isSyncing = false;
    }

    private void SyncPresetRadios()
    {
        SnowPreset? p = SnowPresets.Detect(_settings);
        PresetCozyRadio.IsChecked = p == SnowPreset.Cozy;
        PresetClassicRadio.IsChecked = p == SnowPreset.Classic;
        PresetHeavyRadio.IsChecked = p == SnowPreset.Heavy;
        PresetCyberRadio.IsChecked = p == SnowPreset.Cyber;
    }

    private void SyncThemeRadios()
    {
        switch (_settings.ColorTheme)
        {
            case SnowColorTheme.CyberpunkNeon: WelcomeThemeNeon.IsChecked = true; break;
            case SnowColorTheme.RoyalGold: WelcomeThemeGold.IsChecked = true; break;
            case SnowColorTheme.FestiveConfetti: WelcomeThemeConfetti.IsChecked = true; break;
            default: WelcomeThemeWhite.IsChecked = true; break;
        }
    }

    private void SyncSliders()
    {
        foreach (SliderBinding b in _sliders)
        {
            double v = b.Get(_settings);
            b.Slider.Value = Math.Clamp(v, b.Slider.Minimum, b.Slider.Maximum);
            b.Label.Text = FormatValue(b, v);
        }
    }

    private void ApplyToPreview()
    {
        Preview.ApplySettings(_settings);
    }

    private void OnSliderChanged(SliderBinding b, double value)
    {
        b.Label.Text = FormatValue(b, value);
        if (_isSyncing || !IsLoaded) return;

        b.Set(_settings, value);
        bool prev = _isSyncing;
        _isSyncing = true;
        SyncPresetRadios();
        _isSyncing = prev;
        ApplyToPreview();
    }

    private void OnPresetRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncing || !IsLoaded) return;

        SnowPreset? preset = null;
        if (ReferenceEquals(sender, PresetCozyRadio)) preset = SnowPreset.Cozy;
        else if (ReferenceEquals(sender, PresetClassicRadio)) preset = SnowPreset.Classic;
        else if (ReferenceEquals(sender, PresetHeavyRadio)) preset = SnowPreset.Heavy;
        else if (ReferenceEquals(sender, PresetCyberRadio)) preset = SnowPreset.Cyber;
        if (preset == null) return;

        SnowPresets.Apply(_settings, preset.Value);

        _isSyncing = true;
        SyncThemeRadios();
        SyncSliders();
        _isSyncing = false;

        ApplyToPreview();
    }

    private void OnWelcomeThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncing || !IsLoaded) return;

        if (WelcomeThemeNeon.IsChecked == true) _settings.ColorTheme = SnowColorTheme.CyberpunkNeon;
        else if (WelcomeThemeGold.IsChecked == true) _settings.ColorTheme = SnowColorTheme.RoyalGold;
        else if (WelcomeThemeConfetti.IsChecked == true) _settings.ColorTheme = SnowColorTheme.FestiveConfetti;
        else _settings.ColorTheme = SnowColorTheme.ClassicWhite;

        _isSyncing = true;
        SyncPresetRadios();
        _isSyncing = false;
        ApplyToPreview();
    }

    private void OnWelcomeFeatureToggled(object sender, RoutedEventArgs e)
    {
        if (_isSyncing || !IsLoaded) return;

        _settings.EnableSnowballCrafting = WelcomeCraftingToggle.IsChecked == true;
        _settings.EnableScreenSplatters = WelcomeCraftingToggle.IsChecked == true;
        _settings.EnableLivingSnowmen = WelcomeSnowmenToggle.IsChecked == true;
        _settings.EnableCursorScatter = WelcomeScatterToggle.IsChecked == true;
        _settings.EnableWindowShakeShed = WelcomeShakeToggle.IsChecked == true;

        _settings.EnableWindowOcclusion = WelcomeOcclusionToggle.IsChecked == true;
        _settings.AccumulateOnTaskbar = WelcomeTaskbarToggle.IsChecked == true;
        _settings.PauseInFullscreenGames = WelcomeGamePauseToggle.IsChecked == true;
        _settings.EnablePowerSaving = WelcomePowerSaveToggle.IsChecked == true;

        _settings.EnableUiAnimations = WelcomeAnimationsToggle.IsChecked == true;
        Motion.Enabled = _settings.EnableUiAnimations;

        ApplyToPreview();
    }

    private void OnWelcomeAutoStartClicked(object sender, RoutedEventArgs e)
    {
        if (_isSyncing || !IsLoaded) return;
        _autoStart = WelcomeAutoStartToggle.IsChecked == true;
    }

    private void OnClearPreviewClick(object sender, RoutedEventArgs e)
    {
        Preview.ClearSnow();
    }

    private void ShowStep(int step, int direction)
    {
        _currentStep = step;

        for (int i = 0; i < _stepPanels.Length; i++)
        {
            if (i != step)
            {
                _stepPanels[i].BeginAnimation(OpacityProperty, null);
                _stepPanels[i].Visibility = Visibility.Collapsed;
            }
        }

        FrameworkElement panel = _stepPanels[step];
        if (direction != 0)
        {
            Motion.Reveal(panel, fromX: 28 * direction, fromY: 0);
        }
        else
        {
            panel.Visibility = Visibility.Visible;
        }

        UpdateNavigationUi();
    }

    private void UpdateNavigationUi()
    {
        for (int i = 0; i < TotalSteps; i++)
        {
            bool active = i == _currentStep;
            double width = active ? 18 : 8;
            System.Windows.Media.Color color = active ? ActiveColor : InactiveColor;
            Rectangle dot = _dots[i];
            var brush = (SolidColorBrush)dot.Fill;

            if (Motion.Enabled && IsLoaded)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = new Duration(TimeSpan.FromMilliseconds(200));
                dot.BeginAnimation(WidthProperty, new DoubleAnimation(width, dur) { EasingFunction = ease });
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, dur) { EasingFunction = ease });
            }
            else
            {
                dot.BeginAnimation(WidthProperty, null);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                dot.Width = width;
                brush.Color = color;
            }
        }

        BackBtn.Visibility = _currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        BackBtn.Content = Loc.Tr("← Назад", "← Back");
        NextBtn.Content = _currentStep == TotalSteps - 1
            ? Loc.Tr("Готово ✓", "Done ✓")
            : Loc.Tr("Далее →", "Next →");

        string kind = _currentStep < FirstSettingsStep
            ? Loc.Tr("знакомство", "intro")
            : Loc.Tr("настройка", "setup");
        StepCounterText.Text = Loc.Tr(
            $"Шаг {_currentStep + 1} из {TotalSteps} · {kind}",
            $"Step {_currentStep + 1} of {TotalSteps} · {kind}");
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) ShowStep(_currentStep - 1, -1);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep < TotalSteps - 1) ShowStep(_currentStep + 1, +1);
        else CompleteAndClose();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CompleteAndClose()
    {
        Finish(apply: true);
        Close();
    }

    private void Finish(bool apply)
    {
        if (_finished) return;
        _finished = true;

        if (apply)
        {
            _live.CopyFrom(_settings);
            _live.IsFirstRunCompleted = true;
            if (_autoStart != _initialAutoStart)
            {
                AppSettings.SetAutoStart(_autoStart);
            }
            _live.Save();
        }
        else
        {
            _live.Save();
        }

        Motion.Enabled = _live.EnableUiAnimations;
        _onFinished(apply);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Finish(apply: _isFirstRun);
        base.OnClosing(e);
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}