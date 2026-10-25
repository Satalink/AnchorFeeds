using System.Globalization;
using System.Text.RegularExpressions;
using AnchorFeeds.Services;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Models;

public sealed class AppSettings
{
    [YamlMember(Alias = "feeds")]
    public List<string> Feeds { get; set; } = [];

    [YamlMember(Alias = "max_articles_per_feed")]
    public int MaxArticlesPerFeed { get; set; }

    [YamlMember(Alias = "output_dir")]
    public string OutputDirectory { get; set; } = string.Empty;

    [YamlMember(Alias = "final_output")]
    public string FinalOutput { get; set; } = string.Empty;

    [YamlMember(Alias = "retention")]
    public string Retention { get; set; } = string.Empty;

    [YamlMember(Alias = "mark_as_played")]
    public string MarkAsPlayed { get; set; } = "when_started";

    [YamlMember(Alias = "playback_speed")]
    public double PlaybackSpeed { get; set; } = 1.0;

    [YamlMember(Alias = "scan_mode")]
    public bool ScanMode { get; set; } = true;

    [YamlMember(Alias = "scan_countdown_seconds")]
    public int ScanCountdownSeconds { get; set; } = 7;

    [YamlMember(Alias = "auto_play_next")]
    public bool AutoPlayNext { get; set; } = true;

    [YamlMember(Alias = "retrieve_latest_feeds_on_launch")]
    public bool RetrieveLatestFeedsOnLaunch { get; set; } = true;

    [YamlMember(Alias = "show_article_text")]
    public bool ShowArticleText { get; set; } = true;

    [YamlMember(Alias = "unplayed_only")]
    public bool ShowUnplayedOnly { get; set; }

    [YamlMember(Alias = "starred_only")]
    public bool ShowStarredOnly { get; set; }

    [YamlMember(Alias = "article_sort_order")]
    public string ArticleSortOrder { get; set; } = "newest_first";

    [YamlMember(Alias = "group_articles_by_feed")]
    public bool GroupArticlesByFeed { get; set; }

    [YamlMember(Alias = "application_language")]
    public string ApplicationLanguage { get; set; } = "system";

    [YamlMember(Alias = "appearance_theme")]
    public string AppearanceTheme { get; set; } = "light";

    [YamlMember(Alias = "mp3_bitrate_kbps")]
    public int Mp3BitrateKbps { get; set; }

    [YamlMember(Alias = "article_list_width_ratio")]
    public double ArticleListWidthRatio { get; set; } = 0.4;

    [YamlMember(Alias = "voices")]
    public VoiceSettings Voices { get; set; } = new();

    [YamlIgnore]
    public TimeSpan RetentionPeriod { get; private set; }

    [YamlIgnore]
    public bool MarkOnStart => MarkAsPlayed.Equals("when_started", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        var supportedLanguages = new HashSet<string>(
            ["system", "en", "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"],
            StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(ApplicationLanguage) || !supportedLanguages.Contains(ApplicationLanguage))
        {
            throw new InvalidDataException("application_language must be 'system' or a supported language code.");
        }

        if (AppearanceTheme is not ("light" or "dark" or "charcoal" or "system_desktop"))
        {
            throw new InvalidDataException("appearance_theme must be 'light', 'dark', 'charcoal', or 'system_desktop'.");
        }

        if (Feeds is null || Feeds.Count == 0)
        {
            throw new InvalidDataException("settings.yaml must contain at least one RSS feed.");
        }

        foreach (var feed in Feeds)
        {
            if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException($"Invalid RSS feed URL: {feed}");
            }
        }

        if (MaxArticlesPerFeed < 1)
        {
            throw new InvalidDataException("max_articles_per_feed must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory) || string.IsNullOrWhiteSpace(FinalOutput))
        {
            throw new InvalidDataException("output_dir and final_output must be non-empty.");
        }

        if (!FinalOutput.Contains("{feed}", StringComparison.Ordinal) ||
            !FinalOutput.Contains("{title}", StringComparison.Ordinal) ||
            FinalOutput.IndexOf("{feed}", StringComparison.Ordinal) >= FinalOutput.IndexOf("{title}", StringComparison.Ordinal) ||
            FinalOutput.IndexOf("{feed}", StringComparison.Ordinal) != FinalOutput.LastIndexOf("{feed}", StringComparison.Ordinal) ||
            FinalOutput.IndexOf("{title}", StringComparison.Ordinal) != FinalOutput.LastIndexOf("{title}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("final_output must contain one {feed} placeholder followed by one {title} placeholder.");
        }

        if (ArticleFileNaming.UsesArticleFolderTemplate(FinalOutput))
        {
            var articleFolderTemplate = ArticleFileNaming.GetArticleFolderTemplate(FinalOutput);
            if (!articleFolderTemplate.Contains("{feed}", StringComparison.Ordinal) ||
                !articleFolderTemplate.Contains("{title}", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "When final_output uses an article folder, its folder name must contain both {feed} and {title}.");
            }
        }

        if (Voices is null || Voices.GetVoices().Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("voices cannot contain an empty voice name.");
        }

        if (MarkAsPlayed is null)
        {
            throw new InvalidDataException("mark_as_played must be 'when_started' or 'when_ended_or_skipped'.");
        }

        if (MarkAsPlayed.Equals("when_started", StringComparison.OrdinalIgnoreCase))
        {
            MarkAsPlayed = "when_started";
        }
        else if (MarkAsPlayed.Equals("when_ended_or_skipped", StringComparison.OrdinalIgnoreCase))
        {
            MarkAsPlayed = "when_ended_or_skipped";
        }
        else
        {
            throw new InvalidDataException("mark_as_played must be 'when_started' or 'when_ended_or_skipped'.");
        }

        if (PlaybackSpeed is 1.4 or 1.5 or 2.0)
        {
            PlaybackSpeed = 1.3;
        }
        else if (PlaybackSpeed is not (0.7 or 0.8 or 0.9 or 1.0 or 1.1 or 1.2 or 1.3))
        {
            throw new InvalidDataException("playback_speed must be one of 0.7, 0.8, 0.9, 1, 1.1, 1.2, or 1.3.");
        }

        if (ScanCountdownSeconds is not (3 or 5 or 7 or 10 or 15 or 20 or 30 or 60))
        {
            throw new InvalidDataException("scan_countdown_seconds must be 3, 5, 7, 10, 15, 20, 30, or 60.");
        }

        if (Mp3BitrateKbps is not (0 or 48 or 96 or 128 or 160 or 192 or 256 or 320))
        {
            throw new InvalidDataException("mp3_bitrate_kbps must be 0 (Edge TTS default), 48, 96, 128, 160, 192, 256, or 320.");
        }

        if (!double.IsFinite(ArticleListWidthRatio) || ArticleListWidthRatio is < 0.2 or > 0.8)
        {
            throw new InvalidDataException("article_list_width_ratio must be between 0.2 and 0.8.");
        }

        if (ArticleSortOrder is not ("alphabetical_ascending" or "alphabetical_descending" or "newest_first" or "oldest_first"))
        {
            throw new InvalidDataException("article_sort_order must be alphabetical_ascending, alphabetical_descending, newest_first, or oldest_first.");
        }

        if (string.IsNullOrWhiteSpace(Retention))
        {
            throw new InvalidDataException("retention must be a positive duration such as '6 hours', '2 days', or '4 weeks'.");
        }

        var match = Regex.Match(
            Retention,
            @"^(?<amount>[1-9][0-9]*)\s+(?<unit>hours?|days?|weeks?)$",
            RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups["amount"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidDataException("retention must be a positive duration such as '6 hours', '2 days', or '4 weeks'.");
        }

        try
        {
            RetentionPeriod = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "hour" or "hours" => TimeSpan.FromHours(amount),
                "day" or "days" => TimeSpan.FromDays(amount),
                "week" or "weeks" => TimeSpan.FromDays(amount * 7d),
                _ => throw new InvalidDataException("Unsupported retention unit.")
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException("retention duration is too large.", ex);
        }
    }
}

public sealed class VoiceSettings
{
    [YamlMember(Alias = "anchor_voices")]
    public List<string> AnchorVoices { get; set; } = [];

    [YamlMember(Alias = "anchor_1")]
    public string? LegacyAnchor1 { get; set; }

    [YamlMember(Alias = "anchor_2")]
    public string? LegacyAnchor2 { get; set; }

    [YamlMember(Alias = "anchor_1_voices")]
    public List<string>? LegacyAnchor1Voices { get; set; }

    [YamlMember(Alias = "anchor_2_voices")]
    public List<string>? LegacyAnchor2Voices { get; set; }

    public IReadOnlyList<string> GetVoices()
    {
        if (AnchorVoices.Count > 0)
        {
            return AnchorVoices;
        }

        return (LegacyAnchor1Voices ?? [])
            .Concat(LegacyAnchor2Voices ?? [])
            .Concat([LegacyAnchor1, LegacyAnchor2])
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .Select(voice => voice!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetVoices(IEnumerable<string> voices)
    {
        AnchorVoices = voices.ToList();
        LegacyAnchor1 = null;
        LegacyAnchor2 = null;
        LegacyAnchor1Voices = null;
        LegacyAnchor2Voices = null;
    }
}
