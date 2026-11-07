using YamlDotNet.Serialization;

namespace AnchorFeeds.Models;

public sealed class TickerSettings
{
    public const double MinHeight = 48;
    public const double MaxHeight = 200;
    public const string TopPosition = "top";
    public const string BottomPosition = "bottom";
    public const string NotifyNone = "none";
    public const string NotifyWindows = "windows";
    public const string NotifySound = "sound";
    public const string ClickOpenArticle = "open_article";
    public const string ClickOpenSourceInBrowser = "open_source_in_browser";
    public const string ClickPlayBodyAudio = "play_body_audio";
    public const string ClickPlayBodyAudioInTicker = "play_body_audio_in_ticker";
    public const string ClickMarkAsRead = "mark_as_read";

    [YamlMember(Alias = "show_when_minimized")]
    public bool ShowWhenMinimized { get; set; }

    [YamlMember(Alias = "show_when_closed_to_tray")]
    public bool ShowWhenClosedToTray { get; set; }

    [YamlMember(Alias = "position")]
    public string Position { get; set; } = BottomPosition;

    [YamlMember(Alias = "font_family")]
    public string FontFamily { get; set; } = "Segoe UI";

    [YamlMember(Alias = "font_size")]
    public double FontSize { get; set; } = 14;

    [YamlMember(Alias = "height")]
    public double Height { get; set; } = 64;

    // Pixels per second.
    [YamlMember(Alias = "scroll_speed")]
    public double ScrollSpeed { get; set; } = 60;

    [YamlMember(Alias = "mark_read_on_title_read")]
    public bool MarkReadWhenTitleRead { get; set; }

    [YamlMember(Alias = "click_action")]
    public string ClickAction { get; set; } = ClickPlayBodyAudio;

    [YamlMember(Alias = "right_click_action")]
    public string RightClickAction { get; set; } = ClickPlayBodyAudio;

    [YamlMember(Alias = "favorites_notification")]
    public string FavoritesNotification { get; set; } = NotifyWindows;

    // Empty means the default Windows notification sound.
    [YamlMember(Alias = "favorites_sound_path")]
    public string FavoritesSoundPath { get; set; } = string.Empty;

    public void Validate()
    {
        if (Position is not (TopPosition or BottomPosition))
        {
            throw new InvalidDataException("ticker position must be 'top' or 'bottom'.");
        }

        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            throw new InvalidDataException("ticker font_family must be a non-empty font family.");
        }

        if (!double.IsFinite(FontSize) || FontSize is < 8 or > 48)
        {
            throw new InvalidDataException("ticker font_size must be between 8 and 48.");
        }

        if (!double.IsFinite(Height) || Height is < MinHeight or > MaxHeight)
        {
            throw new InvalidDataException("ticker height must be between 48 and 200 pixels.");
        }

        if (!double.IsFinite(ScrollSpeed) || ScrollSpeed is < 10 or > 400)
        {
            throw new InvalidDataException("ticker scroll_speed must be between 10 and 400 pixels per second.");
        }

        if (FavoritesNotification is not (NotifyNone or NotifyWindows or NotifySound))
        {
            throw new InvalidDataException("ticker favorites_notification must be 'none', 'windows', or 'sound'.");
        }

        ValidateClickAction(ClickAction, "click_action");
        ValidateClickAction(RightClickAction, "right_click_action");

        FavoritesSoundPath ??= string.Empty;
        FavoritesSoundPath = FavoritesSoundPath.Trim();
    }

    private static void ValidateClickAction(string action, string setting)
    {
        if (action is not (ClickOpenArticle or ClickOpenSourceInBrowser or ClickPlayBodyAudio or ClickPlayBodyAudioInTicker or ClickMarkAsRead))
        {
            throw new InvalidDataException($"ticker {setting} must be 'open_article', 'open_source_in_browser', 'play_body_audio', 'play_body_audio_in_ticker', or 'mark_as_read'.");
        }
    }
}
