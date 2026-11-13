using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AnchorFeeds.Models;

public sealed class ArticleAudio : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _feed = string.Empty;
    private string _feedUrl = string.Empty;
    private string _feedName = string.Empty;
    private string _articleUrl = string.Empty;
    private string _imageUrl = string.Empty;
    private string _sourceLogoUrl = string.Empty;
    private string _audioText = string.Empty;
    private string _articleContentHtml = string.Empty;
    private bool _isPlayed;
    private string _audioError = string.Empty;
    private bool _isStarred;
    private DateTimeOffset? _publishedAt;
    private DateTimeOffset? _lastViewedAt;
    private DateTimeOffset? _lastListenedAt;

    public required string FilePath { get; init; }

    public string TitleAudioPath { get; set; } = string.Empty;

    public string TitleAudioEngine { get; set; } = string.Empty;

    public string BodyAudioEngine { get; set; } = string.Empty;

    public List<string> Categories { get; set; } = [];

    public IReadOnlyList<KeywordPill> MatchedKeywords { get; private set; } = [];

    internal void SetMatchedPills(
        IReadOnlyList<string> favoriteKeywords,
        IReadOnlyList<string> starKeywords,
        IReadOnlyList<string> favoriteCategories)
    {
        var matches = favoriteKeywords.Select(phrase => new KeywordPill(phrase, KeywordPillKind.FavoriteKeyword))
            .Concat(starKeywords.Select(phrase => new KeywordPill(phrase, KeywordPillKind.StarKeyword)))
            .Concat(favoriteCategories.Select(category => new KeywordPill(category, KeywordPillKind.FavoriteCategory)))
            .ToList();
        if (!MatchedKeywords.SequenceEqual(matches))
        {
            MatchedKeywords = matches;
            OnPropertyChanged(nameof(MatchedKeywords));
        }
    }

    private bool _isFavoriteCategory;

    public bool IsFavoriteCategory
    {
        get => _isFavoriteCategory;
        set
        {
            if (_isFavoriteCategory == value)
                return;
            _isFavoriteCategory = value;
            OnPropertyChanged();
        }
    }

    private string _cachedImagePath = string.Empty;

    public string CachedImagePath
    {
        get => _cachedImagePath;
        set
        {
            if (_cachedImagePath == value)
            {
                return;
            }
            _cachedImagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailUri));
        }
    }

    public Uri? ThumbnailUri
    {
        get
        {
            var candidates = new[] { CachedImagePath, ImageUrl, SourceLogoUrl };
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate))
                    {
                        return new Uri(candidate, UriKind.Absolute);
                    }
                    continue;
                }

                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    return uri;
                }
            }

            return null;
        }
    }

    public string ArticleFolderPath { get; set; } = string.Empty;

    public bool HasRedundantBodyAudio
    {
        get
        {
            var titleText = Regex.Replace(Title.Trim(), @"\s+", " ");
            var bodyText = Regex.Replace(AudioText.Trim(), @"\s+", " ");
            return bodyText.Length > 0 &&
                   string.Equals(titleText, bodyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool HasUsableBodyAudio => File.Exists(FilePath) && !HasRedundantBodyAudio;

    public string AudioError
    {
        get => _audioError;
        set
        {
            value ??= string.Empty;
            if (_audioError == value)
            {
                return;
            }

            _audioError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAudioError));
        }
    }

    public bool HasAudioError => !string.IsNullOrWhiteSpace(_audioError);

    public required string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }
            _title = value;
            OnPropertyChanged();
        }
    }

    public required string Feed
    {
        get => _feed;
        set
        {
            if (_feed == value)
            {
                return;
            }
            _feed = value;
            OnPropertyChanged();
        }
    }

    public string FeedName
    {
        get => _feedName;
        set
        {
            if (_feedName == value)
            {
                return;
            }
            _feedName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FeedDisplayName));
        }
    }

    public string FeedUrl
    {
        get => _feedUrl;
        set
        {
            if (_feedUrl == value)
            {
                return;
            }
            _feedUrl = value;
            OnPropertyChanged();
        }
    }

    public string FeedDisplayName => string.IsNullOrWhiteSpace(FeedName) ? Feed : FeedName;

    public string ArticleUrl
    {
        get => _articleUrl;
        set
        {
            if (_articleUrl == value)
            {
                return;
            }
            _articleUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasArticleUrl));
        }
    }

    public string ImageUrl
    {
        get => _imageUrl;
        set
        {
            if (_imageUrl == value)
            {
                return;
            }
            _imageUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailUri));
        }
    }

    public string SourceLogoUrl
    {
        get => _sourceLogoUrl;
        set
        {
            if (_sourceLogoUrl == value)
            {
                return;
            }
            _sourceLogoUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailUri));
            OnPropertyChanged(nameof(SourceThumbnailUri));
        }
    }

    public Uri? SourceThumbnailUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceLogoUrl))
            {
                return null;
            }

            if (Path.IsPathRooted(SourceLogoUrl))
            {
                return File.Exists(SourceLogoUrl)
                    ? new Uri(SourceLogoUrl, UriKind.Absolute)
                    : null;
            }

            return Uri.TryCreate(SourceLogoUrl, UriKind.Absolute, out var uri) &&
                   uri.Scheme is "http" or "https"
                ? uri
                : null;
        }
    }

    public string AudioText
    {
        get => _audioText;
        set
        {
            if (_audioText == value)
            {
                return;
            }
            _audioText = value;
            OnPropertyChanged();
        }
    }

    public string ArticleContentHtml
    {
        get => _articleContentHtml;
        set
        {
            if (_articleContentHtml == value)
            {
                return;
            }
            _articleContentHtml = value;
            _mediaCountHtml = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMultipleMedia));
        }
    }

    private string? _mediaCountHtml;
    private bool _hasMultipleMedia;

    // True when the article would show a carousel (main image plus more media, or 2+ media items).
    public bool HasMultipleMedia
    {
        get
        {
            var html = _articleContentHtml;
            if (string.IsNullOrEmpty(html))
            {
                return false;
            }

            if (!ReferenceEquals(html, _mediaCountHtml))
            {
                var extras = Services.ArticleMediaExtractor.Extract(html, ImageUrl, SourceLogoUrl).Count;
                _hasMultipleMedia = extras + (string.IsNullOrWhiteSpace(ImageUrl) ? 0 : 1) >= 2;
                _mediaCountHtml = html;
            }

            return _hasMultipleMedia;
        }
    }

    public DateTime CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt
    {
        get => _publishedAt;
        set
        {
            if (_publishedAt == value)
            {
                return;
            }

            _publishedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayDateTime));
            OnPropertyChanged(nameof(RelativeDateTime));
        }
    }

    public string DisplayDateTime =>
        (PublishedAt?.LocalDateTime ?? CreatedAt).ToString("g", CultureInfo.CurrentCulture);

    public string RelativeDateTime
    {
        get
        {
            var elapsed = DateTime.Now - (PublishedAt?.LocalDateTime ?? CreatedAt);
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var (amount, unit) = elapsed.TotalMinutes < 1
                ? (Math.Max(1, (int)elapsed.TotalSeconds), "second")
                : elapsed.TotalMinutes < 60
                ? ((int)elapsed.TotalMinutes, "minute")
                : elapsed.TotalHours < 24
                    ? ((int)elapsed.TotalHours, "hour")
                    : elapsed.TotalDays < 30
                        ? ((int)elapsed.TotalDays, "day")
                        : elapsed.TotalDays < 365
                            ? ((int)(elapsed.TotalDays / 30), "month")
                            : ((int)(elapsed.TotalDays / 365), "year");
            return $"{amount} {unit}{(amount == 1 ? string.Empty : "s")} ago";
        }
    }

    public TimeSpan GetNextRelativeDateTimeChange(DateTime now)
    {
        var elapsed = now - (PublishedAt?.LocalDateTime ?? CreatedAt);
        var elapsedSeconds = Math.Max(0, elapsed.TotalSeconds);
        double nextChangeSeconds;
        if (elapsedSeconds < 60)
        {
            var displayedSeconds = Math.Max(1, (int)elapsedSeconds);
            nextChangeSeconds = displayedSeconds + 1;
        }
        else
        {
            var unitSeconds = elapsedSeconds < 60 * 60 ? 60
                : elapsedSeconds < 24 * 60 * 60 ? 60 * 60
                : elapsedSeconds < 30 * 24 * 60 * 60 ? 24 * 60 * 60
                : elapsedSeconds < 365 * 24 * 60 * 60 ? 30 * 24 * 60 * 60
                : 365 * 24 * 60 * 60;
            nextChangeSeconds = (Math.Floor(elapsedSeconds / unitSeconds) + 1) * unitSeconds;
        }

        return TimeSpan.FromSeconds(Math.Max(0.1, nextChangeSeconds - elapsedSeconds));
    }

    public void RefreshRelativeDateTime() => OnPropertyChanged(nameof(RelativeDateTime));

    public DateTime SortDate => PublishedAt?.LocalDateTime ?? CreatedAt;

    public DateTime? PlayedAt { get; set; }

    public DateTimeOffset? LastViewedAt
    {
        get => _lastViewedAt;
        set
        {
            if (_lastViewedAt == value)
            {
                return;
            }

            _lastViewedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBeenViewed));
            OnPropertyChanged(nameof(LastActivityAt));
        }
    }

    public DateTimeOffset? LastListenedAt
    {
        get => _lastListenedAt;
        set
        {
            if (_lastListenedAt == value)
            {
                return;
            }

            _lastListenedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBeenListenedTo));
            OnPropertyChanged(nameof(LastActivityAt));
        }
    }

    public bool HasBeenViewed => LastViewedAt.HasValue;
    public bool HasBeenListenedTo => LastListenedAt.HasValue;
    public DateTimeOffset? LastActivityAt =>
        LastViewedAt > LastListenedAt ? LastViewedAt : LastListenedAt;

    public bool HasArticleUrl => Uri.TryCreate(ArticleUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public bool IsPlayed
    {
        get => _isPlayed;
        set
        {
            if (_isPlayed == value)
            {
                return;
            }

            _isPlayed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayedLabel));
        }
    }

    public string PlayedLabel => IsPlayed ? "Played" : "Unplayed";

    public bool IsStarred
    {
        get => _isStarred;
        set
        {
            if (_isStarred == value)
            {
                return;
            }

            _isStarred = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StarLabel));
        }
    }

    public string StarLabel => IsStarred ? "★" : "☆";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
