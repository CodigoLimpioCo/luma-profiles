using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaProfiles.Services;

/// <summary>Builds (and caches) the "after" picture of the before/after preview from the bundled reference photo.</summary>
public static class ProfilePreviewRenderer
{
    private const string ReferenceUri = "pack://application:,,,/LumaProfiles;component/Assets/color-reference.png";
    private const int PreviewWidth = 360;
    private const int MaxCachedImages = 300;

    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static BitmapSource? _original;
    private static byte[]? _pixels;
    private static int _stride;

    /// <summary>The untouched reference picture (the "before" side).</summary>
    public static BitmapSource? Original
    {
        get
        {
            Load();
            return _original;
        }
    }

    public static BitmapSource? Render(PreviewSettings settings)
    {
        Load();
        if (_original is null || _pixels is null) return null;

        var key = settings.Key;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var pixels = (byte[])_pixels.Clone();
        ProfilePreview.Apply(pixels, settings);
        var image = BitmapSource.Create(
            _original.PixelWidth, _original.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, _stride);
        image.Freeze();

        lock (Gate)
        {
            if (Cache.Count >= MaxCachedImages) Cache.Clear();
            Cache[key] = image;
        }

        return image;
    }

    private static void Load()
    {
        if (_original is not null) return;

        lock (Gate)
        {
            if (_original is not null) return;

            try
            {
                var source = new BitmapImage();
                source.BeginInit();
                source.UriSource = new Uri(ReferenceUri, UriKind.Absolute);
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.EndInit();
                source.Freeze();

                // Same crop the profile cards show (the centre strip of the photo), scaled down for speed.
                var crop = new CroppedBitmap(source, new Int32Rect(
                    (int)(source.PixelWidth * 0.333), (int)(source.PixelHeight * 0.08),
                    (int)(source.PixelWidth * 0.333), (int)(source.PixelHeight * 0.72)));
                var scale = PreviewWidth / (double)crop.PixelWidth;
                var scaled = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
                var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                _stride = converted.PixelWidth * 4;
                var pixels = new byte[_stride * converted.PixelHeight];
                converted.CopyPixels(pixels, _stride, 0);
                _pixels = pixels;
                _original = converted;
            }
            catch (Exception exception)
            {
                AppLog.Warn("The preview reference image could not be loaded.", exception);
            }
        }
    }
}
