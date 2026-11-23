using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frosty;

public static class AppIcon
{
    // Должно совпадать с <LogicalName> в Frosty.csproj. Ресурс вшит в сборку,
    // поэтому одинаково читается и из обычной сборки, и из PublishSingleFile
    // (никаких путей к файлам и Assembly.Location).
    private const string ResourceName = "Frosty.Assets.Frosty.ico";

    private static readonly object Sync = new();
    private static byte[]? _iconBytes;
    private static BitmapFrame[]? _frames;

    private static byte[] ReadIconBytes()
    {
        if (_iconBytes != null) return _iconBytes;

        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Не удалось загрузить ресурс Frosty.ico.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _iconBytes = ms.ToArray();
        return _iconBytes;
    }

    // Все кадры .ico (16/24/32/48/64/128/256), декодируются один раз на всё приложение.
    private static BitmapFrame[] GetFrames()
    {
        lock (Sync)
        {
            if (_frames != null) return _frames;

            var decoder = BitmapDecoder.Create(
                new MemoryStream(ReadIconBytes(), writable: false),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frames = decoder.Frames.ToArray();
            foreach (var f in frames)
            {
                if (f.CanFreeze) f.Freeze();
            }

            _frames = frames;
            return _frames;
        }
    }

    /// <summary>
    /// Для <see cref="System.Windows.Window.Icon"/>. Кадр принадлежит декодеру .ico,
    /// поэтому WPF сам подбирает из него нужные размеры для заголовка, панели задач и Alt+Tab.
    /// </summary>
    public static ImageSource WpfIcon => GetFrames()[0];

    /// <summary>
    /// Для <see cref="System.Windows.Controls.Image.Source"/> внутри интерфейса:
    /// берёт наименьший кадр, который не меньше запрошенного размера в физических пикселях
    /// (иначе — самый большой), чтобы логотип не был размытым и не растягивался.
    /// </summary>
    public static ImageSource GetLogo(int pixelSize)
    {
        var frames = GetFrames();
        BitmapFrame? best = null;
        BitmapFrame? largest = null;

        foreach (var f in frames)
        {
            if (largest == null || f.PixelWidth > largest.PixelWidth) largest = f;
            if (f.PixelWidth >= pixelSize && (best == null || f.PixelWidth < best.PixelWidth)) best = f;
        }

        return best ?? largest ?? frames[0];
    }

    public static (Icon Icon, IntPtr Handle) CreateIcon(int size)
    {
        using var stream = new MemoryStream(ReadIconBytes(), writable: false);
        var icon = new Icon(stream);
        return ((Icon)icon.Clone(), IntPtr.Zero);
    }
}
