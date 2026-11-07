using System.Text.Json;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class FeedUpdateStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly Dictionary<string, FeedCursor> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _statePath;

    public FeedUpdateStateService(IEnumerable<FeedConfiguration> feeds)
    {
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "anchorfeeds");
        Directory.CreateDirectory(stateDirectory);
        _statePath = Path.Combine(stateDirectory, "feed-update-state.json");
        Load();

        var hasChanges = false;
        var configuredFeeds = feeds.ToList();
        foreach (var feed in configuredFeeds)
        {
            if (_feeds.ContainsKey(feed.Url))
            {
                continue;
            }

            var cursor = SeedFromLibraryState(feed, configuredFeeds);
            _feeds[feed.Url] = cursor;
            hasChanges = true;
        }

        if (hasChanges)
        {
            Save();
        }
    }

    public DateTimeOffset? GetLastCheckedAt(string feedUrl) =>
        _feeds.TryGetValue(feedUrl, out var cursor) ? cursor.LastCheckedAt : null;

    public bool IsProcessed(
        string feedUrl,
        DateTimeOffset? publishedAt,
        string articleUrl,
        string title)
    {
        var cursor = GetCursor(feedUrl);
        var itemKey = CreateItemKey(articleUrl, title);
        if (publishedAt is not { } timestamp)
        {
            return cursor.UndatedItemKeys.Contains(itemKey);
        }

        if (cursor.LastItemTimestamp is not { } lastItemTimestamp)
        {
            return false;
        }

        var comparison = timestamp.CompareTo(lastItemTimestamp);
        return comparison < 0 ||
               (comparison == 0 && cursor.LatestTimestampItemKeys.Contains(itemKey));
    }

    public void MarkItemProcessed(
        string feedUrl,
        DateTimeOffset? publishedAt,
        string articleUrl,
        string title)
    {
        var cursor = GetCursor(feedUrl);
        var itemKey = CreateItemKey(articleUrl, title);
        if (publishedAt is not { } timestamp)
        {
            cursor.UndatedItemKeys.Add(itemKey);
        }
        else if (cursor.LastItemTimestamp is not { } lastItemTimestamp ||
                 timestamp > lastItemTimestamp)
        {
            cursor.LastItemTimestamp = timestamp;
            cursor.LatestTimestampItemKeys.Clear();
            cursor.LatestTimestampItemKeys.Add(itemKey);
        }
        else if (timestamp == lastItemTimestamp)
        {
            cursor.LatestTimestampItemKeys.Add(itemKey);
        }

        Save();
    }

    public void MarkFeedChecked(string feedUrl)
    {
        GetCursor(feedUrl).LastCheckedAt = DateTimeOffset.UtcNow;
        Save();
    }

    private FeedCursor GetCursor(string feedUrl)
    {
        if (_feeds.TryGetValue(feedUrl, out var cursor))
        {
            return cursor;
        }

        cursor = new FeedCursor();
        _feeds.Add(feedUrl, cursor);
        return cursor;
    }

    private FeedCursor SeedFromLibraryState(
        FeedConfiguration feed,
        IReadOnlyList<FeedConfiguration> configuredFeeds)
    {
        var libraryStatePath = Path.Combine(Path.GetDirectoryName(_statePath)!, "library-state.json");
        if (!File.Exists(libraryStatePath))
        {
            return new FeedCursor();
        }

        var libraryState = JsonSerializer.Deserialize<LibraryState>(
            File.ReadAllText(libraryStatePath),
            JsonOptions) ?? throw new InvalidDataException("The saved article library state is empty or invalid.");
        if (libraryState.Entries is null)
        {
            throw new InvalidDataException("The saved article library state has no article entries.");
        }

        var feedUri = new Uri(feed.Url);
        var sameHostFeedCount = configuredFeeds.Count(configuredFeed =>
            Uri.TryCreate(configuredFeed.Url, UriKind.Absolute, out var configuredUri) &&
            configuredUri.Host.Equals(feedUri.Host, StringComparison.OrdinalIgnoreCase));
        var entries = libraryState.Entries
            .Where(entry =>
                string.Equals(entry.FeedUrl, feed.Url, StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(entry.FeedUrl) &&
                 sameHostFeedCount == 1 &&
                 entry.Feed.Equals(feedUri.Host, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var cursor = new FeedCursor();
        foreach (var entry in entries.Where(entry => entry.PublishedAt is null))
        {
            cursor.UndatedItemKeys.Add(CreateItemKey(entry.ArticleUrl, entry.Title));
        }

        var datedEntries = entries
            .Where(entry => entry.PublishedAt.HasValue)
            .ToList();
        if (datedEntries.Count > 0)
        {
            var latestTimestamp = datedEntries.Max(entry => entry.PublishedAt!.Value);
            cursor.LastItemTimestamp = latestTimestamp;
            foreach (var entry in datedEntries.Where(entry => entry.PublishedAt == latestTimestamp))
            {
                cursor.LatestTimestampItemKeys.Add(CreateItemKey(entry.ArticleUrl, entry.Title));
            }
        }

        return cursor;
    }

    private static string CreateItemKey(string articleUrl, string title) =>
        string.IsNullOrWhiteSpace(articleUrl)
            ? title.Trim()
            : articleUrl.Trim();

    private void Load()
    {
        if (!File.Exists(_statePath))
        {
            return;
        }

        var state = JsonSerializer.Deserialize<FeedUpdateStateDocument>(
            File.ReadAllText(_statePath),
            JsonOptions) ?? throw new InvalidDataException("The feed update state is empty or invalid.");
        if (state.Feeds is null)
        {
            throw new InvalidDataException("The feed update state has no feed entries.");
        }

        foreach (var (feedUrl, cursor) in state.Feeds)
        {
            if (cursor is null)
            {
                throw new InvalidDataException($"The feed update state for '{feedUrl}' is empty.");
            }

            cursor.LatestTimestampItemKeys = new(
                cursor.LatestTimestampItemKeys ?? [],
                StringComparer.OrdinalIgnoreCase);
            cursor.UndatedItemKeys = new(
                cursor.UndatedItemKeys ?? [],
                StringComparer.OrdinalIgnoreCase);
            _feeds[feedUrl] = cursor;
        }
    }

    private void Save()
    {
        var contents = JsonSerializer.Serialize(new FeedUpdateStateDocument { Feeds = _feeds }, JsonOptions);
        var tempPath = _statePath + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, _statePath, true);
    }

    private sealed class FeedUpdateStateDocument
    {
        public Dictionary<string, FeedCursor> Feeds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public FeedUpdateStateDocument()
        {
        }
    }

    private sealed class FeedCursor
    {
        public DateTimeOffset? LastItemTimestamp { get; set; }
        public DateTimeOffset? LastCheckedAt { get; set; }
        public HashSet<string> LatestTimestampItemKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UndatedItemKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public FeedCursor()
        {
        }
    }
}
