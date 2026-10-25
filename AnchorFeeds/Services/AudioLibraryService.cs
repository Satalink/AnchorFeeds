using System.Collections.ObjectModel;
using System.Text.Json;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class AudioLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AppSettings _settings;
    private readonly string _outputDirectory;
    private readonly string _statePath;
    private readonly Dictionary<string, LibraryEntryState> _savedEntries;
    private readonly System.Text.RegularExpressions.Regex _filenameRegex;
    private readonly System.Text.RegularExpressions.Regex _articleFolderRegex;

    public AudioLibraryService(AppSettings settings, string applicationDirectory)
    {
        _settings = settings;
        _outputDirectory = ArticleFileNaming.ResolveOutputDirectory(applicationDirectory, settings.FinalOutput);
        _filenameRegex = ArticleFileNaming.BuildFilenameRegex(
            ArticleFileNaming.GetLegacyFilenameTemplate(settings.FinalOutput));
        _articleFolderRegex = ArticleFileNaming.BuildFilenameRegex(
            ArticleFileNaming.GetArticleFolderTemplate(settings.FinalOutput));

        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiNewscast");
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

    public void Rebuild()
    {
        _savedEntries.Clear();
        Refresh(removeExpiredFiles: false);
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

            existing[fullPath] = new ArticleAudio
            {
                FilePath = fullPath,
                TitleAudioPath = File.Exists(titlePath) ? titlePath : string.Empty,
                CachedImagePath = cachedImagePath ?? string.Empty,
                ArticleFolderPath = articleFolder,
                Title = title,
                Feed = feed,
                ArticleUrl = saved?.ArticleUrl ?? string.Empty,
                ImageUrl = saved?.ImageUrl ?? string.Empty,
                SourceLogoUrl = saved?.SourceLogoUrl ?? string.Empty,
                AudioText = saved?.AudioText ?? string.Empty,
                PublishedAt = saved?.PublishedAt,
                CreatedAt = File.GetCreationTime(fullPath),
                IsPlayed = saved?.IsPlayed ?? false,
                IsStarred = saved?.IsStarred ?? false,
                PlayedAt = saved?.PlayedAt
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

    public int MarkAllPlayed()
    {
        var unplayed = Items.Where(item => !item.IsPlayed).ToList();
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
            Path.GetExtension(_settings.FinalOutput);
        var legacyPath = Path.GetFullPath(Path.Combine(_outputDirectory, legacyFileName));
        if (_savedEntries.Remove(legacyPath, out var migratedState))
        {
            article.IsPlayed = migratedState.IsPlayed;
            article.IsStarred = migratedState.IsStarred;
            article.PlayedAt = migratedState.PlayedAt;
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
            existing.ArticleUrl = article.ArticleUrl;
            existing.ImageUrl = article.ImageUrl;
            existing.SourceLogoUrl = article.SourceLogoUrl;
            existing.AudioText = article.AudioText;
            existing.PublishedAt = article.PublishedAt;
            existing.TitleAudioPath = article.TitleAudioPath;
            existing.CachedImagePath = article.CachedImagePath;
            existing.ArticleFolderPath = article.ArticleFolderPath;
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
    }

    private void RemoveExpiredFiles()
    {
        var cutoff = DateTime.Now.Subtract(_settings.RetentionPeriod);
        foreach (var file in EnumerateArticleAudioFiles())
        {
            if (File.GetCreationTime(file) >= cutoff)
            {
                continue;
            }

            var folder = Path.GetDirectoryName(file);
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
            _savedEntries.Remove(Path.GetFullPath(file));
        }
    }

    private static LibraryEntryState ToState(ArticleAudio article) => new()
    {
        FilePath = article.FilePath,
        TitleAudioPath = article.TitleAudioPath,
        CachedImagePath = article.CachedImagePath,
        ArticleFolderPath = article.ArticleFolderPath,
        Title = article.Title,
        Feed = article.Feed,
        ArticleUrl = article.ArticleUrl,
        ImageUrl = article.ImageUrl,
        SourceLogoUrl = article.SourceLogoUrl,
        AudioText = article.AudioText,
        PublishedAt = article.PublishedAt,
        IsPlayed = article.IsPlayed,
        IsStarred = article.IsStarred,
        PlayedAt = article.PlayedAt
    };
}
