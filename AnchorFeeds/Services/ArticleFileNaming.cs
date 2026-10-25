using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AnchorFeeds.Services;

public static class ArticleFileNaming
{
    public static bool UsesArticleFolderTemplate(string finalOutput) =>
        finalOutput.EndsWith(Path.DirectorySeparatorChar) ||
        finalOutput.EndsWith(Path.AltDirectorySeparatorChar) ||
        Path.GetFileName(finalOutput).Equals("body.mp3", StringComparison.OrdinalIgnoreCase);

    public static string GetArticleFolderTemplate(string finalOutput) =>
        UsesArticleFolderTemplate(finalOutput)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(
                Path.GetFileName(finalOutput).Equals("body.mp3", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(finalOutput) ?? string.Empty
                    : finalOutput)) ?? string.Empty
            : Path.GetFileNameWithoutExtension(finalOutput);

    public static string GetLegacyFilenameTemplate(string finalOutput) =>
        UsesArticleFolderTemplate(finalOutput)
            ? $"{GetArticleFolderTemplate(finalOutput)}.mp3"
            : Path.GetFileName(finalOutput);

    public static string ResolveOutputDirectory(string baseDirectory, string finalOutput)
    {
        var feedPlaceholder = finalOutput.IndexOf("{feed}", StringComparison.Ordinal);
        if (feedPlaceholder < 0)
        {
            throw new InvalidDataException("final_output must contain {feed}.");
        }

        var pathPrefix = finalOutput[..feedPlaceholder];
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(pathPrefix) ? pathPrefix : Path.Combine(baseDirectory, pathPrefix));
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    public static string BuildArticleFolderPath(
        string outputDirectory,
        string finalOutput,
        string feedSlug,
        string titleSlug)
    {
        var folderName = GetArticleFolderTemplate(finalOutput)
            .Replace("{feed}", feedSlug, StringComparison.Ordinal)
            .Replace("{title}", titleSlug, StringComparison.Ordinal);
        return Path.Combine(outputDirectory, folderName);
    }

    public static string BuildLegacyOutputPath(
        string outputDirectory,
        string finalOutput,
        string feedSlug,
        string titleSlug)
    {
        var fileName = GetLegacyFilenameTemplate(finalOutput)
            .Replace("{feed}", feedSlug, StringComparison.Ordinal)
            .Replace("{title}", titleSlug, StringComparison.Ordinal);
        return Path.Combine(outputDirectory, fileName);
    }

    public static string BuildBodyAudioPath(string finalOutput, string articleFolder)
    {
        var usesBodyFilename = Path.GetFileName(finalOutput).Equals("body.mp3", StringComparison.OrdinalIgnoreCase);
        return Path.Combine(articleFolder, usesBodyFilename ? Path.GetFileName(finalOutput) : "body.mp3");
    }

    public static string BuildFeedSlug(string feedUrl)
    {
        var uri = new Uri(feedUrl);
        return $"{Slug(uri.Host, "feed")}-{ShortHash(feedUrl)}";
    }

    public static string BuildTitleSlug(string title)
    {
        var normalizedTitle = NormalizeTitle(title);
        var slug = Slug(normalizedTitle, "untitled");
        if (slug.Length > 100)
        {
            slug = slug[..100].TrimEnd('-');
        }

        return $"{slug}-{ShortHash(normalizedTitle)}";
    }

    public static string NormalizeTitle(string title) =>
        Regex.Replace(title.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToLowerInvariant();

    public static Regex BuildFilenameRegex(string filenameTemplate)
    {
        if (!filenameTemplate.Contains("{feed}", StringComparison.Ordinal) ||
            !filenameTemplate.Contains("{title}", StringComparison.Ordinal) ||
            filenameTemplate.IndexOf("{feed}", StringComparison.Ordinal) >= filenameTemplate.IndexOf("{title}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("final_output must contain {feed} followed by {title}.");
        }

        var pattern = Regex.Escape(filenameTemplate)
            .Replace(Regex.Escape("{feed}"), "(?<feed>[a-z0-9-]+)")
            .Replace(Regex.Escape("{title}"), "(?<title>[a-z0-9-]+)");
        return new Regex($"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public static string ReadableSlug(string value)
    {
        var withoutHash = Regex.Replace(value, "-[a-f0-9]{12}$", string.Empty, RegexOptions.IgnoreCase);
        return string.Join(' ', withoutHash.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string Slug(string value, string fallback)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? fallback : slug;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
