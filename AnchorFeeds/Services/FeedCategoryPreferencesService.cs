using System.Text.Json;

namespace AnchorFeeds.Services;

public sealed class FeedCategoryPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "anchorfeeds",
        "feed-category-preferences.json");

    public FeedCategoryPreferences Get(string feedUrl)
    {
        lock (_sync)
        {
            var entries = Load();
            return entries.TryGetValue(feedUrl, out var saved)
                ? saved.ToPreferences(feedUrl)
                : new FeedCategoryPreferences(feedUrl, [], []);
        }
    }

    public void SavePreferences(IEnumerable<FeedCategoryPreferences> preferences)
    {
        lock (_sync)
        {
            var entries = Load();
            foreach (var preference in preferences)
            {
                var saved = GetOrCreate(entries, preference.FeedUrl);
                saved.FavoriteCategories = Normalize(preference.FavoriteCategories);
                saved.BlockedCategories = Normalize(preference.BlockedCategories);
            }

            Save(entries);
        }
    }

    public FeedCategoryPreferences SetCategoryDisposition(
        string feedUrl,
        string category,
        bool favorite,
        bool blocked)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A feed category is required.", nameof(category));
        if (favorite && blocked)
            throw new ArgumentException("A category cannot be both favorite and blocked.", nameof(category));

        lock (_sync)
        {
            var entries = Load();
            var saved = GetOrCreate(entries, feedUrl);
            saved.FavoriteCategories = RemoveCategory(saved.FavoriteCategories, category);
            saved.BlockedCategories = RemoveCategory(saved.BlockedCategories, category);
            if (favorite)
                saved.FavoriteCategories = Normalize(saved.FavoriteCategories.Append(category));
            if (blocked)
                saved.BlockedCategories = Normalize(saved.BlockedCategories.Append(category));
            Save(entries);
            return saved.ToPreferences(feedUrl);
        }
    }

    public static bool IsBlocked(FeedCategoryPreferences preferences, IEnumerable<string> categories)
    {
        var blocked = new HashSet<string>(preferences.BlockedCategories, StringComparer.OrdinalIgnoreCase);
        return categories.Any(blocked.Contains);
    }

    public static bool IsDisplayableCategory(string category) =>
        !string.IsNullOrWhiteSpace(category) && !Guid.TryParse(category.Trim(), out _);

    private Dictionary<string, SavedPreferences> Load()
    {
        if (!File.Exists(_statePath))
        {
            return new Dictionary<string, SavedPreferences>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var content = File.ReadAllText(_statePath);
            var loaded = JsonSerializer.Deserialize<List<SavedEntry>>(content, JsonOptions)
                ?? throw new InvalidDataException("Feed category preferences file contains no data.");
            return loaded
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FeedUrl))
                .GroupBy(entry => entry.FeedUrl, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Preferences, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read feed category preferences at '{_statePath}'.", ex);
        }
    }

    private void Save(Dictionary<string, SavedPreferences> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var tempPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var savedEntries = entries
                .Select(entry => new SavedEntry { FeedUrl = entry.Key, Preferences = entry.Value })
                .ToList();
            File.WriteAllText(tempPath, JsonSerializer.Serialize(savedEntries, JsonOptions));
            File.Move(tempPath, _statePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static SavedPreferences GetOrCreate(
        IDictionary<string, SavedPreferences> entries,
        string feedUrl)
    {
        if (entries.TryGetValue(feedUrl, out var saved))
        {
            return saved;
        }

        saved = new SavedPreferences();
        entries.Add(feedUrl, saved);
        return saved;
    }

    private static List<string> Normalize(IEnumerable<string> categories) =>
        categories
            .Select(category => category.Trim())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> ExtractCategories(System.Xml.XmlNode item)
    {
        var categories = new List<string>();
        var nodes = item.SelectNodes("./*[local-name()='category']");
        if (nodes is null)
        {
            return categories;
        }

        foreach (System.Xml.XmlNode category in nodes)
        {
            var value = category.Attributes?["term"]?.Value;
            if (string.IsNullOrWhiteSpace(value))
                value = category.Attributes?["label"]?.Value;
            if (string.IsNullOrWhiteSpace(value))
                value = category.InnerText;
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        return Normalize(categories);
    }

    private static List<string> RemoveCategory(IEnumerable<string> categories, string category) =>
        categories
            .Where(value => !value.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private sealed class SavedEntry
    {
        public SavedEntry()
        {
        }

        public string FeedUrl { get; set; } = string.Empty;
        public SavedPreferences Preferences { get; set; } = new();
    }

    private sealed class SavedPreferences
    {
        public List<string> FavoriteCategories { get; set; } = [];
        public List<string> BlockedCategories { get; set; } = [];

        public FeedCategoryPreferences ToPreferences(string feedUrl) =>
            new(feedUrl, FavoriteCategories, BlockedCategories);
    }
}

public sealed record FeedCategoryPreferences(
    string FeedUrl,
    IReadOnlyList<string> FavoriteCategories,
    IReadOnlyList<string> BlockedCategories);
