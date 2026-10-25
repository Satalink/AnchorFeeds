using System.Net.Http;
using System.Windows.Media.Imaging;

namespace AnchorFeeds.Services;

public static class ArticleMediaCache
{
    private const int MaximumImageSizeBytes = 10_000_000;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> ImageExtensions =
        [".avif", ".bmp", ".gif", ".ico", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"];

    public static async Task<string?> DownloadImageAsync(
        string imageUrl,
        string articleFolder,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, imageUri);
        request.Headers.UserAgent.ParseAdd("AnchorFeeds/1.0");
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumImageSizeBytes)
        {
            throw new InvalidDataException($"Article image exceeds {MaximumImageSizeBytes / 1_000_000} MB.");
        }

        await using var imageStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var bytesRead = await imageStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }
            if (memoryStream.Length + bytesRead > MaximumImageSizeBytes)
            {
                throw new InvalidDataException($"Article image exceeds {MaximumImageSizeBytes / 1_000_000} MB.");
            }
            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (memoryStream.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(articleFolder);
        var extension = GetImageExtension(
            response.Content.Headers.ContentType?.MediaType,
            imageUri.AbsolutePath);
        var imagePath = Path.Combine(articleFolder, $"image{extension}");
        var temporaryPath = imagePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, memoryStream.ToArray(), cancellationToken);
            File.Move(temporaryPath, imagePath, true);
            foreach (var oldImage in Directory.EnumerateFiles(articleFolder, "image.*")
                         .Where(path => !path.Equals(imagePath, StringComparison.OrdinalIgnoreCase) &&
                                        ImageExtensions.Contains(Path.GetExtension(path))))
            {
                File.Delete(oldImage);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return imagePath;
    }

    public static BitmapImage LoadBitmap(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1200;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static string? FindCachedImage(string articleFolder) =>
        Directory.Exists(articleFolder)
            ? Directory.EnumerateFiles(articleFolder, "image.*")
                .FirstOrDefault(path => ImageExtensions.Contains(Path.GetExtension(path)))
            : null;

    private static string GetImageExtension(string? mediaType, string imagePath)
    {
        var mediaExtension = mediaType?.ToLowerInvariant() switch
        {
            "image/avif" => ".avif",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            "image/vnd.microsoft.icon" or "image/x-icon" => ".ico",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/tiff" => ".tif",
            "image/webp" => ".webp",
            _ => null
        };
        if (mediaExtension is not null)
        {
            return mediaExtension;
        }

        var pathExtension = Path.GetExtension(imagePath).ToLowerInvariant();
        return ImageExtensions.Contains(pathExtension) ? pathExtension : ".jpg";
    }
}
