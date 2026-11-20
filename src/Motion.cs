using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Frosty;

             
                                                                                                     
                                                                                                      
                                         
                                                                                         
                                                                               
              
public static class Motion
{
    public static bool Enabled { get; set; } = true;

    private static readonly Duration ColorDuration = new(TimeSpan.FromMilliseconds(150));
    private static readonly Duration ScaleDuration = new(TimeSpan.FromMilliseconds(120));
    private static readonly Duration ThumbDuration = new(TimeSpan.FromMilliseconds(190));

                                                                                     

    public static readonly DependencyProperty HoverBackgroundProperty = Reg<Brush?>("HoverBackground");
    public static readonly DependencyProperty PressedBackgroundProperty = Reg<Brush?>("PressedBackground");
    public static readonly DependencyProperty CheckedBackgroundProperty = Reg<Brush?>("CheckedBackground");
    public static readonly DependencyProperty HoverBorderProperty = Reg<Brush?>("HoverBorder");
    public static readonly DependencyProperty PressedBorderProperty = Reg<Brush?>("PressedBorder");
    public static readonly DependencyProperty CheckedBorderProperty = Reg<Brush?>("CheckedBorder");
    public static readonly DependencyProperty FocusBorderProperty = Reg<Brush?>("FocusBorder");

    public static readonly DependencyProperty HoverScaleProperty = RegD("HoverScale", 1.0);
    public static readonly DependencyProperty PressedScaleProperty = RegD("PressedScale", 1.0);
                                                                                                                  
    public static readonly DependencyProperty CheckedOffsetXProperty = RegD("CheckedOffsetX", 0.0);

    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);
    public static Brush? GetPressedBackground(DependencyObject o) => (Brush?)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush? v) => o.SetValue(PressedBackgroundProperty, v);
    public static Brush? GetCheckedBackground(DependencyObject o) => (Brush?)o.GetValue(CheckedBackgroundProperty);
    public static void SetCheckedBackground(DependencyObject o, Brush? v) => o.SetValue(CheckedBackgroundProperty, v);
    public static Brush? GetHoverBorder(DependencyObject o) => (Brush?)o.GetValue(HoverBorderProperty);
    public static void SetHoverBorder(DependencyObject o, Brush? v) => o.SetValue(HoverBorderProperty, v);
    public static Brush? GetPressedBorder(DependencyObject o) => (Brush?)o.GetValue(PressedBorderProperty);
    public static void SetPressedBorder(DependencyObject o, Brush? v) => o.SetValue(PressedBorderProperty, v);
    public static Brush? GetCheckedBorder(DependencyObject o) => (Brush?)o.GetValue(CheckedBorderProperty);
    public static void SetCheckedBorder(DependencyObject o, Brush? v) => o.SetValue(CheckedBorderProperty, v);
    public static Brush? GetFocusBorder(DependencyObject o) => (Brush?)o.GetValue(FocusBorderProperty);
    public static void SetFocusBorder(DependencyObject o, Brush? v) => o.SetValue(FocusBorderProperty, v);
    public static double GetHoverScale(DependencyObject o) => (double)o.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject o, double v) => o.SetValue(HoverScaleProperty, v);
    public static double GetPressedScale(DependencyObject o) => (double)o.GetValue(PressedScaleProperty);
    public static void SetPressedScale(DependencyObject o, double v) => o.SetValue(PressedScaleProperty, v);
    public static double GetCheckedOffsetX(DependencyObject o) => (double)o.GetValue(CheckedOffsetXProperty);
    public static void SetCheckedOffsetX(DependencyObject o, double v) => o.SetValue(CheckedOffsetXProperty, v);

    private static DependencyProperty Reg<T>(string name) =>
        DependencyProperty.RegisterAttached(name, typeof(T), typeof(Motion),
            new PropertyMetadata(null, OnStateAttached));

    private static DependencyProperty RegD(string name, double def) =>
        DependencyProperty.RegisterAttached(name, typeof(double), typeof(Motion),
            new PropertyMetadata(def, OnStateAttached));

                                                                                        

    private sealed class State
    {
        public FrameworkElement Parent = null!;
        public SolidColorBrush? BgBrush;
        public SolidColorBrush? BorderBrush;
        public Color NormalBg;
        public Color NormalBorder;
        public ScaleTransform Scale = new(1, 1);
        public TranslateTransform Translate = new(0, 0);
        public bool UsesTransform;
        public readonly List<(DependencyProperty Dp, Type Owner)> Hooks = new();
        public EventHandler Handler = null!;
        public bool Initialized;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    private static void OnStateAttached(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        if (States.TryGetValue(el, out _)) return;

        var st = new State();
        States.Add(el, st);

        el.Loaded += OnElementLoaded;
        el.Unloaded += OnElementUnloaded;
        if (el.IsLoaded) Initialize(el, st);
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        if (States.TryGetValue(el, out State? st)) Initialize(el, st);
    }

    private static void OnElementUnloaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        if (States.TryGetValue(el, out State? st)) Detach(st);
    }

    private static void Initialize(FrameworkElement el, State st)
    {
        if (st.Initialized) return;
        st.Initialized = true;
        st.Parent = el.TemplatedParent as FrameworkElement ?? el;

                                                                                                             
        switch (el)
        {
            case Border b:
                st.NormalBg = ColorOf(b.Background);
                st.NormalBorder = ColorOf(b.BorderBrush);
                st.BgBrush = new SolidColorBrush(st.NormalBg);
                st.BorderBrush = new SolidColorBrush(st.NormalBorder);
                b.Background = st.BgBrush;
                b.BorderBrush = st.BorderBrush;
                break;
            case Shape s:
                st.NormalBg = ColorOf(s.Fill);
                st.NormalBorder = ColorOf(s.Stroke);
                st.BgBrush = new SolidColorBrush(st.NormalBg);
                st.BorderBrush = new SolidColorBrush(st.NormalBorder);
                s.Fill = st.BgBrush;
                s.Stroke = st.BorderBrush;
                break;
        }

        bool needsTransform = GetHoverScale(el) != 1.0 || GetPressedScale(el) != 1.0 || GetCheckedOffsetX(el) != 0.0;
        if (needsTransform)
        {
            var group = new TransformGroup();
            group.Children.Add(st.Scale);
            group.Children.Add(st.Translate);
            el.RenderTransform = group;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            st.UsesTransform = true;
        }

        st.Handler = (_, _) => Apply(el, st, snap: false);

        Hook(st, UIElement.IsMouseOverProperty, typeof(UIElement));
        Hook(st, UIElement.IsKeyboardFocusedProperty, typeof(UIElement));
        if (st.Parent is ToggleButton) Hook(st, ToggleButton.IsCheckedProperty, typeof(ToggleButton));
        if (st.Parent is ButtonBase) Hook(st, ButtonBase.IsPressedProperty, typeof(ButtonBase));
        if (st.Parent is Thumb) Hook(st, Thumb.IsDraggingProperty, typeof(Thumb));

        Apply(el, st, snap: true);
    }

    private static void Hook(State st, DependencyProperty dp, Type owner)
    {
        DependencyPropertyDescriptor.FromProperty(dp, owner)?.AddValueChanged(st.Parent, st.Handler);
        st.Hooks.Add((dp, owner));
    }

    private static void Detach(State st)
    {
        if (!st.Initialized) return;
        foreach (var (dp, owner) in st.Hooks)
        {
            DependencyPropertyDescriptor.FromProperty(dp, owner)?.RemoveValueChanged(st.Parent, st.Handler);
        }
        st.Hooks.Clear();
        st.Initialized = false;
    }

    private static Color ColorOf(Brush? b) =>
        b is SolidColorBrush scb ? scb.Color : Colors.Transparent;

    private static Color? ColorOfOrNull(Brush? b) => b is SolidColorBrush scb ? scb.Color : null;

                                                                                        

    private static void Apply(FrameworkElement el, State st, bool snap)
    {
        FrameworkElement p = st.Parent;

        bool pressed = p switch
        {
            ButtonBase bb => bb.IsPressed,
            Thumb t => t.IsDragging,
            _ => false
        };
        bool isChecked = p is ToggleButton tb && tb.IsChecked == true;
        bool hover = p.IsMouseOver;
        bool focused = p.IsKeyboardFocused;

        bool animate = Enabled && !snap && el.IsLoaded;

              
        Color? bgTarget = null;
        if (pressed) bgTarget = ColorOfOrNull(GetPressedBackground(el)) ?? ColorOfOrNull(GetHoverBackground(el));
        if (bgTarget == null && isChecked) bgTarget = ColorOfOrNull(GetCheckedBackground(el));
        if (bgTarget == null && hover) bgTarget = ColorOfOrNull(GetHoverBackground(el));
        AnimateColor(st.BgBrush, bgTarget ?? st.NormalBg, animate);

                
        Color? brTarget = null;
        if (pressed) brTarget = ColorOfOrNull(GetPressedBorder(el)) ?? ColorOfOrNull(GetHoverBorder(el));
        if (brTarget == null && isChecked) brTarget = ColorOfOrNull(GetCheckedBorder(el));
        if (brTarget == null && hover) brTarget = ColorOfOrNull(GetHoverBorder(el));
        if (brTarget == null && focused) brTarget = ColorOfOrNull(GetFocusBorder(el));
        AnimateColor(st.BorderBrush, brTarget ?? st.NormalBorder, animate);

        if (st.UsesTransform)
        {
            double scale = 1.0;
            if (pressed && GetPressedScale(el) != 1.0) scale = GetPressedScale(el);
            else if (hover && GetHoverScale(el) != 1.0) scale = GetHoverScale(el);

            AnimateDouble(st.Scale, ScaleTransform.ScaleXProperty, scale, animate, ScaleDuration, false);
            AnimateDouble(st.Scale, ScaleTransform.ScaleYProperty, scale, animate, ScaleDuration, false);

            double offset = GetCheckedOffsetX(el);
            if (offset != 0.0)
            {
                AnimateDouble(st.Translate, TranslateTransform.XProperty, isChecked ? offset : 0.0, animate, ThumbDuration, true);
            }
        }
    }

    private static void AnimateColor(SolidColorBrush? brush, Color target, bool animate)
    {
        if (brush == null) return;

        Color current = brush.Color;

                                                                                                   
        if (current.A == 0 && target.A != 0)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = Color.FromArgb(0, target.R, target.G, target.B);
        }
        else if (target.A == 0 && current.A != 0)
        {
            target = Color.FromArgb(0, current.R, current.G, current.B);
        }

        if (!animate)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = target;
            return;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = target,
            Duration = ColorDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void AnimateDouble(Animatable target, DependencyProperty dp, double value, bool animate, Duration duration, bool overshoot)
    {
        if (!animate)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, value);
            return;
        }

        IEasingFunction ease = overshoot
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        target.BeginAnimation(dp, new DoubleAnimation
        {
            To = value,
            Duration = duration,
            EasingFunction = ease
        });
    }

                                                                                              

                 
                                                                                 
                                                            
                  
    public static void Reveal(FrameworkElement el, double fromX = 0, double fromY = 10)
    {
        el.Visibility = Visibility.Visible;

        if (!Enabled)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            if (el.RenderTransform is TranslateTransform t0)
            {
                t0.BeginAnimation(TranslateTransform.XProperty, null);
                t0.BeginAnimation(TranslateTransform.YProperty, null);
                t0.X = 0;
                t0.Y = 0;
            }
            return;
        }

        if (el.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
        {
            tt = new TranslateTransform();
            el.RenderTransform = tt;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = new Duration(TimeSpan.FromMilliseconds(260));

        el.Opacity = 1;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        });

        tt.X = 0;
        tt.Y = 0;
        if (fromX != 0)
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (fromY != 0)
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

                                                                        
    public static void PlayWindowEntrance(FrameworkElement root)
    {
        if (!Enabled) return;
        Reveal(root, 0, 14);
    }
}
