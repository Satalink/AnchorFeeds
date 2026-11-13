using System.Collections.ObjectModel;
using System.Text.Json;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class AudioLibraryService
{
    public const string ErrorMarkerFileName = "body.error.txt";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AppSettings _settings;
    private readonly FeedCategoryPreferencesService _categoryPreferences;
    private readonly string _outputDirectory;
    private readonly string _statePath;
    private readonly Dictionary<string, LibraryEntryState> _savedEntries;
    private readonly System.Text.RegularExpressions.Regex _filenameRegex;
    private readonly System.Text.RegularExpressions.Regex _articleFolderRegex;

    public AudioLibraryService(AppSettings settings, string applicationDirectory, FeedCategoryPreferencesService categoryPreferences)
    {
        _settings = settings;
        _categoryPreferences = categoryPreferences;
        _outputDirectory = ArticleFileNaming.ResolveOutputDirectory(applicationDirectory, settings.OutputDirectory);
        _filenameRegex = ArticleFileNaming.BuildFilenameRegex(
            ArticleFileNaming.GetLegacyFilenameTemplate());
        _articleFolderRegex = ArticleFileNaming.BuildFilenameRegex(
            ArticleFileNaming.GetArticleFolderTemplate());

        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "anchorfeeds");
        Directory.CreateDirectory(stateDirectory);
        _statePath = Path.Combine(stateDirectory, "library-state.json");
        _savedEntries = LoadSavedEntries();
    }

    public ObservableCollection<ArticleAudio> Items { get; } = [];

    public string OutputDirectory => _outputDirectory;

    public void Refresh()
    {
        Refresh(removeExpiredFiles: true);
    }

    public int Rebuild()
    {
        Directory.CreateDirectory(_outputDirectory);
        var removedCount = 0;

        foreach (var articleFolder in Directory.EnumerateDirectories(_outputDirectory).ToList())
        {
            if (!_articleFolderRegex.IsMatch(Path.GetFileName(articleFolder)))
            {
                continue;
            }

            Directory.Delete(articleFolder, recursive: true);
            removedCount++;
        }

        foreach (var audioFile in Directory.EnumerateFiles(_outputDirectory).ToList())
        {
            var fileName = Path.GetFileName(audioFile);
            if (!_filenameRegex.IsMatch(fileName) ||
                !Path.GetExtension(fileName).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(audioFile);
            var mediaFolder = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(audioFile));
            if (_articleFolderRegex.IsMatch(Path.GetFileName(mediaFolder)) && Directory.Exists(mediaFolder))
            {
                Directory.Delete(mediaFolder, recursive: true);
            }
            removedCount++;
        }

        Items.Clear();
        _savedEntries.Clear();
        Save();
        return removedCount;
    }

    public int Prune()
    {
        var removed = RemoveExpiredFiles();
        Refresh(removeExpiredFiles: false);
        return removed;
    }

    private void Refresh(bool removeExpiredFiles)
    {
        Directory.CreateDirectory(_outputDirectory);
        if (removeExpiredFiles)
        {
            RemoveExpiredFiles();
        }

        var existing = new Dictionary<string, ArticleAudio>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateArticleAudioFiles())
        {
            var name = Path.GetFileName(file);
            var isArticleFolderAudio = name.Equals("body.mp3", StringComparison.OrdinalIgnoreCase);
            var match = isArticleFolderAudio
                ? _articleFolderRegex.Match(Path.GetFileName(Path.GetDirectoryName(file)!))
                : _filenameRegex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(file);
            _savedEntries.TryGetValue(fullPath, out var saved);
            var articleFolder = isArticleFolderAudio
                ? Path.GetDirectoryName(fullPath)!
                : Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(fullPath));
            var titlePath = Path.Combine(articleFolder, "title.mp3");
            var cachedImagePath = File.Exists(saved?.CachedImagePath)
                ? saved!.CachedImagePath
                : ArticleMediaCache.FindCachedImage(articleFolder);
            var title = saved?.Title;
            var feed = saved?.Feed;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = ArticleFileNaming.ReadableSlug(match.Groups["title"].Value);
            }
            if (string.IsNullOrWhiteSpace(feed))
            {
                feed = ArticleFileNaming.ReadableSlug(match.Groups["feed"].Value);
            }
            var feedConfiguration = FindFeedConfiguration(saved?.FeedUrl, feed);
            var errorMarkerPath = Path.Combine(articleFolder, ErrorMarkerFileName);
            var audioError = !File.Exists(fullPath) && File.Exists(errorMarkerPath)
                ? File.ReadAllText(errorMarkerPath).Trim()
                : string.Empty;

            existing[fullPath] = new ArticleAudio
            {
                FilePath = fullPath,
                TitleAudioPath = File.Exists(titlePath) ? titlePath : string.Empty,
                TitleAudioEngine = saved?.TitleAudioEngine ?? string.Empty,
                BodyAudioEngine = saved?.BodyAudioEngine ?? string.Empty,
                Categories = saved?.Categories ?? [],
                CachedImagePath = cachedImagePath ?? string.Empty,
                ArticleFolderPath = articleFolder,
                Title = title,
                Feed = feed,
                FeedName = feedConfiguration?.Name ?? saved?.FeedName ?? string.Empty,
                FeedUrl = feedConfiguration?.Url ?? saved?.FeedUrl ?? string.Empty,
                ArticleUrl = saved?.ArticleUrl ?? string.Empty,
                ImageUrl = saved?.ImageUrl ?? string.Empty,
                SourceLogoUrl = saved?.SourceLogoUrl ?? string.Empty,
                AudioText = saved?.AudioText ?? string.Empty,
                ArticleContentHtml = saved?.ArticleContentHtml ?? string.Empty,
                PublishedAt = saved?.PublishedAt,
                CreatedAt = File.Exists(fullPath) ? File.GetCreationTime(fullPath) : Directory.GetCreationTime(articleFolder),
                AudioError = audioError,
                IsPlayed = saved?.IsPlayed ?? false,
                IsStarred = saved?.IsStarred ?? false,
                PlayedAt = saved?.PlayedAt,
                LastViewedAt = saved?.LastViewedAt,
                LastListenedAt = saved?.LastListenedAt
            };
        }

        Items.Clear();
        foreach (var item in existing.Values.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Title))
        {
            Items.Add(item);
        }

        _savedEntries.Clear();
        foreach (var item in Items)
        {
            _savedEntries[item.FilePath] = ToState(item);
        }
        Save();
    }

    public void MarkPlayed(ArticleAudio article)
    {
        article.IsPlayed = true;
        article.PlayedAt = DateTime.Now;
        _savedEntries[article.FilePath] = ToState(article);
        Save();
    }

    public int MarkAllPlayed(IEnumerable<ArticleAudio> articles)
    {
        ArgumentNullException.ThrowIfNull(articles);
        var visiblePaths = articles
            .Select(article => article.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unplayed = Items
            .Where(item => visiblePaths.Contains(item.FilePath) && !item.IsPlayed)
            .ToList();
        if (unplayed.Count == 0)
        {
            return 0;
        }

        var playedAt = DateTime.Now;
        foreach (var article in unplayed)
        {
            article.IsPlayed = true;
            article.PlayedAt = playedAt;
            _savedEntries[article.FilePath] = ToState(article);
        }

        Save();
        return unplayed.Count;
    }

    public void Delete(ArticleAudio article)
    {
        var libraryArticle = Items.FirstOrDefault(item =>
            item.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected article is no longer in the library.");
        var fullPath = Path.GetFullPath(libraryArticle.FilePath);
        var articleFolder = Path.GetFullPath(libraryArticle.ArticleFolderPath);
        if (!Path.GetDirectoryName(fullPath)!.Equals(_outputDirectory, StringComparison.OrdinalIgnoreCase) &&
            !Path.GetDirectoryName(fullPath)!.Equals(articleFolder, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected audio file is outside the configured library directory.");
        }

        var folderParent = Path.GetDirectoryName(articleFolder);
        if (!string.Equals(folderParent, _outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The article media folder is outside the configured library directory.");
        }

        if (Directory.Exists(articleFolder))
        {
            Directory.Delete(articleFolder, true);
        }
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        Items.Remove(libraryArticle);
        _savedEntries.Remove(fullPath);
        Save();
    }

    public void AddOrUpdate(ArticleAudio article)
    {
        var legacyFileName = Path.GetFileNameWithoutExtension(article.ArticleFolderPath) +
            ".mp3";
        var legacyPath = Path.GetFullPath(Path.Combine(_outputDirectory, legacyFileName));
        if (_savedEntries.Remove(legacyPath, out var migratedState))
        {
            article.IsPlayed = migratedState.IsPlayed;
            article.IsStarred = migratedState.IsStarred;
            article.PlayedAt = migratedState.PlayedAt;
            article.LastViewedAt = migratedState.LastViewedAt;
            article.LastListenedAt = migratedState.LastListenedAt;
        }

        var existing = Items.FirstOrDefault(item =>
            item.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Items.Insert(0, article);
        }
        else
        {
            existing.Title = article.Title;
            existing.Feed = article.Feed;
            existing.FeedName = article.FeedName;
            existing.FeedUrl = article.FeedUrl;
            existing.ArticleUrl = article.ArticleUrl;
            existing.ImageUrl = article.ImageUrl;
            existing.SourceLogoUrl = article.SourceLogoUrl;
            existing.AudioText = article.AudioText;
            existing.ArticleContentHtml = article.ArticleContentHtml;
            existing.PublishedAt = article.PublishedAt;
            existing.TitleAudioPath = article.TitleAudioPath;
            existing.TitleAudioEngine = article.TitleAudioEngine;
            existing.BodyAudioEngine = article.BodyAudioEngine;
            existing.Categories = article.Categories;
            existing.IsFavoriteCategory = article.IsFavoriteCategory;
            existing.CachedImagePath = article.CachedImagePath;
            existing.ArticleFolderPath = article.ArticleFolderPath;
            existing.AudioError = article.AudioError;
            article = existing;
        }
        _savedEntries[article.FilePath] = ToState(article);
        Save();
    }

    public void MarkUnplayed(ArticleAudio article)
    {
        article.IsPlayed = false;
        article.PlayedAt = null;
        _savedEntries[article.FilePath] = ToState(article);
        Save();
    }

    public void SaveStarred(ArticleAudio article)
    {
        _savedEntries[article.FilePath] = ToState(article);
        Save();
    }

    public void RecordViewed(ArticleAudio article)
    {
        article.LastViewedAt = DateTimeOffset.Now;
        SaveArticleState(article);
    }

    public void RecordListened(ArticleAudio article)
    {
        article.LastListenedAt = DateTimeOffset.Now;
        SaveArticleState(article);
    }

    public void Save()
    {
        var state = new LibraryState
        {
            Entries = _savedEntries.Values.ToList()
        };
        var tempPath = _statePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tempPath, _statePath, true);
    }

    private Dictionary<string, LibraryEntryState> LoadSavedEntries()
    {
        if (!File.Exists(_statePath))
        {
            return new Dictionary<string, LibraryEntryState>(StringComparer.OrdinalIgnoreCase);
        }

        var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(_statePath))
            ?? throw new InvalidDataException($"Could not read played state from {_statePath}.");
        var entries = new Dictionary<string, LibraryEntryState>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in state.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath)))
        {
            var filePath = Path.GetFullPath(entry.FilePath);
            if (!File.Exists(filePath))
            {
                var fileName = Path.GetFileName(filePath);
                var relocatedPath = Path.Combine(_outputDirectory, fileName);
                if (_filenameRegex.IsMatch(fileName) && File.Exists(relocatedPath))
                {
                    filePath = Path.GetFullPath(relocatedPath);
                    entry.FilePath = filePath;
                }
                else if (_filenameRegex.IsMatch(fileName))
                {
                    var articleFolder = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(fileName));
                    var folderBodyPath = Path.Combine(articleFolder, "body.mp3");
                    if (_articleFolderRegex.IsMatch(Path.GetFileName(articleFolder)) && File.Exists(folderBodyPath))
                    {
                        filePath = Path.GetFullPath(folderBodyPath);
                        entry.FilePath = filePath;
                        entry.ArticleFolderPath = articleFolder;
                        entry.TitleAudioPath = Path.Combine(articleFolder, "title.mp3");
                        entry.CachedImagePath = ArticleMediaCache.FindCachedImage(articleFolder) ?? string.Empty;
                    }
                }
            }

            entries[filePath] = entry;
        }

        return entries;
    }

    private IEnumerable<string> EnumerateArticleAudioFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_outputDirectory))
        {
            var name = Path.GetFileName(file);
            if (_filenameRegex.IsMatch(name) &&
                Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var bodyFile in Directory.EnumerateFiles(_outputDirectory, "body.mp3", SearchOption.AllDirectories))
        {
            var articleFolder = Path.GetDirectoryName(bodyFile);
            if (articleFolder is not null &&
                string.Equals(Path.GetDirectoryName(articleFolder), _outputDirectory, StringComparison.OrdinalIgnoreCase) &&
                _articleFolderRegex.IsMatch(Path.GetFileName(articleFolder)))
            {
                yield return bodyFile;
            }
        }

        // Articles whose body audio failed to generate have no body.mp3, only an error marker.
        foreach (var markerFile in Directory.EnumerateFiles(_outputDirectory, ErrorMarkerFileName, SearchOption.AllDirectories))
        {
            var articleFolder = Path.GetDirectoryName(markerFile);
            if (articleFolder is not null &&
                string.Equals(Path.GetDirectoryName(articleFolder), _outputDirectory, StringComparison.OrdinalIgnoreCase) &&
                _articleFolderRegex.IsMatch(Path.GetFileName(articleFolder)) &&
                !File.Exists(Path.Combine(articleFolder, "body.mp3")))
            {
                yield return Path.Combine(articleFolder, "body.mp3");
            }
        }
    }

    private int RemoveExpiredFiles()
    {
        var removed = 0;
        var now = DateTime.Now;
        var favoriteMatcher = new PhraseMatcher(_settings.FavoritedPhrases);
        var favoriteCategoriesByFeed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var currentItems = Items.ToDictionary(item => item.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateArticleAudioFiles().ToList())
        {
            var fullPath = Path.GetFullPath(file);
            currentItems.TryGetValue(fullPath, out var item);
            _savedEntries.TryGetValue(fullPath, out var saved);
            var isStarred = item?.IsStarred ?? saved?.IsStarred ?? false;
            var folder = Path.GetDirectoryName(file);
            var isFolderAudio = Path.GetFileName(file).Equals("body.mp3", StringComparison.OrdinalIgnoreCase);
            var nameMatch = isFolderAudio
                ? _articleFolderRegex.Match(Path.GetFileName(folder!))
                : _filenameRegex.Match(Path.GetFileName(file));
            var feed = item?.Feed ?? saved?.Feed;
            if (string.IsNullOrWhiteSpace(feed))
            {
                feed = ArticleFileNaming.ReadableSlug(nameMatch.Groups["feed"].Value);
            }
            var feedUrl = item?.FeedUrl ?? saved?.FeedUrl;
            feedUrl = FindFeedConfiguration(feedUrl, feed)?.Url ?? feedUrl ?? string.Empty;
            if (!favoriteCategoriesByFeed.TryGetValue(feedUrl, out var favoriteCategories))
            {
                favoriteCategories = new HashSet<string>(
                    _categoryPreferences.Get(feedUrl).FavoriteCategories, StringComparer.OrdinalIgnoreCase);
                favoriteCategoriesByFeed.Add(feedUrl, favoriteCategories);
            }
            var title = item?.Title ?? saved?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = ArticleFileNaming.ReadableSlug(nameMatch.Groups["title"].Value);
            }
            var isFavorite = (item?.Categories ?? saved?.Categories ?? []).Any(favoriteCategories.Contains) ||
                favoriteMatcher.IsMatch(title, item?.AudioText ?? saved?.AudioText);
            var retention = isStarred ? _settings.StarredRetentionPeriod
                : isFavorite ? _settings.FavoritesRetentionPeriod
                : _settings.RetentionPeriod;
            if (isStarred && isFavorite)
            {
                retention = _settings.StarredRetentionPeriod is { } starred &&
                            _settings.FavoritesRetentionPeriod is { } favorite
                    ? (starred > favorite ? starred : favorite)
                    : null;
            }
            var createdAt = File.Exists(file) ? File.GetCreationTime(file) : Directory.GetCreationTime(folder!);
            if (retention is null || now - createdAt <= retention.Value)
            {
                continue;
            }
            removed++;

            if (folder is not null &&
                string.Equals(Path.GetDirectoryName(folder), _outputDirectory, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(folder, true);
            }
            else
            {
                File.Delete(file);
                var mediaFolder = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(file));
                if (Directory.Exists(mediaFolder))
                {
                    Directory.Delete(mediaFolder, true);
                }
            }
            _savedEntries.Remove(fullPath);
        }

        return removed;
    }

    private static LibraryEntryState ToState(ArticleAudio article) => new()
    {
        FilePath = article.FilePath,
        TitleAudioPath = article.TitleAudioPath,
        TitleAudioEngine = article.TitleAudioEngine,
        BodyAudioEngine = article.BodyAudioEngine,
        Categories = article.Categories,
        CachedImagePath = article.CachedImagePath,
        ArticleFolderPath = article.ArticleFolderPath,
        Title = article.Title,
        Feed = article.Feed,
        FeedName = article.FeedName,
        FeedUrl = article.FeedUrl,
        ArticleUrl = article.ArticleUrl,
        ImageUrl = article.ImageUrl,
        SourceLogoUrl = article.SourceLogoUrl,
        AudioText = article.AudioText,
        ArticleContentHtml = article.ArticleContentHtml,
        PublishedAt = article.PublishedAt,
        IsPlayed = article.IsPlayed,
        IsStarred = article.IsStarred,
        PlayedAt = article.PlayedAt,
        LastViewedAt = article.LastViewedAt,
        LastListenedAt = article.LastListenedAt
    };

    private void SaveArticleState(ArticleAudio article)
    {
        _savedEntries[article.FilePath] = ToState(article);
        Save();
    }

    private FeedConfiguration? FindFeedConfiguration(string? feedUrl, string feedHost)
    {
        if (!string.IsNullOrWhiteSpace(feedUrl))
        {
            var exactMatch = _settings.FeedSources.FirstOrDefault(feed =>
                feed.Url.Equals(feedUrl, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        var hostMatches = _settings.FeedSources
            .Where(feed => Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri) &&
                uri.Host.Equals(feedHost, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return hostMatches.Count == 1 ? hostMatches[0] : null;
    }
}
