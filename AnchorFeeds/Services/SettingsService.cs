using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.Win32;
using AnchorFeeds.Models;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Services;

public sealed class SettingsService(string settingsPath)
{
    private const string RegistryPath = @"Software\Satalink\AnchorFeeds";
    private const string RegistryModeValue = "PortableMode";
    private const string RegistrySettingsValue = "SettingsYaml";
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public string SettingsPath { get; } = settingsPath;

    public AppSettings Load()
    {
        using var registrySettings = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (registrySettings?.GetValueNames().Contains(RegistrySettingsValue, StringComparer.OrdinalIgnoreCase) == true)
        {
            var contents = registrySettings.GetValue(RegistrySettingsValue) as string
                ?? throw new InvalidDataException(
                    "AnchorFeeds settings exist in the current user's registry but are not stored as text.");
            var settings = DeserializeSettings(contents);
            settings.PortableMode = false;
            RemoveSettingsFile();
            return settings;
        }

        if (!File.Exists(SettingsPath))
        {
            throw new FileNotFoundException($"Settings file not found: {SettingsPath}", SettingsPath);
        }

        return DeserializeSettings(File.ReadAllText(SettingsPath));
    }

    private AppSettings DeserializeSettings(string contents)
    {
        var settings = _deserializer.Deserialize<AppSettings>(contents)
            ?? throw new InvalidDataException("settings.yaml is empty or invalid.");
        if (!string.IsNullOrWhiteSpace(settings.FinalOutput) &&
            settings.FinalOutput.Contains("{feed}", StringComparison.Ordinal))
        {
            settings.OutputDirectory = ArticleFileNaming.ResolveLegacyOutputDirectory(
                Path.GetDirectoryName(SettingsPath) ?? AppContext.BaseDirectory,
                settings.FinalOutput);
        }

        settings.FinalOutput = ArticleFileNaming.BuildFinalOutputTemplate(settings.OutputDirectory);
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

    public void SaveScanMode(bool value) =>
        SaveSetting("scan_mode", value ? "true" : "false");

    public void SaveScanCountdownSeconds(int value)
    {
        if (value is not (0 or 3 or 5 or 7 or 10 or 15 or 20 or 30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("scan_countdown_seconds", value.ToString(CultureInfo.InvariantCulture));
    }

    public void SaveAutoPlayNext(bool value) =>
        SaveSetting("auto_play_next", value ? "true" : "false");

    public void SaveAudioMuted(bool value) =>
        SaveSetting("audio_muted", value ? "true" : "false");

    public void SaveShowArticleText(bool value) =>
        SaveSetting("show_article_text", value ? "true" : "false");

    public void SaveShowMatchingKeywords(bool value) =>
        SaveSetting("show_matching_keywords", value ? "true" : "false");

    public void SaveShowUnplayedOnly(bool value) =>
        SaveSetting("unplayed_only", value ? "true" : "false");

    public void SaveShowStarredOnly(bool value) =>
        SaveSetting("starred_only", value ? "true" : "false");

    public void SaveShowFavoriteCategoriesOnly(bool value) =>
        SaveSetting("favorite_categories_only", value ? "true" : "false");

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

    public void SaveArticleImageHeightRatio(double value)
    {
        if (!double.IsFinite(value) || value is < 0.2 or > 0.8)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SaveSetting("article_image_height_ratio", value.ToString("0.####", CultureInfo.InvariantCulture));
    }

    public void SaveAll(AppSettings settings)
    {
        settings.Validate();
        var contents = _serializer.Serialize(settings);
        var verifiedSettings = DeserializeSettings(contents);
        if (verifiedSettings.PortableMode)
        {
            WriteSettings(contents);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        }
        else
        {
            using var registrySettings = Registry.CurrentUser.CreateSubKey(RegistryPath);
            registrySettings.SetValue(RegistrySettingsValue, contents, RegistryValueKind.String);
            registrySettings.SetValue(RegistryModeValue, 0, RegistryValueKind.DWord);
            RemoveSettingsFile();
        }
    }

    private void SaveSetting(string key, string value)
    {
        var portableMode = IsPortableMode();
        var contents = portableMode
            ? File.ReadAllText(SettingsPath)
            : ReadRegistrySettings();
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

        var verifiedSettings = DeserializeSettings(contents);
        if (portableMode)
        {
            WriteSettings(contents);
        }
        else
        {
            using var registrySettings = Registry.CurrentUser.CreateSubKey(RegistryPath);
            registrySettings.SetValue(RegistrySettingsValue, contents, RegistryValueKind.String);
        }
    }

    private bool IsPortableMode() =>
        !HasRegistrySettings();

    private bool HasRegistrySettings()
    {
        using var registrySettings = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return registrySettings?.GetValueNames()
            .Contains(RegistrySettingsValue, StringComparer.OrdinalIgnoreCase) == true;
    }

    private static string ReadRegistrySettings()
    {
        using var registrySettings = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (registrySettings?.GetValueNames()
                .Contains(RegistrySettingsValue, StringComparer.OrdinalIgnoreCase) != true)
        {
            throw new InvalidDataException(
                "AnchorFeeds settings were not found in the current user's registry.");
        }

        return registrySettings.GetValue(RegistrySettingsValue) as string
            ?? throw new InvalidDataException(
                "AnchorFeeds settings exist in the current user's registry but are not stored as text.");
    }

    private void RemoveSettingsFile()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        File.Delete(SettingsPath);
    }

    private void WriteSettings(string contents)
    {
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, SettingsPath, true);
    }
}
