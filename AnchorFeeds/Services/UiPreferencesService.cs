using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnchorFeeds.Services;

public sealed class UiPreferencesService
{
    public const double DefaultArticleListWidthRatio = 0.4;

    private static readonly Regex LegacyArticleListWidthPattern = new(
        @"(?m)^article_list_width_ratio[ \t]*:[ \t]*(?<value>[^\r\n]+)(?<lineEnding>\r?\n|$)");
    private readonly string _preferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AncoredNewsFeeds",
        "ui-preferences.json");

    public double LoadArticleListWidthRatio(string settingsPath)
    {
        var settingsContents = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : string.Empty;
        if (!LegacyArticleListWidthPattern.IsMatch(settingsContents) && File.Exists(_preferencesPath))
        {
            var preferences = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_preferencesPath))
                ?? throw new InvalidDataException($"Could not read UI preferences from {_preferencesPath}.");
            settingsContents = AppendRatioSetting(
                settingsContents,
                ValidateRatio(preferences.ArticleListWidthRatio));
            WriteSettings(settingsPath, settingsContents);
        }

        return LoadLegacyRatio(settingsPath) ?? DefaultArticleListWidthRatio;
    }

    private static double? LoadLegacyRatio(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        var match = LegacyArticleListWidthPattern.Match(File.ReadAllText(settingsPath));
        if (!match.Success)
        {
            return null;
        }

        if (!double.TryParse(
                match.Groups["value"].Value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var ratio))
        {
            throw new InvalidDataException("The legacy article_list_width_ratio setting is invalid.");
        }

        return ValidateRatio(ratio);
    }

    private static string AppendRatioSetting(string contents, double ratio)
    {
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : Environment.NewLine;
        return contents.TrimEnd() + newline + "article_list_width_ratio: " +
            ratio.ToString("0.####", CultureInfo.InvariantCulture) + newline;
    }

    private static void WriteSettings(string settingsPath, string contents)
    {
        var tempPath = settingsPath + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, settingsPath, true);
    }

    private static double ValidateRatio(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio is < 0.2 or > 0.8)
        {
            throw new InvalidDataException("The saved article-list width must be between 0.2 and 0.8.");
        }

        return ratio;
    }

    private sealed class UiPreferences
    {
        public double ArticleListWidthRatio { get; set; } = DefaultArticleListWidthRatio;
    }
}
