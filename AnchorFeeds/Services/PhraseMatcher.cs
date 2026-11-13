using System.Text.RegularExpressions;

namespace AnchorFeeds.Services;

/// <summary>Case-insensitive whole-word/phrase matching against article text.</summary>
public sealed class PhraseMatcher
{
    private readonly Regex? _regex;
    private readonly List<(string Phrase, string Pattern)> _phrases;
    private List<(string Phrase, Regex Regex)>? _individualMatchers;

    public PhraseMatcher(IEnumerable<string>? phrases)
    {
        _phrases = (phrases ?? [])
            .Select(phrase => phrase?.Trim() ?? string.Empty)
            .Where(phrase => phrase.Length > 0)
            .Select(phrase => string.Join(" ", phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(phrase => (phrase, string.Join(@"\s+", phrase.Split(' ').Select(Regex.Escape))))
            .ToList();
        if (_phrases.Count > 0)
        {
            _regex = new Regex(
                @"(?<!\w)(?:" + string.Join("|", _phrases.Select(phrase => phrase.Pattern)) + @")(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromSeconds(2));
        }
    }

    public bool IsEmpty => _regex is null;

    public bool IsMatch(params string?[] texts) =>
        _regex is not null && texts.Any(text => !string.IsNullOrEmpty(text) && _regex.IsMatch(text));

    public IReadOnlyList<string> FindMatches(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        _individualMatchers ??= _phrases.Select(phrase => (
            phrase.Phrase,
            new Regex(@"(?<!\w)" + phrase.Pattern + @"(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromSeconds(2)))).ToList();
        return _individualMatchers.Where(matcher => matcher.Regex.IsMatch(text))
            .Select(matcher => matcher.Phrase)
            .ToList();
    }
}
