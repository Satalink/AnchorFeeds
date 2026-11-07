using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Models;

public sealed class FeedConfiguration
{
    private static readonly Regex IntervalPattern = new(
        @"^(?<amount>[1-9][0-9]{0,4})\s+(?<unit>minutes?|hours?|days?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<FeedOption> GroupOptions { get; } =
    [
        new("general_news", "News & Current Affairs"),
        new("technology", "Technology & Computing"),
        new("software_dev", "Software Development & IT"),
        new("financial_markets", "Financial & Markets"),
        new("business_industry", "Business & Industry"),
        new("science_space", "Science, Space & Engineering"),
        new("podcasts_audio", "Podcasts & Audio"),
        new("video_media", "Video & Multimedia"),
        new("media_entertainment", "Entertainment & Pop Culture"),
        new("gaming", "Gaming & Esports"),
        new("sports", "Sports & Athletics"),
        new("automotive", "Automotive & Transportation"),
        new("outdoors_lifestyle", "Outdoors & Lifestyle"),
        new("blogs_commentary", "Blogs, Essays & Commentary"),
        new("system_alerts", "Releases & System Alerts"),
        new("other", "Other / Uncategorized")
    ];

    public static IReadOnlyList<FeedOption> UpdatePeriodOptions { get; } =
    [
        new("inherit", "Use global interval"),
        new("Manual only", "Manual only"),
        new("15 minutes", "15 minutes"),
        new("30 minutes", "30 minutes"),
        new("1 hour", "1 hour"),
        new("2 hours", "2 hours"),
        new("6 hours", "6 hours"),
        new("12 hours", "12 hours"),
        new("1 day", "1 day")
    ];

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = string.Empty;

    [YamlMember(Alias = "group")]
    public string Group { get; set; } = "other";

    [YamlMember(Alias = "update_period")]
    public string UpdatePeriod { get; set; } = "inherit";

    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "last_updated_utc")]
    public object? LegacyLastUpdatedUtc { get; set; }

    [YamlIgnore]
    public TimeSpan? UpdatePeriodValue { get; private set; }

    public static FeedConfiguration FromLegacyUrl(string url)
    {
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;
        return new FeedConfiguration
        {
            Name = name,
            Url = url,
            Group = "other",
            UpdatePeriod = "inherit"
        };
    }

    public FeedConfiguration Copy() => new()
    {
        Name = Name,
        Url = Url,
        Group = Group,
        UpdatePeriod = UpdatePeriod,
        Enabled = Enabled
    };

    public TimeSpan? GetEffectiveUpdatePeriod(TimeSpan? globalPeriod) =>
        UpdatePeriod.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            ? globalPeriod
            : UpdatePeriodValue;

    public void Validate()
    {
        LegacyLastUpdatedUtc = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException("Each feed must have a name.");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"Invalid feed link for '{Name}': {Url}");
        }

        if (!GroupOptions.Any(option => option.Value.Equals(Group, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"The group for feed '{Name}' is not supported.");
        }

        if (!UpdatePeriodOptions.Any(option =>
                option.Value.Equals(UpdatePeriod, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"The update period for feed '{Name}' is not supported.");
        }

        UpdatePeriodValue = ParseUpdatePeriod(UpdatePeriod);
    }

    private static TimeSpan? ParseUpdatePeriod(string? updatePeriod)
    {
        if (string.Equals(updatePeriod, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(updatePeriod, "Manual only", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = IntervalPattern.Match(updatePeriod ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException(
                "A feed update period must be Use global interval, Manual only, or an interval from 15 minutes to 1 day.");
        }

        var amount = double.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        return char.ToLowerInvariant(match.Groups["unit"].Value[0]) switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount)
        };
    }
}

public sealed record FeedOption(string Value, string Label);
