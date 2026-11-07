using System.Text.RegularExpressions;

namespace AnchorFeeds.Services;

/// <summary>Case-insensitive whole-word/phrase matching against article text.</summary>
public sealed class PhraseMatcher
{
    private readonly Regex? _regex;

    public PhraseMatcher(IEnumerable<string>? phrases)
    {
        var patterns = (phrases ?? [])
            .Select(phrase => phrase?.Trim() ?? string.Empty)
            .Where(phrase => phrase.Length > 0)
            .Select(phrase => string.Join(
                @"\s+",
                phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (patterns.Count > 0)
        {
            _regex = new Regex(
                @"(?<!\w)(?:" + string.Join("|", patterns) + @")(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromSeconds(2));
        }
    }

    public bool IsEmpty => _regex is null;

    public bool IsMatch(params string?[] texts) =>
        _regex is not null && texts.Any(text => !string.IsNullOrEmpty(text) && _regex.IsMatch(text));
}
