using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Frosty;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SnowSimulationEngine _engine;
    private readonly Action _onSettingsChanged;
    private readonly Action? _onShowWelcome;
    private readonly DispatcherTimer _saveDebounceTimer;
    private bool _isInitializing = true;
    private bool _allowRealClose;
    private bool _akEasterEggActive;
    private int _akEasterEggClicks;
    private DateTime _lastAkClickTime;

    public SettingsWindow(AppSettings settings, SnowSimulationEngine engine, Action onSettingsChanged, Action? onShowWelcome = null)
    {
        InitializeComponent();
        _settings = settings;
        _engine = engine;
        _onSettingsChanged = onSettingsChanged;
        _onShowWelcome = onShowWelcome;

        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _saveDebounceTimer.Tick += (_, _) =>
        {
            _saveDebounceTimer.Stop();
            _settings.Save();
        };

        Icon = AppIcon.WpfIcon;

        Loc.LanguageChanged += ApplyLocalization;
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public void ForceClose()
    {
        Loc.LanguageChanged -= ApplyLocalization;
        _allowRealClose = true;
        Close();
    }

    public void RefreshFromSettings()
    {
        if (IsLoaded)
        {
            SyncUiFromSettings();
            ApplyLocalization();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryApplyWindows11Backdrop();
        SyncUiFromSettings();
        ApplyLocalization();
        Motion.PlayWindowEntrance(RootBorder);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && IsLoaded)
        {
            SyncUiFromSettings();
            ApplyLocalization();
            Motion.PlayWindowEntrance(RootBorder);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _saveDebounceTimer.Stop();
        _settings.Save();

        if (!_allowRealClose && System.Windows.Application.Current?.ShutdownMode != ShutdownMode.OnExplicitShutdown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
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
        Title = Loc.Tr("Настройки Frosty", "Frosty Settings");

        NavWeatherText.Text = Loc.Tr("Погода и физика", "Weather & Physics");
        NavInteractiveText.Text = Loc.Tr("Интерактив и объекты", "Interactive & Objects");
        NavSystemText.Text = Loc.Tr("Система", "System");

        SidebarLangLabel.Text = Loc.Tr("Язык:", "Language:");
        HotkeysHeaderText.Text = Loc.Tr("Горячие клавиши:", "Hotkeys:");
        HotkeyPauseText.Text = Loc.Tr("Ctrl + Alt + S – Пауза", "Ctrl + Alt + S – Pause");
        HotkeyClearText.Text = Loc.Tr("Ctrl + Alt + C – Очистка", "Ctrl + Alt + C – Clear");

        WeatherHeaderTitle.Text = Loc.Tr("Погода и физика снегопада", "Snowfall Weather & Physics");
        PresetsSectionTitle.Text = Loc.Tr("ГОТОВЫЕ ШАБЛОНЫ", "PRESETS");

        PresetCozyTitle.Text = Loc.Tr("Уютный", "Cozy");
        PresetCozyDesc.Text = Loc.Tr("Редкие, неспешные хлопья.", "Gentle, slow snowflakes.");
        PresetClassicTitle.Text = Loc.Tr("Классика", "Classic");
        PresetClassicDesc.Text = Loc.Tr("Сбалансированный вариант.", "Balanced everyday snowfall.");
        PresetHeavyTitle.Text = Loc.Tr("Снегопад", "Heavy Snow");
        PresetHeavyDesc.Text = Loc.Tr("Плотный, но спокойный снег.", "Dense yet calm snowfall.");
        PresetCyberTitle.Text = Loc.Tr("Киберпанк", "Cyberpunk");
        PresetCyberDesc.Text = Loc.Tr("Неоновые хлопья.", "Glowing neon snowflakes.");

        ParticleCountTitle.Text = Loc.Tr("Количество снежинок", "Snowflake count");
        SizeTitle.Text = Loc.Tr("Размер снежинок", "Snowflake size");
        SpeedTitle.Text = Loc.Tr("Скорость падения", "Fall speed");
        WindTitle.Text = Loc.Tr("Сила ветра", "Wind strength");
        WindIntervalTitle.Text = Loc.Tr("Частота смены порывов ветра", "Wind gust interval");
        SwayTitle.Text = Loc.Tr("Амплитуда покачивания снежинок", "Snowflake sway amplitude");
        SnowHeightTitle.Text = Loc.Tr("Максимальная высота сугробов", "Max snowdrift height");
        AccumRateTitle.Text = Loc.Tr("Скорость накопления сугробов", "Snow accumulation speed");

        BlizzardTitleText.Text = Loc.Tr("Режим «Метель / Вьюга»", "Blizzard Mode");
        BlizzardDescText.Text = Loc.Tr("Порывистый штормовой ветер и наклонная траектория снега", "Gusty storm wind and angled snowfall trajectory");

        ParticleStyleTitle.Text = Loc.Tr("Тип частиц и сугробов", "Particle & Pile Type");
        StyleSnowText.Text = Loc.Tr("Снег", "Snow");
        StyleAutumnText.Text = Loc.Tr("Осень (листья)", "Autumn (Leaves)");
        StyleSakuraText.Text = Loc.Tr("Сакура (лепестки)", "Sakura (Petals)");
        StyleEmbersText.Text = Loc.Tr("Искры", "Embers");

        ThemePaletteTitle.Text = Loc.Tr("Цветовая гамма снега", "Snow Color Gamma");
        ThemeWhiteText.Text = Loc.Tr("Классика", "Classic");
        ThemeNeonText.Text = Loc.Tr("Киберпанк", "Cyberpunk");
        ThemeGoldText.Text = Loc.Tr("Золото", "Royal Gold");
        ThemeConfettiText.Text = Loc.Tr("Конфетти", "Confetti");

        InteractiveHeaderTitle.Text = Loc.Tr("Интерактив и физика объектов", "Interactive & Object Physics");
        CraftingTitleText.Text = Loc.Tr("Лепка снежков и снеговиков (ЛКМ / ПКМ)", "Craft snowballs & snowmen (LMB / RMB)");
        CraftingDescText.Text = Loc.Tr("ЛКМ по сугробу – скатать ком, соединить 2–3 кома – снеговик. ПКМ – разбить объект в снежинки.", "LMB on a drift to roll a snowball, stack 2–3 balls to build a snowman. RMB to shatter.");

        UpdateLivingSnowmenTitle();
        LivingSnowmenDescText.Text = Loc.Tr("Если слепить 2 или более снеговиков, они начинают перекидываться снежками друг в друга", "When 2 or more snowmen are built, they start a snowball fight with each other");

        WindowShakeTitleText.Text = Loc.Tr("Стряхивание сугробов при резкой тряске окна", "Shed snowdrifts by shaking windows");
        WindowShakeDescText.Text = Loc.Tr("Если резко потрясти окно из стороны в сторону, сугроб сорвется лавиной", "Shake a window rapidly side to side to trigger a snow avalanche");
        ShakeThresholdTitle.Text = Loc.Tr("Сила тряски, нужная чтобы стряхнуть снег с окна", "Window shake force required to shed snow");
        ShakeThresholdHintText.Text = Loc.Tr("Меньше – снег слетает от лёгкого встряхивания, больше – окно нужно трясти резко и сильно", "Lower – snow falls off with a gentle shake; higher – requires a strong shake");

        ScreenEffectsTitleText.Text = Loc.Tr("Реалистичные следы от снежков и снеговиков на экране", "Realistic snowball splatters on screen");
        ScreenEffectsDescText.Text = Loc.Tr("Сильный удар снежка или снеговика оставляет реалистичную тающую кляксу со стекающими каплями", "Hard throws leave a melting snow splatter with dripping droplets on screen");

        CursorScatterTitleText.Text = Loc.Tr("Реакция снега и сугробов на движение мыши", "Snow & drift reaction to mouse movement");
        CursorScatterDescText.Text = Loc.Tr("Быстрое движение курсора развеивает падающие снежинки и сдувает верхушки сугробов", "Fast cursor movement scatters falling snowflakes and blows snow off drift tops");

        CursorPowerTitle.Text = Loc.Tr("Радиус и сила сдувания снега курсором", "Cursor scatter radius & power");
        GravityTitle.Text = Loc.Tr("Гравитация снежков и снеговиков", "Snowball & snowman gravity");
        ToughnessTitle.Text = Loc.Tr("Прочность снежков при ударах о стены и окна", "Snowball toughness on impact");

        OcclusionTitleText.Text = Loc.Tr("Верхние окна закрывают нижние от падающего снега", "Top windows shield lower windows from snow");
        OcclusionDescText.Text = Loc.Tr("Верхние окна работают как крыша и закрывают нижние от снега", "Foreground windows act as a roof and block snow from landing on windows behind them");

        KeepHiddenSnowTitleText.Text = Loc.Tr("Сохранять сугробы при сворачивании окон", "Keep snowdrifts when windows are minimized");
        KeepHiddenSnowDescText.Text = Loc.Tr("Если свернуть или скрыть окно, снег и снеговики вернутся на него при разворачивании", "Minimized windows retain their snowdrifts and snowmen when restored");

        TaskbarSnowTitleText.Text = Loc.Tr("Накапливать снег на панели задач Windows", "Accumulate snow on Windows taskbar");
        TaskbarSnowDescText.Text = Loc.Tr("Формирует сугробы вдоль нижней панели задач экрана", "Forms snowdrifts along the bottom taskbar");

        FullscreenPauseTitleText.Text = Loc.Tr("Автопауза в полноэкранных играх и видео", "Auto-pause in fullscreen games & videos");
        FullscreenPauseDescText.Text = Loc.Tr("Скрывает оверлей и останавливает симуляцию при запуске полноэкранных игр", "Hides overlay and pauses simulation when fullscreen games or videos are running");

        SystemHeaderTitle.Text = Loc.Tr("Системные параметры и управление", "System Settings & Controls");

        AutoUpdateTitleText.Text = Loc.Tr("Автообновление через GitHub", "Auto-update via GitHub");
        AutoUpdateDescText.Text = Loc.Tr("Проверять наличие новых релизов при запуске приложения", "Automatically check for new GitHub releases on startup");
        UpdateStatusText.Text = Loc.Tr($"Текущая версия: v{AppConfig.CurrentVersion}", $"Current version: v{AppConfig.CurrentVersion}");
        CheckUpdateBtnText.Text = Loc.Tr("Проверить обновления", "Check for updates");

        AutoStartTitleText.Text = Loc.Tr("Запускать автоматически вместе с Windows", "Launch automatically with Windows");
        AutoStartDescText.Text = Loc.Tr("Добавляет приложение в автозагрузку текущего пользователя Windows", "Adds Frosty to current Windows user startup");

        UiAnimationsTitleText.Text = Loc.Tr("Анимации интерфейса", "Interface animations");
        UiAnimationsDescText.Text = Loc.Tr("Плавные переходы кнопок, переключателей, вкладок и окон. Отключите, если нужен мгновенный отклик", "Smooth transitions for buttons, toggles, tabs, and windows. Disable for instant response");

        PowerSavingTitleText.Text = Loc.Tr("Умный режим энергосбережения (Adaptive FPS)", "Smart power saving mode (Adaptive FPS)");
        PowerSavingDescText.Text = Loc.Tr("Плавные 60 FPS во время активности и снижение до 15 FPS при простое", "Smooth 60 FPS while active, drops to 15 FPS when idle");
        PowerTimeoutTitle.Text = Loc.Tr("Таймер перехода в режим сна", "Idle sleep timeout");

        DisplayPerfTitleText.Text = Loc.Tr("Мониторы и лимит FPS", "Monitors & FPS Limit");
        FpsLimitTitleText.Text = Loc.Tr("Ограничение частоты кадров (для 144/240 Гц мониторов)", "Frame rate limit (for 144/240 Hz monitors)");
        FpsNativeText.Text = Loc.Tr("Нативный (Hz)", "Native (Hz)");
        MonitorModeTitleText.Text = Loc.Tr("Область отображения мониторов", "Monitor display mode");
        MonitorAllText.Text = Loc.Tr("Все мониторы", "All monitors");
        MonitorPrimaryText.Text = Loc.Tr("Только основной", "Primary only");
        MonitorSecondaryText.Text = Loc.Tr("Только второй", "Secondary only");
        ProcessRulesTitleText.Text = Loc.Tr("Списки процессов для автопаузы", "Auto-pause process lists");
        WhitelistTitleText.Text = Loc.Tr("Исключения (не ставить на паузу в fullscreen, через запятую):", "Whitelist (never pause in fullscreen, comma-separated):");
        BlacklistTitleText.Text = Loc.Tr("Всегда ставить на паузу (даже в оконном режиме, через запятую):", "Force pause (even in windowed mode, comma-separated):");

        HotkeysCardTitleText.Text = Loc.Tr("Глобальные горячие клавиши", "Global hotkeys");
        HotkeysCardDescText.Text = Loc.Tr("Ctrl + Alt + S (пауза/возобновление) и Ctrl + Alt + C (очистить весь снег)", "Ctrl + Alt + S (pause/resume) and Ctrl + Alt + C (clear all snow)");

        ClearAllSnowBtnText.Text = Loc.Tr("Очистить весь снег", "Clear all snow");
        ClearSnowmenBtnText.Text = Loc.Tr("Удалить снеговиков", "Remove snowmen");
        ResetDefaultsBtnText.Text = Loc.Tr("Сбросить настройки", "Reset defaults");
        ShowWelcomeBtnText.Text = Loc.Tr("Показать обучение (мастер первого запуска)", "Show tutorial (first-run wizard)");

        UpdateSliderLabels();
    }

    private void UpdateLivingSnowmenTitle()
    {
        if (LivingSnowmenTitleText == null) return;
        if (_akEasterEggActive)
        {
            LivingSnowmenTitleText.Text = Loc.Tr("Живые снеговики [AK-47 MODE 🔥]", "Living snowmen [AK-47 MODE 🔥]");
        }
        else
        {
            LivingSnowmenTitleText.Text = Loc.Tr("Живые снеговики (битва снежками)", "Living snowmen (snowball fight)");
        }
    }

    private void SyncUiFromSettings()
    {
        _isInitializing = true;

        ParticleCountSlider.Value = _settings.ParticleCount;
        SizeSlider.Value = _settings.SizeMultiplier;
        SpeedSlider.Value = _settings.SpeedMultiplier;
        WindSlider.Value = _settings.WindMultiplier;
        WindIntervalSlider.Value = _settings.WindChangeIntervalSeconds;
        SwaySlider.Value = _settings.SwayAmplitudeMultiplier;
        SnowHeightSlider.Value = _settings.MaxSnowHeight;
        AccumRateSlider.Value = _settings.AccumulationRateMultiplier;

        CursorPowerSlider.Value = _settings.CursorScatterPower;
        GravitySlider.Value = _settings.ObjectGravityMultiplier;
        ToughnessSlider.Value = _settings.SnowballToughnessMultiplier;
        ShakeThresholdSlider.Value = _settings.WindowShakeThreshold;
        PowerTimeoutSlider.Value = _settings.PowerSaveTimeoutSeconds;

        UpdateSliderLabels();

        BlizzardToggle.IsChecked = _settings.IsBlizzardMode;
        CraftingToggle.IsChecked = _settings.EnableSnowballCrafting;
        LivingSnowmenToggle.IsChecked = _settings.EnableLivingSnowmen;
        WindowShakeToggle.IsChecked = _settings.EnableWindowShakeShed;
        ScreenEffectsToggle.IsChecked = _settings.EnableScreenSplatters;
        CursorScatterToggle.IsChecked = _settings.EnableCursorScatter;
        OcclusionToggle.IsChecked = _settings.EnableWindowOcclusion;
        KeepHiddenSnowToggle.IsChecked = _settings.KeepSnowOnHiddenWindows;
        TaskbarSnowToggle.IsChecked = _settings.AccumulateOnTaskbar;
        FullscreenPauseToggle.IsChecked = _settings.PauseInFullscreenGames;
        PowerSavingToggle.IsChecked = _settings.EnablePowerSaving;
        UiAnimationsToggle.IsChecked = _settings.EnableUiAnimations;
        HotkeysToggle.IsChecked = _settings.EnableGlobalHotkeys;
        LanguageToggle.IsChecked = _settings.IsEnglishLanguage;
        AutoUpdateToggle.IsChecked = _settings.EnableAutoUpdate;
        AutoStartToggle.IsChecked = AppSettings.IsAutoStartEnabled();

        switch (_settings.ParticleStyle)
        {
            case ParticleStyle.AutumnLeaves: StyleAutumnRadio.IsChecked = true; break;
            case ParticleStyle.SakuraPetals: StyleSakuraRadio.IsChecked = true; break;
            case ParticleStyle.Embers: StyleEmbersRadio.IsChecked = true; break;
            default: StyleSnowRadio.IsChecked = true; break;
        }

        switch (_settings.ColorTheme)
        {
            case SnowColorTheme.CyberpunkNeon: ThemeNeonRadio.IsChecked = true; break;
            case SnowColorTheme.RoyalGold: ThemeGoldRadio.IsChecked = true; break;
            case SnowColorTheme.FestiveConfetti: ThemeConfettiRadio.IsChecked = true; break;
            default: ThemeWhiteRadio.IsChecked = true; break;
        }
        ColorGammaCard.Opacity = _settings.ParticleStyle == ParticleStyle.Snow ? 1.0 : 0.45;

        if (_settings.TargetFps == 30) Fps30Radio.IsChecked = true;
        else if (_settings.TargetFps == 0) FpsNativeRadio.IsChecked = true;
        else Fps60Radio.IsChecked = true;

        switch (_settings.MonitorMode)
        {
            case MonitorMode.PrimaryOnly: MonitorPrimaryRadio.IsChecked = true; break;
            case MonitorMode.SecondaryOnly: MonitorSecondaryRadio.IsChecked = true; break;
            default: MonitorAllRadio.IsChecked = true; break;
        }

        WhitelistTextBox.Text = _settings.FullscreenWhitelist;
        BlacklistTextBox.Text = _settings.ForcePauseProcesses;

        UpdatePresetRadios();

        _isInitializing = false;
    }

    private bool _isPresetSync;

    private void UpdatePresetRadios()
    {
        _isPresetSync = true;
        SnowPreset? p = SnowPresets.Detect(_settings);
        PresetCozyRadio.IsChecked = p == SnowPreset.Cozy;
        PresetClassicRadio.IsChecked = p == SnowPreset.Classic;
        PresetHeavyRadio.IsChecked = p == SnowPreset.Heavy;
        PresetCyberRadio.IsChecked = p == SnowPreset.Cyber;
        _isPresetSync = false;
    }

    private void OnPresetChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isPresetSync) return;

        SnowPreset? preset = null;
        if (ReferenceEquals(sender, PresetCozyRadio)) preset = SnowPreset.Cozy;
        else if (ReferenceEquals(sender, PresetClassicRadio)) preset = SnowPreset.Classic;
        else if (ReferenceEquals(sender, PresetHeavyRadio)) preset = SnowPreset.Heavy;
        else if (ReferenceEquals(sender, PresetCyberRadio)) preset = SnowPreset.Cyber;
        if (preset == null) return;

        SnowPresets.Apply(_settings, preset.Value);
        _settings.ApplyToEngine(_engine);
        SyncUiFromSettings();

        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void UpdateSliderLabels()
    {
        string secUnit = Loc.Tr("сек", "s");
        if (ParticleCountLabel != null) ParticleCountLabel.Text = ((int)Math.Round(ParticleCountSlider.Value)).ToString();
        if (SizeLabel != null) SizeLabel.Text = $"{SizeSlider.Value:F1}x";
        if (SpeedLabel != null) SpeedLabel.Text = $"{SpeedSlider.Value:F1}x";
        if (WindLabel != null) WindLabel.Text = $"{WindSlider.Value:F1}x";
        if (WindIntervalLabel != null) WindIntervalLabel.Text = $"{WindIntervalSlider.Value:F1} {secUnit}";
        if (SwayLabel != null) SwayLabel.Text = $"{SwaySlider.Value:F1}x";
        if (SnowHeightLabel != null) SnowHeightLabel.Text = $"{(int)Math.Round(SnowHeightSlider.Value)} px";
        if (AccumRateLabel != null) AccumRateLabel.Text = $"{AccumRateSlider.Value:F1}x";

        if (CursorPowerLabel != null) CursorPowerLabel.Text = $"{CursorPowerSlider.Value:F1}x";
        if (GravityLabel != null) GravityLabel.Text = $"{GravitySlider.Value:F1}x";

        if (ShakeThresholdLabel != null)
        {
            double s = ShakeThresholdSlider.Value;
            string desc = s < 0.7
                ? Loc.Tr("Лёгкая", "Light")
                : (s > 1.8 ? Loc.Tr("Очень сильная", "Very strong") : (s > 1.3 ? Loc.Tr("Сильная", "Strong") : Loc.Tr("Обычная", "Normal")));
            ShakeThresholdLabel.Text = $"{desc} ({s:F1}x)";
        }

        if (ToughnessLabel != null)
        {
            double t = ToughnessSlider.Value;
            string desc = t < 0.75
                ? Loc.Tr("Хрупкие", "Fragile")
                : (t > 2.2 ? Loc.Tr("Неразбиваемые", "Unbreakable") : Loc.Tr("Обычные", "Normal"));
            ToughnessLabel.Text = $"{desc} ({t:F1}x)";
        }

        if (PowerTimeoutLabel != null) PowerTimeoutLabel.Text = $"{(int)Math.Round(PowerTimeoutSlider.Value)} {secUnit}";
    }

    private void OnNavTabChecked(object sender, RoutedEventArgs e)
    {
        if (WeatherPanel == null || InteractivePanel == null || SystemPanel == null) return;

        ShowTabPanel(WeatherPanel, TabWeatherBtn.IsChecked == true);
        ShowTabPanel(InteractivePanel, TabInteractiveBtn.IsChecked == true);
        ShowTabPanel(SystemPanel, TabSystemBtn.IsChecked == true);
    }

    private static void ShowTabPanel(FrameworkElement panel, bool show)
    {
        if (!show)
        {
            panel.Visibility = Visibility.Collapsed;
        }
        else if (panel.Visibility != Visibility.Visible)
        {
            Motion.Reveal(panel, fromX: 0, fromY: 10);
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplySliderChange(sender);
        if (!_isInitializing) UpdatePresetRadios();
    }

    private void ApplySliderChange(object sender)
    {
        if (_isInitializing) return;

        if (ReferenceEquals(sender, ParticleCountSlider))
        {
            _settings.ParticleCount = (int)Math.Round(ParticleCountSlider.Value);
            _engine.SetActiveParticleCount(_settings.ParticleCount);
        }
        else if (ReferenceEquals(sender, SizeSlider))
        {
            _settings.SizeMultiplier = (float)Math.Round(SizeSlider.Value, 1);
            _engine.SetSizeMultiplier(_settings.SizeMultiplier);
        }
        else if (ReferenceEquals(sender, SpeedSlider))
        {
            _settings.SpeedMultiplier = (float)Math.Round(SpeedSlider.Value, 1);
            _engine.SetSpeedMultiplier(_settings.SpeedMultiplier);
        }
        else if (ReferenceEquals(sender, WindSlider))
        {
            _settings.WindMultiplier = (float)Math.Round(WindSlider.Value, 1);
            _engine.SetWindMultiplier(_settings.WindMultiplier);
        }
        else if (ReferenceEquals(sender, WindIntervalSlider))
        {
            _settings.WindChangeIntervalSeconds = (float)Math.Round(WindIntervalSlider.Value, 1);
            _engine.SetWindChangeInterval(_settings.WindChangeIntervalSeconds);
        }
        else if (ReferenceEquals(sender, SwaySlider))
        {
            _settings.SwayAmplitudeMultiplier = (float)Math.Round(SwaySlider.Value, 1);
            _engine.SetSwayAmplitudeMultiplier(_settings.SwayAmplitudeMultiplier);
        }
        else if (ReferenceEquals(sender, SnowHeightSlider))
        {
            _settings.MaxSnowHeight = (float)Math.Round(SnowHeightSlider.Value);
            _engine.SetMaxSnowHeight(_settings.MaxSnowHeight);
        }
        else if (ReferenceEquals(sender, AccumRateSlider))
        {
            _settings.AccumulationRateMultiplier = (float)Math.Round(AccumRateSlider.Value, 1);
            _engine.SetAccumulationRateMultiplier(_settings.AccumulationRateMultiplier);
        }
        else if (ReferenceEquals(sender, CursorPowerSlider))
        {
            _settings.CursorScatterPower = (float)Math.Round(CursorPowerSlider.Value, 1);
            _engine.SetCursorScatterPower(_settings.CursorScatterPower);
        }
        else if (ReferenceEquals(sender, GravitySlider))
        {
            _settings.ObjectGravityMultiplier = (float)Math.Round(GravitySlider.Value, 1);
            _engine.SetObjectGravityMultiplier(_settings.ObjectGravityMultiplier);
        }
        else if (ReferenceEquals(sender, ToughnessSlider))
        {
            _settings.SnowballToughnessMultiplier = (float)Math.Round(ToughnessSlider.Value, 1);
            _engine.SetSnowballToughnessMultiplier(_settings.SnowballToughnessMultiplier);
        }
        else if (ReferenceEquals(sender, ShakeThresholdSlider))
        {
            _settings.WindowShakeThreshold = (float)Math.Round(ShakeThresholdSlider.Value, 1);
            _engine.SetWindowShakeThreshold(_settings.WindowShakeThreshold);
        }
        else if (ReferenceEquals(sender, PowerTimeoutSlider))
        {
            _settings.PowerSaveTimeoutSeconds = (float)Math.Round(PowerTimeoutSlider.Value);
            _engine.SetPowerSaveTimeoutSeconds(_settings.PowerSaveTimeoutSeconds);
        }

        UpdateSliderLabels();

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        ApplyToggleChange(sender);
        if (!_isInitializing) UpdatePresetRadios();
    }

    private void ApplyToggleChange(object sender)
    {
        if (_isInitializing) return;

        if (ReferenceEquals(sender, BlizzardToggle))
        {
            _settings.IsBlizzardMode = BlizzardToggle.IsChecked == true;
            _engine.SetBlizzardMode(_settings.IsBlizzardMode);
        }
        else if (ReferenceEquals(sender, CraftingToggle))
        {
            _settings.EnableSnowballCrafting = CraftingToggle.IsChecked == true;
            _engine.SetSnowballCrafting(_settings.EnableSnowballCrafting);
        }
        else if (ReferenceEquals(sender, LivingSnowmenToggle))
        {
            _settings.EnableLivingSnowmen = LivingSnowmenToggle.IsChecked == true;
            _engine.SetLivingSnowmen(_settings.EnableLivingSnowmen);
        }
        else if (ReferenceEquals(sender, WindowShakeToggle))
        {
            _settings.EnableWindowShakeShed = WindowShakeToggle.IsChecked == true;
            _engine.SetWindowShakeShed(_settings.EnableWindowShakeShed);
        }
        else if (ReferenceEquals(sender, ScreenEffectsToggle))
        {
            bool enabled = ScreenEffectsToggle.IsChecked == true;
            _settings.EnableScreenSplatters = enabled;
            _engine.SetScreenSplatters(enabled);
        }
        else if (ReferenceEquals(sender, CursorScatterToggle))
        {
            _settings.EnableCursorScatter = CursorScatterToggle.IsChecked == true;
            _engine.SetCursorScatter(_settings.EnableCursorScatter);
        }
        else if (ReferenceEquals(sender, OcclusionToggle))
        {
            _settings.EnableWindowOcclusion = OcclusionToggle.IsChecked == true;
            _engine.SetWindowOcclusion(_settings.EnableWindowOcclusion);
        }
        else if (ReferenceEquals(sender, KeepHiddenSnowToggle))
        {
            _settings.KeepSnowOnHiddenWindows = KeepHiddenSnowToggle.IsChecked == true;
            _engine.SetKeepSnowOnHiddenWindows(_settings.KeepSnowOnHiddenWindows);
        }
        else if (ReferenceEquals(sender, TaskbarSnowToggle))
        {
            _settings.AccumulateOnTaskbar = TaskbarSnowToggle.IsChecked == true;
            _engine.SetAccumulateOnTaskbar(_settings.AccumulateOnTaskbar);
        }
        else if (ReferenceEquals(sender, FullscreenPauseToggle))
        {
            _settings.PauseInFullscreenGames = FullscreenPauseToggle.IsChecked == true;
        }
        else if (ReferenceEquals(sender, PowerSavingToggle))
        {
            _settings.EnablePowerSaving = PowerSavingToggle.IsChecked == true;
            _engine.SetPowerSaving(_settings.EnablePowerSaving);
        }
        else if (ReferenceEquals(sender, UiAnimationsToggle))
        {
            _settings.EnableUiAnimations = UiAnimationsToggle.IsChecked == true;
            Motion.Enabled = _settings.EnableUiAnimations;
        }
        else if (ReferenceEquals(sender, HotkeysToggle))
        {
            _settings.EnableGlobalHotkeys = HotkeysToggle.IsChecked == true;
        }
        else if (ReferenceEquals(sender, LanguageToggle))
        {
            _settings.IsEnglishLanguage = LanguageToggle.IsChecked == true;
            Loc.SetLanguage(_settings.IsEnglishLanguage);
        }
        else if (ReferenceEquals(sender, AutoUpdateToggle))
        {
            _settings.EnableAutoUpdate = AutoUpdateToggle.IsChecked == true;
        }

        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void OnThemeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        ApplyThemeChange();
        UpdatePresetRadios();
    }

    private void OnParticleStyleChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (StyleAutumnRadio.IsChecked == true) _settings.ParticleStyle = ParticleStyle.AutumnLeaves;
        else if (StyleSakuraRadio.IsChecked == true) _settings.ParticleStyle = ParticleStyle.SakuraPetals;
        else if (StyleEmbersRadio.IsChecked == true) _settings.ParticleStyle = ParticleStyle.Embers;
        else _settings.ParticleStyle = ParticleStyle.Snow;

        ColorGammaCard.Opacity = _settings.ParticleStyle == ParticleStyle.Snow ? 1.0 : 0.45;
        _engine.SetParticleStyle(_settings.ParticleStyle);
        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void ApplyThemeChange()
    {
        if (ThemeNeonRadio.IsChecked == true) _settings.ColorTheme = SnowColorTheme.CyberpunkNeon;
        else if (ThemeGoldRadio.IsChecked == true) _settings.ColorTheme = SnowColorTheme.RoyalGold;
        else if (ThemeConfettiRadio.IsChecked == true) _settings.ColorTheme = SnowColorTheme.FestiveConfetti;
        else _settings.ColorTheme = SnowColorTheme.ClassicWhite;

        _engine.SetColorTheme(_settings.ColorTheme);
        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void OnFpsRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (Fps30Radio.IsChecked == true) _settings.TargetFps = 30;
        else if (FpsNativeRadio.IsChecked == true) _settings.TargetFps = 0;
        else _settings.TargetFps = 60;

        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void OnMonitorRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (MonitorPrimaryRadio.IsChecked == true) _settings.MonitorMode = MonitorMode.PrimaryOnly;
        else if (MonitorSecondaryRadio.IsChecked == true) _settings.MonitorMode = MonitorMode.SecondaryOnly;
        else _settings.MonitorMode = MonitorMode.AllMonitors;

        _saveDebounceTimer.Stop();
        _settings.Save();
        _onSettingsChanged();
    }

    private void OnProcessListTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.FullscreenWhitelist = WhitelistTextBox.Text ?? string.Empty;
        _settings.ForcePauseProcesses = BlacklistTextBox.Text ?? string.Empty;
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnAutoStartToggleClicked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        AppSettings.SetAutoStart(AutoStartToggle.IsChecked == true);
    }

    private void OnGitHubClick(object sender, RoutedEventArgs e)
    {
        AppConfig.OpenUrl(AppConfig.GitHubUrl);
    }

    private void OnTelegramClick(object sender, RoutedEventArgs e)
    {
        AppConfig.OpenUrl(AppConfig.TelegramUrl);
    }

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = Loc.Tr("Проверка обновлений...", "Checking for updates...");

        UpdateInfo? info = await AutoUpdater.CheckForUpdatesAsync();
        if (info == null)
        {
            UpdateStatusText.Text = Loc.Tr(
                $"У вас последняя версия (v{AppConfig.CurrentVersion})",
                $"You have the latest version (v{AppConfig.CurrentVersion})");
            CheckUpdateBtn.IsEnabled = true;
            return;
        }

        UpdateStatusText.Text = Loc.Tr(
            $"Доступна новая версия: {info.TagVersion}",
            $"New version available: {info.TagVersion}");

        var res = System.Windows.MessageBox.Show(
            this,
            Loc.Tr(
                $"Найдена новая версия Frosty ({info.TagVersion}).\nСкачать и установить обновление сейчас?",
                $"A new version of Frosty ({info.TagVersion}) is available.\nDownload and install the update now?"),
            "Frosty Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (res == MessageBoxResult.Yes)
        {
            UpdateStatusText.Text = Loc.Tr("Скачивание обновления...", "Downloading update...");
            await AutoUpdater.DownloadAndApplyUpdateAsync(info);
        }

        CheckUpdateBtn.IsEnabled = true;
    }

    private void OnClearAllSnowClick(object sender, RoutedEventArgs e)
    {
        _engine.ClearAllSnow();
    }

    private void OnClearSnowmenOnlyClick(object sender, RoutedEventArgs e)
    {
        _engine.ClearSnowmenAndSnowballs();
    }

    private void OnShowWelcomeClick(object sender, RoutedEventArgs e)
    {
        _onShowWelcome?.Invoke();
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        bool keepLang = _settings.IsEnglishLanguage;
        var defaults = new AppSettings { IsFirstRunCompleted = _settings.IsFirstRunCompleted, IsEnglishLanguage = keepLang };
        _settings.CopyFrom(defaults);
        Motion.Enabled = _settings.EnableUiAnimations;

        SyncUiFromSettings();
        _saveDebounceTimer.Stop();
        _settings.ApplyToEngine(_engine);
        _settings.Save();
        _onSettingsChanged();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnLivingSnowmenCardClick(object sender, MouseButtonEventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastAkClickTime).TotalSeconds > 1.5)
        {
            _akEasterEggClicks = 0;
        }
        _lastAkClickTime = now;
        _akEasterEggClicks++;

        if (_akEasterEggClicks >= 5)
        {
            _akEasterEggClicks = 0;
            _akEasterEggActive = _engine.ToggleAk47EasterEgg();
            UpdateLivingSnowmenTitle();
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        _saveDebounceTimer.Stop();
        _settings.Save();
        Hide();
    }
}