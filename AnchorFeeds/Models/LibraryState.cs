namespace AnchorFeeds.Models;

public sealed class LibraryState
{
    public List<LibraryEntryState> Entries { get; set; } = [];
}

public sealed class LibraryEntryState
{
    public string FilePath { get; set; } = string.Empty;
    public string TitleAudioPath { get; set; } = string.Empty;
    public string TitleAudioEngine { get; set; } = string.Empty;
    public string BodyAudioEngine { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public string CachedImagePath { get; set; } = string.Empty;
    public string ArticleFolderPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Feed { get; set; } = string.Empty;
    public string FeedName { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string ArticleUrl { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string SourceLogoUrl { get; set; } = string.Empty;
    public string AudioText { get; set; } = string.Empty;
    public string ArticleContentHtml { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public bool IsPlayed { get; set; }
    public bool IsStarred { get; set; }
    public DateTime? PlayedAt { get; set; }
    public DateTimeOffset? LastViewedAt { get; set; }
    public DateTimeOffset? LastListenedAt { get; set; }
}
