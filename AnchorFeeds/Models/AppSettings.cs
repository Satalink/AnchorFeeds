using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Models;

public sealed class AppSettings
{
    [YamlMember(Alias = "feeds")]
    public List<string> Feeds { get; set; } = [];

    [YamlMember(Alias = "feed_sources")]
    public List<FeedConfiguration> FeedSources { get; set; } = [];

    [YamlMember(Alias = "max_articles_per_feed")]
    public object? LegacyMaxArticlesPerFeed { get; set; }

    [YamlMember(Alias = "output_dir")]
    public string OutputDirectory { get; set; } = string.Empty;

    [YamlMember(Alias = "final_output")]
    public string FinalOutput { get; set; } = string.Empty;

    [YamlMember(Alias = "retention")]
    public string Retention { get; set; } = string.Empty;

    [YamlMember(Alias = "starred_retention")]
    public string StarredRetention { get; set; } = "never";

    [YamlMember(Alias = "favorites_retention")]
    public string FavoritesRetention { get; set; } = "never";

    [YamlMember(Alias = "feed_update_interval")]
    public string FeedUpdateInterval { get; set; } = "Manual only";

    [YamlMember(Alias = "portable_mode")]
    public bool PortableMode { get; set; } = true;

    [YamlIgnore]
    public TimeSpan? FeedUpdateIntervalPeriod { get; private set; }

    [YamlMember(Alias = "mark_as_played")]
    public string MarkAsPlayed { get; set; } = "when_started";

    [YamlMember(Alias = "playback_speed")]
    public double? LegacyPlaybackSpeed { get; set; }

    [YamlMember(Alias = "scan_mode")]
    public bool ScanMode { get; set; } = true;

    [YamlMember(Alias = "scan_countdown_seconds")]
    public int ScanCountdownSeconds { get; set; } = 7;

    [YamlMember(Alias = "banned_phrases")]
    public List<string> BannedPhrases { get; set; } = [];

    [YamlMember(Alias = "favorited_phrases")]
    public List<string> FavoritedPhrases { get; set; } = [];

    [YamlMember(Alias = "starred_phrases")]
    public List<string> StarredPhrases { get; set; } = [];

    [YamlMember(Alias = "show_matching_keywords")]
    public bool ShowMatchingKeywords { get; set; } = true;

    [YamlMember(Alias = "full_mode_for_favorites")]
    public bool FullModeForFavorites { get; set; }

    [YamlMember(Alias = "auto_play_next")]
    public bool AutoPlayNext { get; set; } = true;

    [YamlMember(Alias = "retrieve_latest_feeds_on_launch")]
    public bool RetrieveLatestFeedsOnLaunch { get; set; } = true;

    [YamlMember(Alias = "ai_prompt_instruction")]
    public string AiPromptInstruction { get; set; } = "Summarize this article:";

    // Retained so settings created before the AI toggle can still be loaded.
    [YamlMember(Alias = "ai_chat_agent")]
    public string AiChatAgent { get; set; } = "none";

    [YamlMember(Alias = "ai_chat_enabled")]
    public bool AiChatEnabled { get; set; }

    [YamlMember(Alias = "minimize_to_tray")]
    public bool MinimizeToTray { get; set; }

    [YamlMember(Alias = "audio_muted")]
    public bool AudioMuted { get; set; }

    [YamlMember(Alias = "close_to_tray")]
    public bool CloseToTray { get; set; }

    [YamlMember(Alias = "launch_at_startup")]
    public bool LaunchAtStartup { get; set; } = true;

    [YamlMember(Alias = "debug_logging")]
    public bool DebugLoggingEnabled { get; set; }

    [YamlMember(Alias = "show_article_text")]
    public bool ShowArticleText { get; set; } = true;

    [YamlMember(Alias = "unplayed_only")]
    public bool ShowUnplayedOnly { get; set; }

    [YamlMember(Alias = "starred_only")]
    public bool ShowStarredOnly { get; set; }

    [YamlMember(Alias = "favorite_categories_only")]
    public bool ShowFavoriteCategoriesOnly { get; set; }

    [YamlMember(Alias = "article_sort_order")]
    public string ArticleSortOrder { get; set; } = "newest_first";

    [YamlMember(Alias = "group_articles_by_feed")]
    public bool GroupArticlesByFeed { get; set; }

    [YamlMember(Alias = "application_language")]
    public string ApplicationLanguage { get; set; } = "system";

    [YamlMember(Alias = "appearance_theme")]
    public string AppearanceTheme { get; set; } = "light";

    [YamlMember(Alias = "hotkeys")]
    public HotkeySettings Hotkeys { get; set; } = new();

    [YamlMember(Alias = "ticker")]
    public TickerSettings Ticker { get; set; } = new();

    [YamlMember(Alias = "article_list_font_family")]
    public string ArticleListFontFamily { get; set; } = "Segoe UI";

    [YamlMember(Alias = "article_list_font_size")]
    public double ArticleListFontSize { get; set; } = 12;

    [YamlMember(Alias = "article_text_font_family")]
    public string ArticleTextFontFamily { get; set; } = "Segoe UI";

    [YamlMember(Alias = "article_text_font_size")]
    public double ArticleTextFontSize { get; set; } = 14;

    [YamlMember(Alias = "mp3_bitrate_kbps")]
    public int Mp3BitrateKbps { get; set; }

    public const string EdgeTtsEngine = "edge_tts";
    public const string EdgeTtsOnlyEngine = "edge_tts_only";
    public const string WindowsSpeechEngine = "windows";

    public static bool UsesEdgeTts(string engine) => engine is EdgeTtsEngine or EdgeTtsOnlyEngine;

    [YamlMember(Alias = "tts_engine")]
    public string TtsEngine { get; set; } = EdgeTtsEngine;

    [YamlMember(Alias = "article_list_width_ratio")]
    public double ArticleListWidthRatio { get; set; } = 0.4;

    [YamlMember(Alias = "article_image_height_ratio")]
    public double ArticleImageHeightRatio { get; set; } = 2d / 3d;

    [YamlMember(Alias = "voices")]
    public VoiceSettings Voices { get; set; } = new();

    [YamlIgnore]
    public TimeSpan RetentionPeriod { get; private set; }

    [YamlIgnore]
    public TimeSpan? StarredRetentionPeriod { get; private set; }

    [YamlIgnore]
    public TimeSpan? FavoritesRetentionPeriod { get; private set; }

    [YamlIgnore]
    public bool MarkOnStart => MarkAsPlayed.Equals("when_started", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        LegacyMaxArticlesPerFeed = null;

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

        if (string.IsNullOrWhiteSpace(AiPromptInstruction) || AiPromptInstruction.Length > 500)
        {
            throw new InvalidDataException("ai_prompt_instruction must be between 1 and 500 characters.");
        }

        if (!string.IsNullOrWhiteSpace(AiChatAgent) &&
            !AiChatAgent.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            AiChatEnabled = true;
        }

        Hotkeys ??= new HotkeySettings();
        Hotkeys.MigrateLegacyDefaults();
        Hotkeys.Validate();

        Ticker ??= new TickerSettings();
        Ticker.Validate();

        ValidateFontFamily(ArticleListFontFamily, "article_list_font_family");
        ValidateFontFamily(ArticleTextFontFamily, "article_text_font_family");
        ValidateFontSize(ArticleListFontSize, "article_list_font_size");
        ValidateFontSize(ArticleTextFontSize, "article_text_font_size");

        BannedPhrases ??= [];
        FavoritedPhrases ??= [];
        StarredPhrases ??= [];
        Feeds ??= [];
        FeedSources ??= [];
        if (FeedSources.Count == 0 && Feeds.Count > 0)
        {
            FeedSources = Feeds.Select(FeedConfiguration.FromLegacyUrl).ToList();
        }

        if (FeedSources.Count == 0)
        {
            throw new InvalidDataException("settings.yaml must contain at least one RSS feed.");
        }

        foreach (var feed in FeedSources)
        {
            feed.Validate();
        }
        Feeds = FeedSources.Select(feed => feed.Url).ToList();

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            throw new InvalidDataException("The media output directory must be non-empty.");
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

        if (ScanCountdownSeconds is not (0 or 3 or 5 or 7 or 10 or 15 or 20 or 30 or 60))
        {
            throw new InvalidDataException("scan_countdown_seconds must be 0, 3, 5, 7, 10, 15, 20, 30, or 60.");
        }

        if (Mp3BitrateKbps is not (0 or 48 or 96 or 128 or 160 or 192 or 256 or 320))
        {
            throw new InvalidDataException("mp3_bitrate_kbps must be 0 (Edge TTS default), 48, 96, 128, 160, 192, 256, or 320.");
        }

        if (TtsEngine is not (EdgeTtsEngine or EdgeTtsOnlyEngine or WindowsSpeechEngine))
        {
            throw new InvalidDataException("tts_engine must be 'edge_tts', 'edge_tts_only', or 'windows'.");
        }

        if (!double.IsFinite(ArticleListWidthRatio) || ArticleListWidthRatio is < 0.2 or > 0.8)
        {
            throw new InvalidDataException("article_list_width_ratio must be between 0.2 and 0.8.");
        }

        if (!double.IsFinite(ArticleImageHeightRatio) || ArticleImageHeightRatio is < 0.2 or > 0.8)
        {
            throw new InvalidDataException("article_image_height_ratio must be between 0.2 and 0.8.");
        }

        if (ArticleSortOrder is not ("alphabetical_ascending" or "alphabetical_descending" or "newest_first" or "oldest_first"))
        {
            throw new InvalidDataException("article_sort_order must be alphabetical_ascending, alphabetical_descending, newest_first, or oldest_first.");
        }

        FeedUpdateIntervalPeriod = null;
        if (!string.IsNullOrWhiteSpace(FeedUpdateInterval) &&
            !FeedUpdateInterval.Trim().Equals("Manual only", StringComparison.OrdinalIgnoreCase))
        {
            var intervalMatch = Regex.Match(
                FeedUpdateInterval.Trim(),
                @"^(?<amount>[1-9][0-9]{0,4})\s+(?<unit>minutes?|hours?|days?)$",
                RegexOptions.IgnoreCase);
            if (!intervalMatch.Success)
            {
                throw new InvalidDataException("feed_update_interval must be 'Manual only' or a duration such as '15 minutes', '1 hour', or '6 hours'.");
            }

            var intervalAmount = double.Parse(intervalMatch.Groups["amount"].Value, CultureInfo.InvariantCulture);
            FeedUpdateIntervalPeriod = char.ToLowerInvariant(intervalMatch.Groups["unit"].Value[0]) switch
            {
                'm' => TimeSpan.FromMinutes(intervalAmount),
                'h' => TimeSpan.FromHours(intervalAmount),
                _ => TimeSpan.FromDays(intervalAmount)
            };
            if (FeedUpdateIntervalPeriod < TimeSpan.FromMinutes(1))
            {
                throw new InvalidDataException("feed_update_interval must be at least 1 minute.");
            }
        }

        RetentionPeriod = ParseRetention(Retention, "retention", allowNever: false)!.Value;
        StarredRetentionPeriod = ParseRetention(StarredRetention, "starred_retention", allowNever: true);
        FavoritesRetentionPeriod = ParseRetention(FavoritesRetention, "favorites_retention", allowNever: true);
    }

    private static TimeSpan? ParseRetention(string? value, string name, bool allowNever)
    {
        if (allowNever && string.Equals(value?.Trim(), "never", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Regex.Match(
            value?.Trim() ?? string.Empty,
            @"^(?<amount>[1-9][0-9]*)\s+(?<unit>hours?|days?|weeks?)$",
            RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups["amount"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidDataException($"{name} must be {(allowNever ? "'never' or " : string.Empty)}a positive duration such as '6 hours', '2 days', or '4 weeks'.");
        }

        try
        {
            return match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "hour" or "hours" => TimeSpan.FromHours(amount),
                "day" or "days" => TimeSpan.FromDays(amount),
                "week" or "weeks" => TimeSpan.FromDays(amount * 7d),
                _ => throw new InvalidDataException("Unsupported retention unit.")
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"{name} duration is too large.", ex);
        }
    }

    private static void ValidateFontFamily(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{settingName} must be a non-empty font family.");
        }

        try
        {
            _ = new System.Windows.Media.FontFamily(value);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{settingName} is not a valid font family.", ex);
        }
    }

    private static void ValidateFontSize(double value, string settingName)
    {
        if (!double.IsFinite(value) || value is < 8 or > 48)
        {
            throw new InvalidDataException($"{settingName} must be between 8 and 48.");
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
