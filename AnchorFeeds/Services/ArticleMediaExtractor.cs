using System.Net;
using System.Text.RegularExpressions;

namespace AnchorFeeds.Services;

public enum ArticleMediaKind
{
    PrimaryImage,
    Image,
    YouTubeVideo
}

public enum VideoHost
{
    YouTube,
    Rumble,
    BitChute
}

public sealed record ArticleMediaItem(
    ArticleMediaKind Kind,
    string Url,
    string? VideoId = null,
    VideoHost Host = VideoHost.YouTube)
{
    public string WatchUrl => Host switch
    {
        VideoHost.Rumble => $"https://rumble.com/embed/{VideoId}/",
        VideoHost.BitChute => $"https://www.bitchute.com/video/{VideoId}/",
        _ => $"https://www.youtube.com/watch?v={VideoId}"
    };

    public string YouTubeThumbnailUrl => Host == VideoHost.YouTube
        ? $"https://img.youtube.com/vi/{VideoId}/hqdefault.jpg"
        : string.Empty;
}

/// <summary>
/// Finds images and YouTube videos in the raw article HTML. This runs on the original markup because
/// speech text cleaning removes iframes and URLs, which would otherwise lose the video links.
/// </summary>
public static class ArticleMediaExtractor
{
    private const int MaxItems = 12;
    private static readonly HashSet<string> TrackingQueryParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "fbclid",
            "gclid",
            "mc_cid",
            "mc_eid",
            "ref",
            "referrer",
            "utm_campaign",
            "utm_content",
            "utm_medium",
            "utm_source",
            "utm_term"
        };

    private static readonly Regex ImageRegex = new(
        """<img\b(?<attrs>[^>]*?)\bsrc\s*=\s*["'](?<url>[^"']+)["'](?<rest>[^>]*)>""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TinySizeRegex = new(
        """\b(?:width|height)\s*=\s*["']?[0-2]["']?(?![\d%])""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YouTubeRegex = new(
        @"(?:youtube(?:-nocookie)?\.com/(?:embed/|watch\?(?:[^""'\s<>]*?&)?v=|shorts/|live/|v/)|youtu\.be/)(?<id>[A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RumbleRegex = new(
        @"rumble\.com/embed/(?<id>v[A-Za-z0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BitChuteRegex = new(
        @"bitchute\.com/(?:embed|video)/(?<id>[A-Za-z0-9_-]{6,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ArticleMediaItem> Extract(string? html, params string?[] excludedImageUrls)
    {
        var result = new List<ArticleMediaItem>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return result;
        }

        var text = WebUtility.HtmlDecode(html);
        var excluded = new HashSet<string>(
            excludedImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => GetImageDeduplicationKey(url!)),
            StringComparer.OrdinalIgnoreCase);
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenVideos = new HashSet<string>(StringComparer.Ordinal);

        var videos = new List<ArticleMediaItem>();
        foreach (Match match in YouTubeRegex.Matches(text))
        {
            var id = match.Groups["id"].Value;
            if (seenVideos.Add(id))
            {
                videos.Add(new ArticleMediaItem(ArticleMediaKind.YouTubeVideo, string.Empty, id));
            }
        }

        foreach (Match match in RumbleRegex.Matches(text))
        {
            var id = match.Groups["id"].Value;
            if (seenVideos.Add("rumble:" + id))
            {
                videos.Add(new ArticleMediaItem(ArticleMediaKind.YouTubeVideo, string.Empty, id, VideoHost.Rumble));
            }
        }

        foreach (Match match in BitChuteRegex.Matches(text))
        {
            var id = match.Groups["id"].Value;
            if (seenVideos.Add("bitchute:" + id))
            {
                videos.Add(new ArticleMediaItem(ArticleMediaKind.YouTubeVideo, string.Empty, id, VideoHost.BitChute));
            }
        }

        foreach (Match match in ImageRegex.Matches(text))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
            var hasUri = Uri.TryCreate(url, UriKind.Absolute, out var uri);
            var deduplicationKey = hasUri && uri is not null
                ? GetImageDeduplicationKey(uri)
                : url;
            if (!hasUri ||
                uri is null ||
                uri.Scheme is not ("http" or "https") ||
                url.Contains("/emoji/", StringComparison.OrdinalIgnoreCase) ||
                TinySizeRegex.IsMatch(match.Groups["attrs"].Value + " " + match.Groups["rest"].Value) ||
                excluded.Contains(deduplicationKey) ||
                !seenImages.Add(deduplicationKey))
            {
                continue;
            }

            result.Add(new ArticleMediaItem(ArticleMediaKind.Image, url));
        }

        result.AddRange(videos);
        return result.Take(MaxItems).ToList();
    }

    private static string GetImageDeduplicationKey(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? GetImageDeduplicationKey(uri)
            : url.Trim();

    private static string GetImageDeduplicationKey(Uri uri)
    {
        var queryParameters = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter =>
            {
                var separator = parameter.IndexOf('=');
                var name = separator < 0 ? parameter : parameter[..separator];
                var value = separator < 0 ? string.Empty : parameter[(separator + 1)..];
                return (Name: Uri.UnescapeDataString(name.Replace('+', ' ')), Value: value);
            })
            .Where(parameter => !IsTrackingQueryParameter(parameter.Name))
            .Select(parameter => $"{parameter.Name}={parameter.Value}")
            .OrderBy(parameter => parameter, StringComparer.Ordinal)
            .ToArray();

        var path = uri.GetLeftPart(UriPartial.Path);
        return queryParameters.Length == 0
            ? path
            : $"{path}?{string.Join("&", queryParameters)}";
    }

    private static bool IsTrackingQueryParameter(string name) =>
        TrackingQueryParameters.Contains(name) ||
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
}
