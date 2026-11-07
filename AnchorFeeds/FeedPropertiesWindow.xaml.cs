using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AnchorFeeds.Models;
using AnchorFeeds.Services;

namespace AnchorFeeds;

public partial class FeedPropertiesWindow : Window
{
    private readonly FeedConfiguration _feed;
    private readonly ObservableCollection<string> _favoriteCategories = [];
    private readonly ObservableCollection<string> _blockedCategories = [];

    public FeedPropertiesWindow(
        FeedConfiguration feed,
        FeedCategoryPreferences preferences)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _feed = feed;
        FeedNameText.Text = feed.Name;
        FeedUrlText.Text = feed.Url;
        Replace(_favoriteCategories, preferences.FavoriteCategories);
        Replace(_blockedCategories, preferences.BlockedCategories);
        FavoriteCategoriesItems.ItemsSource = _favoriteCategories;
        BlockedCategoriesItems.ItemsSource = _blockedCategories;
    }

    public FeedCategoryPreferences? UpdatedPreferences { get; private set; }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e) =>
        RemoveCategory(sender, _favoriteCategories);

    private void RemoveBlockedButton_Click(object sender, RoutedEventArgs e) =>
        RemoveCategory(sender, _blockedCategories);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatedPreferences = new FeedCategoryPreferences(
            _feed.Url,
            _favoriteCategories.ToList(),
            _blockedCategories.ToList());
        DialogResult = true;
    }

    private static void RemoveCategory(object sender, ObservableCollection<string> target)
    {
        if (sender is Button { Tag: string category })
        {
            RemoveCategory(category, target);
        }
    }

    private static void RemoveCategory(string category, ObservableCollection<string> target)
    {
        var existing = target.FirstOrDefault(value =>
            value.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            target.Remove(existing);
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            AddUnique(target, value);
        }
    }

    private static void AddUnique(ObservableCollection<string> target, string value)
    {
        if (!target.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(value);
        }
    }
}
