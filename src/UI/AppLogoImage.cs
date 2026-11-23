using System.Windows;
using System.Windows.Media;
using WpfImage = System.Windows.Controls.Image;

namespace Frosty;

/// <summary>
/// Единый элемент логотипа приложения для XAML: &lt;local:AppLogoImage Width="18" Height="18" /&gt;.
/// Источник — Assets/Frosty.ico через <see cref="AppIcon"/>. Подбирает кадр под размер и DPI,
/// сохраняет пропорции (Stretch.Uniform), не меняет раскладку: размеры задаются в XAML, как у прежнего Path.
/// </summary>
public sealed class AppLogoImage : WpfImage
{
    private const double DefaultDipSize = 32;

    public AppLogoImage()
    {
        Stretch = Stretch.Uniform;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Focusable = false;
        Loaded += (_, _) => UpdateSource();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        UpdateSource();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateSource();
    }

    private void UpdateSource()
    {
        try
        {
            double dip = !double.IsNaN(Width) && !double.IsNaN(Height)
                ? Math.Max(Width, Height)
                : DefaultDipSize;

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pixels = (int)Math.Ceiling(dip * scale);

            Source = AppIcon.GetLogo(pixels);
        }
        catch (Exception ex)
        {
            // Отсутствие логотипа не должно ронять окно.
            System.Diagnostics.Debug.WriteLine($"AppLogoImage: {ex.Message}");
        }
    }
}
