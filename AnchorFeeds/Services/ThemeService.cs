using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace AnchorFeeds.Services;

public static class ThemeService
{
    private const string SystemDesktopTheme = "system_desktop";
    private static bool _systemThemeSelected;

    private sealed record ThemePalette(
        string WindowBackground,
        string SurfaceBackground,
        string ElevatedBackground,
        string TextPrimary,
        string TextSecondary,
        string Border,
        string Accent,
        string AccentText,
        string ImageBackground,
        string ArticleTextBackground,
        string ArticleTextForeground,
        string SelectedBackground,
        string MenuHoverBackground,
        string MenuHoverText);

    private static readonly IReadOnlyDictionary<string, ThemePalette> Palettes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["light"] = new(
                "#FFF4F6F8", "#FFFFFFFF", "#FFF8F9FA", "#FF20252B", "#FF606B78",
                "#FFE2E5E9", "#FF2563A6", "#FFFFFFFF", "#FFF3F5F7", "#FFF8F9FA",
                "#FF38424D", "#FFD5E5F5", "#FF2563A6", "#FFFFFFFF"),
            ["dark"] = new(
                "#FF17191D", "#FF22252A", "#FF2A2E34", "#FFF1F3F5", "#FFB0B8C2",
                "#FF414750", "#FF4C8DCE", "#FFFFFFFF", "#FF2A2E34", "#FF25292F",
                "#FFE2E6EB", "#FF31485E", "#FF5B96D1", "#FFFFFFFF"),
            ["charcoal"] = new(
                "#FF202124", "#FF292B2F", "#FF33363B", "#FFE8EAED", "#FFB8BCC2",
                "#FF4A4D52", "#FF8AB4F8", "#FF202124", "#FF33363B", "#FF2E3035",
                "#FFE1E3E6", "#FF3B4655", "#FF8AB4F8", "#FF202124")
        };

    static ThemeService()
    {
        SystemParameters.StaticPropertyChanged += (_, _) =>
        {
            if (_systemThemeSelected && Application.Current is { } application)
            {
                application.Dispatcher.InvokeAsync(() => Apply(SystemDesktopTheme));
            }
        };
    }

    public static void Apply(string theme)
    {
        _systemThemeSelected = theme.Equals(SystemDesktopTheme, StringComparison.OrdinalIgnoreCase);
        if (_systemThemeSelected)
        {
            ApplyPalette(GetSystemDesktopPalette());
            return;
        }

        if (!Palettes.TryGetValue(theme, out var palette))
        {
            throw new InvalidDataException($"Unsupported appearance theme: {theme}");
        }

        ApplyPalette(palette);
    }

    private static void ApplyPalette(ThemePalette palette)
    {
        var resources = Application.Current.Resources;
        resources["WindowBackgroundBrush"] = Brush(palette.WindowBackground);
        resources["SurfaceBackgroundBrush"] = Brush(palette.SurfaceBackground);
        resources["ElevatedBackgroundBrush"] = Brush(palette.ElevatedBackground);
        resources["TextPrimaryBrush"] = Brush(palette.TextPrimary);
        resources["TextSecondaryBrush"] = Brush(palette.TextSecondary);
        resources["BorderBrush"] = Brush(palette.Border);
        resources["AccentBrush"] = Brush(palette.Accent);
        resources["AccentTextBrush"] = Brush(palette.AccentText);
        resources["ImageBackgroundBrush"] = Brush(palette.ImageBackground);
        resources["ArticleTextBackgroundBrush"] = Brush(palette.ArticleTextBackground);
        resources["ArticleTextForegroundBrush"] = Brush(palette.ArticleTextForeground);
        resources["SelectedBackgroundBrush"] = Brush(palette.SelectedBackground);
        resources["MenuHoverBackgroundBrush"] = Brush(palette.MenuHoverBackground);
        resources["MenuHoverTextBrush"] = Brush(palette.MenuHoverText);
        resources[SystemColors.HighlightBrushKey] = Brush(palette.MenuHoverBackground);
        resources[SystemColors.HighlightTextBrushKey] = Brush(palette.MenuHoverText);
    }

    private static ThemePalette GetSystemDesktopPalette()
    {
        if (SystemParameters.HighContrast)
        {
            return new ThemePalette(
                ColorString(SystemColors.WindowColor),
                ColorString(SystemColors.WindowColor),
                ColorString(SystemColors.ControlColor),
                ColorString(SystemColors.WindowTextColor),
                ColorString(SystemColors.GrayTextColor),
                ColorString(SystemColors.WindowFrameColor),
                ColorString(SystemColors.HighlightColor),
                ColorString(SystemColors.HighlightTextColor),
                ColorString(SystemColors.ControlColor),
                ColorString(SystemColors.WindowColor),
                ColorString(SystemColors.WindowTextColor),
                ColorString(SystemColors.HighlightColor),
                ColorString(SystemColors.HighlightColor),
                ColorString(SystemColors.HighlightTextColor));
        }

        var appsUseLightTheme = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            null) is not int lightTheme || lightTheme != 0;
        var accent = ColorString(SystemColors.HighlightColor);
        var accentText = ColorString(SystemColors.HighlightTextColor);

        return appsUseLightTheme
            ? new ThemePalette(
                "#FFF3F3F3",
                ColorString(SystemColors.WindowColor),
                ColorString(SystemColors.ControlColor),
                ColorString(SystemColors.WindowTextColor),
                ColorString(SystemColors.GrayTextColor),
                ColorString(SystemColors.WindowFrameColor),
                accent,
                accentText,
                ColorString(SystemColors.ControlColor),
                ColorString(SystemColors.WindowColor),
                ColorString(SystemColors.WindowTextColor),
                accent,
                accent,
                accentText)
            : new ThemePalette(
                "#FF202020",
                "#FF202020",
                "#FF2D2D2D",
                "#FFF1F1F1",
                "#FFC5C5C5",
                "#FF454545",
                accent,
                accentText,
                "#FF2D2D2D",
                "#FF202020",
                "#FFF1F1F1",
                accent,
                accent,
                accentText);
    }

    private static string ColorString(Color color) => color.ToString();

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
