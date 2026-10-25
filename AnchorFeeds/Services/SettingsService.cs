using System.Text.RegularExpressions;
using System.Globalization;
using AnchorFeeds.Models;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Services;

public sealed class SettingsService(string settingsPath)
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public string SettingsPath { get; } = settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            throw new FileNotFoundException($"Settings file not found: {SettingsPath}", SettingsPath);
        }

        var settings = _deserializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
            ?? throw new InvalidDataException("settings.yaml is empty or invalid.");
        settings.Validate();
        return settings;
    }

    public void SaveMarkAsPlayed(string value)
    {
        if (value is not ("when_started" or "when_ended_or_skipped"))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("mark_as_played", value);
    }

    public void SavePlaybackSpeed(double value)
    {
        if (value is not (0.7 or 0.8 or 0.9 or 1.0 or 1.1 or 1.2 or 1.3))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("playback_speed", value.ToString("0.0#", CultureInfo.InvariantCulture));
    }

    public void SaveScanMode(bool value) =>
        SaveSetting("scan_mode", value ? "true" : "false");

    public void SaveScanCountdownSeconds(int value)
    {
        if (value is not (3 or 5 or 7 or 10 or 15 or 20 or 30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("scan_countdown_seconds", value.ToString(CultureInfo.InvariantCulture));
    }

    public void SaveAutoPlayNext(bool value) =>
        SaveSetting("auto_play_next", value ? "true" : "false");

    public void SaveShowArticleText(bool value) =>
        SaveSetting("show_article_text", value ? "true" : "false");

    public void SaveShowUnplayedOnly(bool value) =>
        SaveSetting("unplayed_only", value ? "true" : "false");

    public void SaveShowStarredOnly(bool value) =>
        SaveSetting("starred_only", value ? "true" : "false");

    public void SaveGroupArticlesByFeed(bool value) =>
        SaveSetting("group_articles_by_feed", value ? "true" : "false");

    public void SaveArticleSortOrder(string value)
    {
        if (value is not ("alphabetical_ascending" or "alphabetical_descending" or "newest_first" or "oldest_first"))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("article_sort_order", value);
    }

    public void SaveArticleListWidthRatio(double value)
    {
        if (!double.IsFinite(value) || value is < 0.2 or > 0.8)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("article_list_width_ratio", value.ToString("0.####", CultureInfo.InvariantCulture));
    }

    public void SaveAll(AppSettings settings)
    {
        settings.Validate();
        var contents = _serializer.Serialize(settings);
        var verifiedSettings = _deserializer.Deserialize<AppSettings>(contents)
            ?? throw new InvalidDataException("Could not verify the updated settings.");
        verifiedSettings.Validate();
        WriteSettings(contents);
    }

    private void SaveSetting(string key, string value)
    {
        var contents = File.ReadAllText(SettingsPath);
        var keyPattern = new Regex($@"(?m)^{Regex.Escape(key)}[ \t]*:[^\r\n]*(?<lineEnding>\r?\n|$)");
        var replacement = $"{key}: {value}";
        var foundKey = false;
        contents = keyPattern.Replace(contents, match =>
        {
            if (foundKey)
            {
                return string.Empty;
            }

            foundKey = true;
            return replacement + match.Groups["lineEnding"].Value;
        });
        if (!foundKey)
        {
            var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : Environment.NewLine;
            contents = contents.TrimEnd() + newline + replacement + newline;
        }

        WriteSettings(contents);
    }

    private void WriteSettings(string contents)
    {
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, SettingsPath, true);
    }
}
