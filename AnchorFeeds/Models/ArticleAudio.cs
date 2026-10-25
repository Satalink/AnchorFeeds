using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AnchorFeeds.Models;

public sealed class ArticleAudio : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _feed = string.Empty;
    private string _articleUrl = string.Empty;
    private string _imageUrl = string.Empty;
    private string _sourceLogoUrl = string.Empty;
    private string _audioText = string.Empty;
    private bool _isPlayed;
    private bool _isStarred;
    private DateTimeOffset? _publishedAt;

    public required string FilePath { get; init; }

    public string TitleAudioPath { get; set; } = string.Empty;

    public string CachedImagePath { get; set; } = string.Empty;

    public string ArticleFolderPath { get; set; } = string.Empty;

    public bool HasRedundantBodyAudio
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TitleAudioPath) ||
                !File.Exists(TitleAudioPath) ||
                !File.Exists(FilePath))
            {
                return false;
            }

            var titleSize = new FileInfo(TitleAudioPath).Length;
            var bodySize = new FileInfo(FilePath).Length;
            var largerSize = Math.Max(titleSize, bodySize);
            return largerSize > 0 && Math.Abs(titleSize - bodySize) / (double)largerSize <= 0.03;
        }
    }

    public bool HasUsableBodyAudio => File.Exists(FilePath) && !HasRedundantBodyAudio;

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
        }
    }

    public string DisplayDateTime =>
        (PublishedAt?.LocalDateTime ?? CreatedAt).ToString("g", CultureInfo.CurrentCulture);

    public DateTime SortDate => PublishedAt?.LocalDateTime ?? CreatedAt;

    public DateTime? PlayedAt { get; set; }
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
