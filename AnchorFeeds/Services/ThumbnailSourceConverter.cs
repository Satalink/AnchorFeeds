using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AnchorFeeds.Services;

// Loads local images into memory so the file is not locked while it is displayed.
public sealed class ThumbnailSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Uri uri)
        {
            return null;
        }

        if (!uri.IsFile)
        {
            return uri;
        }

        try
        {
            return ArticleMediaCache.LoadBitmap(uri.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
