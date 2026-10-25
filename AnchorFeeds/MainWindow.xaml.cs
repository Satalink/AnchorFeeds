using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AnchorFeeds.Models;
using AnchorFeeds.Services;

namespace AnchorFeeds;

public partial class MainWindow : Window
{
    private enum AudioSegment
    {
        None,
        Title,
        Body
    }

    private readonly SettingsService _settingsService = null!;
    private readonly UiPreferencesService _uiPreferencesService = null!;
    private AppSettings _settings = null!;
    private AudioLibraryService _library = null!;
    private NewsGenerationService _generator = null!;
    private readonly DispatcherTimer _positionTimer = new();
    private readonly DispatcherTimer _scanCountdownTimer = new();
    private ArticleAudio? _currentArticle;
    private bool _playbackStarted;
    private bool _isPlaying;
    private bool _audioSourceReady;
    private bool _titleAudioSourceReady;
    private bool _isSeeking;
    private bool _initializing;
    private bool _syncingUnplayedFilter;
    private bool _currentImageLoaded;
    private bool _stopAfterCurrentArticle;
    private bool _scanModeForCurrentArticle;
    private bool _scanCountdownActive;
    private int _scanCountdownRemaining;
    private AudioSegment _audioSegment;
    private CancellationTokenSource? _imageLoadCancellation;
    private double _articleListWidthRatio = UiPreferencesService.DefaultArticleListWidthRatio;

    public bool StartupSucceeded { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _initializing = true;
        _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _scanCountdownTimer.Interval = TimeSpan.FromSeconds(1);
        _scanCountdownTimer.Tick += ScanCountdownTimer_Tick;
        SettingsService settingsService;
        UiPreferencesService uiPreferencesService;
        double articleListWidthRatio;
        AppSettings settings;
        AudioLibraryService library;
        NewsGenerationService generator;
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.yaml");
            settingsService = new SettingsService(settingsPath);
            uiPreferencesService = new UiPreferencesService();
            articleListWidthRatio = uiPreferencesService.LoadArticleListWidthRatio(settingsPath);
            settings = settingsService.Load();
            library = new AudioLibraryService(settings, AppContext.BaseDirectory);
            generator = new NewsGenerationService(settings, AppContext.BaseDirectory);
            library.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not start AnchorFeeds", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _settingsService = settingsService;
        _uiPreferencesService = uiPreferencesService;
        _settings = settings;
        ThemeService.Apply(_settings.AppearanceTheme);
        UpdateScanModeButton();
        _library = library;
        _generator = generator;
        _articleListWidthRatio = _settings.ArticleListWidthRatio = articleListWidthRatio;
        ApplyArticleListWidthRatio(_articleListWidthRatio);
        ArticleList.ItemsSource = _library.Items;
        SelectPlayedRule(_settings.MarkAsPlayed);
        SelectPlaybackSpeed(_settings.PlaybackSpeed);
        AutoPlayNextMenuItem.IsChecked = _settings.AutoPlayNext;
        ShowArticleTextMenuItem.IsChecked = _settings.ShowArticleText;
        SetUnplayedFilterState(_settings.ShowUnplayedOnly);
        SetStarredOnlyFilterState(_settings.ShowStarredOnly);
        SetArticleSortState(_settings.ArticleSortOrder);
        GroupByFeedMenuItem.IsChecked = _settings.GroupArticlesByFeed;
        ApplyArticleListSorting();
        UpdateArticleMediaLayout();
        UpdateFilter();
        StatusText.Text = $"Ready. Audio folder: {_library.OutputDirectory}";
        _positionTimer.Tick += PositionTimer_Tick;
        _initializing = false;
        StartupSucceeded = true;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.RetrieveLatestFeedsOnLaunch)
        {
            await RetrieveLatestFeedsAsync();
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e) =>
        await RetrieveLatestFeedsAsync();

    private async Task RetrieveLatestFeedsAsync()
    {
        GenerateButton.IsEnabled = false;
        GenerationProgress.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var generatedArticles = new Progress<ArticleAudio>(article =>
            {
                _library.AddOrUpdate(article);
                if (_currentArticle?.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _currentArticle.AudioText = article.AudioText;
                    _currentArticle.TitleAudioPath = article.TitleAudioPath;
                    _currentArticle.CachedImagePath = article.CachedImagePath;
                    _currentArticle.ArticleFolderPath = article.ArticleFolderPath;
                    ArticleTextContent.Text = article.AudioText;
                    _ = LoadArticleImageAsync(_currentArticle);
                }
            });
            var result = await _generator.GenerateAsync(progress, generatedArticles, CancellationToken.None);
            _library.Refresh();
            UpdateFilter();
            StatusText.Text = $"Generation complete: {result.GeneratedCount} new article(s).";
            if (result.FailedFeeds.Count > 0 || result.MediaWarnings.Count > 0)
            {
                var warningSections = new List<string>();
                if (result.FailedFeeds.Count > 0)
                {
                    warningSections.Add(
                        $"These feed(s) could not be retrieved:\n\n{string.Join("\n\n", result.FailedFeeds)}");
                }
                if (result.MediaWarnings.Count > 0)
                {
                    warningSections.Add(
                        $"These article images could not be cached:\n\n{string.Join("\n\n", result.MediaWarnings)}");
                }
                if (result.FailedFeeds.Count > 0)
                {
                    StatusText.Text += $" {result.FailedFeeds.Count} feed(s) skipped.";
                }
                if (result.MediaWarnings.Count > 0)
                {
                    StatusText.Text += $" {result.MediaWarnings.Count} image cache warning(s).";
                }
                MessageBox.Show(
                    this,
                    $"Audio generation completed with warnings:\n\n{string.Join("\n\n", warningSections)}",
                    "Feed retrieval or media cache warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Generation failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Audio generation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            GenerationProgress.Visibility = Visibility.Collapsed;
            GenerateButton.IsEnabled = true;
        }
    }

    private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var count = _library.MarkAllPlayed();
            UpdateCurrentState();
            UpdateFilter();
            StatusText.Text = count == 0
                ? "All library articles are already marked as read."
                : $"Marked {count} library article(s) as read.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not mark all articles as read", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteArticleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArticleAudio article })
        {
            return;
        }

        var wasCurrentArticle = ReferenceEquals(_currentArticle, article);
        try
        {
            if (wasCurrentArticle)
            {
                StopCurrentAudio();
                _imageLoadCancellation?.Cancel();
            }

            _library.Delete(article);
            if (wasCurrentArticle)
            {
                _currentArticle = null;
                ArticleList.SelectedItem = null;
                ArticleImage.Source = null;
                _currentImageLoaded = false;
                ArticleTextContent.Text = string.Empty;
                NowPlayingTitle.Text = "Select an article to listen";
                PositionSlider.Value = 0;
                PositionText.Text = "0:00";
                DurationText.Text = "0:00";
                UpdateArticleMediaLayout();
                UpdateCurrentState();
            }

            UpdateFilter();
            StatusText.Text = $"Deleted article audio and saved details: {article.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not delete article", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RebuildLibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Rebuild the library from matching MP3 files?\n\nThis resets article details, played status, and stars. Audio files will not be changed.",
            "Rebuild Library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StopCurrentAudio();
            _imageLoadCancellation?.Cancel();
            _currentArticle = null;
            ArticleList.SelectedItem = null;
            _library.Rebuild();
            ArticleList.ItemsSource = _library.Items;
            ArticleImage.Source = null;
            _currentImageLoaded = false;
            ArticleTextContent.Text = string.Empty;
            NowPlayingTitle.Text = "Select an article to listen";
            PositionSlider.Value = 0;
            PositionText.Text = "0:00";
            DurationText.Text = "0:00";
            UpdateArticleMediaLayout();
            UpdateCurrentState();
            UpdateFilter();
            StatusText.Text = $"Library rebuilt: {_library.Items.Count} article audio file(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not rebuild library", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigureSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var currentLanguage = _settings.ApplicationLanguage;
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.UpdatedSettings is not { } updatedSettings)
        {
            return;
        }

        try
        {
            _settingsService.SaveAll(updatedSettings);
            StopCurrentAudio();
            _imageLoadCancellation?.Cancel();
            _currentArticle = null;
            ArticleList.SelectedItem = null;
            _settings = _settingsService.Load();
            ThemeService.Apply(_settings.AppearanceTheme);
            UpdateScanModeButton();
            _library = new AudioLibraryService(_settings, AppContext.BaseDirectory);
            _library.Refresh();
            _generator = new NewsGenerationService(_settings, AppContext.BaseDirectory);
            ArticleList.ItemsSource = _library.Items;
            SelectPlayedRule(_settings.MarkAsPlayed);
            SelectPlaybackSpeed(_settings.PlaybackSpeed);
            AutoPlayNextMenuItem.IsChecked = _settings.AutoPlayNext;
            ShowArticleTextMenuItem.IsChecked = _settings.ShowArticleText;
            SetUnplayedFilterState(_settings.ShowUnplayedOnly);
            SetStarredOnlyFilterState(_settings.ShowStarredOnly);
            SetArticleSortState(_settings.ArticleSortOrder);
            GroupByFeedMenuItem.IsChecked = _settings.GroupArticlesByFeed;
            ApplyArticleListSorting();
            UpdateArticleMediaLayout();
            UpdateCurrentState();
            UpdateFilter();
            StatusText.Text = string.Equals(
                currentLanguage,
                _settings.ApplicationLanguage,
                StringComparison.OrdinalIgnoreCase)
                    ? $"Settings saved. Audio folder: {_library.OutputDirectory}"
                    : LocalizationService.Get("Settings saved. Restart the app to apply the selected language.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not apply settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlayedRuleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem selected || selected.Tag is not string value)
        {
            return;
        }

        try
        {
            _settingsService.SaveMarkAsPlayed(value);
            _settings.MarkAsPlayed = value;
            SelectPlayedRule(value);
            StatusText.Text = value == "when_started"
                ? "Setting saved: mark played when playback starts."
                : "Setting saved: mark played when playback ends or is skipped.";
        }
        catch (Exception ex)
        {
            SelectPlayedRule(_settings.MarkAsPlayed);
            MessageBox.Show(this, ex.Message, "Could not save setting", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlaybackSpeedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem selected || selected.Tag is not string value ||
            !double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var speed))
        {
            return;
        }

        try
        {
            _settingsService.SavePlaybackSpeed(speed);
            _settings.PlaybackSpeed = speed;
            AudioPlayer.SpeedRatio = speed;
            TitleAudioPlayer.SpeedRatio = speed;
            SelectPlaybackSpeed(speed);
            StatusText.Text = $"Playback speed set to {speed.ToString("0.#", CultureInfo.InvariantCulture)}x.";
        }
        catch (Exception ex)
        {
            SelectPlaybackSpeed(_settings.PlaybackSpeed);
            MessageBox.Show(this, ex.Message, "Could not save playback speed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlaybackSpeedButton_Click(object sender, RoutedEventArgs e)
    {
        PlaybackSpeedContextMenu.PlacementTarget = PlaybackSpeedButton;
        PlaybackSpeedContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PlaybackSpeedContextMenu.IsOpen = true;
    }

    private void ScanModeButton_Click(object sender, RoutedEventArgs e)
    {
        var scanMode = !_settings.ScanMode;
        try
        {
            _settingsService.SaveScanMode(scanMode);
            _settings.ScanMode = scanMode;
            UpdateScanModeButton();
            StatusText.Text = scanMode
                ? "Scan mode is on. It will apply to the next article."
                : "Full mode is on. It will apply to the next article.";
        }
        catch (Exception ex)
        {
            UpdateScanModeButton();
            MessageBox.Show(this, ex.Message, "Could not save playback mode", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateScanModeButton()
    {
        if (ScanModeGlyph is null || _settings is null)
        {
            return;
        }

        ScanModeGlyph.Data = Geometry.Parse(_settings.ScanMode
            ? "M 3,10 A 7,7 0 0 1 16,5 M 16,5 L 16,1 M 16,5 L 12,5 M 16,10 A 7,7 0 0 1 3,15 M 3,15 L 3,19 M 3,15 L 7,15"
            : "M 3,4 H 17 M 3,9 H 17 M 3,14 H 14 M 3,19 H 11");
        ScanModeButton.ToolTip = _settings.ScanMode
            ? "Scan mode is on. Click for Full mode."
            : "Full mode is on. Click for Scan mode.";
    }

    private void AutoPlayNextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var autoPlayNext = AutoPlayNextMenuItem.IsChecked;
        try
        {
            _settingsService.SaveAutoPlayNext(autoPlayNext);
            _settings.AutoPlayNext = autoPlayNext;
            StatusText.Text = autoPlayNext ? "Auto Play Next is on." : "Auto Play Next is off.";
        }
        catch (Exception ex)
        {
            AutoPlayNextMenuItem.IsChecked = _settings.AutoPlayNext;
            MessageBox.Show(this, ex.Message, "Could not save Auto Play Next", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowArticleTextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var showArticleText = ShowArticleTextMenuItem.IsChecked;
        try
        {
            _settingsService.SaveShowArticleText(showArticleText);
            _settings.ShowArticleText = showArticleText;
            UpdateArticleMediaLayout();
        }
        catch (Exception ex)
        {
            ShowArticleTextMenuItem.IsChecked = _settings.ShowArticleText;
            MessageBox.Show(this, ex.Message, "Could not save view setting", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FilterMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } contextMenu } button)
        {
            throw new InvalidOperationException("The article filter menu is unavailable.");
        }

        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
    }

    private void SortMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } contextMenu } button)
        {
            throw new InvalidOperationException("The article sort menu is unavailable.");
        }

        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
    }

    private void ArticleSortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string sortOrder })
        {
            throw new InvalidOperationException("The selected article sort order is unavailable.");
        }

        try
        {
            _settingsService.SaveArticleSortOrder(sortOrder);
            _settings.ArticleSortOrder = sortOrder;
            SetArticleSortState(sortOrder);
            ApplyArticleListSorting();
        }
        catch (Exception ex)
        {
            SetArticleSortState(_settings.ArticleSortOrder);
            MessageBox.Show(this, ex.Message, "Could not save article sort order", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GroupByFeedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var groupByFeed = GroupByFeedMenuItem.IsChecked;
        try
        {
            _settingsService.SaveGroupArticlesByFeed(groupByFeed);
            _settings.GroupArticlesByFeed = groupByFeed;
            ApplyArticleListSorting();
        }
        catch (Exception ex)
        {
            GroupByFeedMenuItem.IsChecked = _settings.GroupArticlesByFeed;
            MessageBox.Show(this, ex.Message, "Could not save article grouping", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetArticleSortState(string sortOrder)
    {
        SortAlphabeticalAscendingMenuItem.IsChecked = sortOrder == "alphabetical_ascending";
        SortAlphabeticalDescendingMenuItem.IsChecked = sortOrder == "alphabetical_descending";
        SortNewestFirstMenuItem.IsChecked = sortOrder == "newest_first";
        SortOldestFirstMenuItem.IsChecked = sortOrder == "oldest_first";
    }

    private void ApplyArticleListSorting()
    {
        if (ArticleList.ItemsSource is null)
        {
            return;
        }

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_library.Items);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            var (property, direction) = _settings.ArticleSortOrder switch
            {
                "alphabetical_ascending" => (nameof(ArticleAudio.Title), ListSortDirection.Ascending),
                "alphabetical_descending" => (nameof(ArticleAudio.Title), ListSortDirection.Descending),
                "oldest_first" => (nameof(ArticleAudio.SortDate), ListSortDirection.Ascending),
                _ => (nameof(ArticleAudio.SortDate), ListSortDirection.Descending)
            };
            view.SortDescriptions.Add(new SortDescription(property, direction));

            view.GroupDescriptions.Clear();
            if (_settings.GroupArticlesByFeed)
            {
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ArticleAudio.Feed)));
            }
        }
    }

    private void UnplayedOnlyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var showUnplayedOnly = UnplayedOnlyMenuItem.IsChecked;
        if (_syncingUnplayedFilter || _initializing)
        {
            UpdateFilter();
            return;
        }

        try
        {
            _settingsService.SaveShowUnplayedOnly(showUnplayedOnly);
            _settings.ShowUnplayedOnly = showUnplayedOnly;
        }
        catch (Exception ex)
        {
            UnplayedOnlyMenuItem.IsChecked = _settings.ShowUnplayedOnly;
            MessageBox.Show(this, ex.Message, "Could not save view setting", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateFilter();
    }

    private void StarredOnlyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var showStarredOnly = StarredOnlyMenuItem.IsChecked;
        if (_syncingUnplayedFilter || _initializing)
        {
            UpdateFilter();
            return;
        }

        try
        {
            _settingsService.SaveShowStarredOnly(showStarredOnly);
            _settings.ShowStarredOnly = showStarredOnly;
        }
        catch (Exception ex)
        {
            StarredOnlyMenuItem.IsChecked = _settings.ShowStarredOnly;
            MessageBox.Show(this, ex.Message, "Could not save view setting", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        UpdateFilter();
    }

    private void SetUnplayedFilterState(bool enabled)
    {
        _syncingUnplayedFilter = true;
        try
        {
            UnplayedOnlyMenuItem.IsChecked = enabled;
        }
        finally
        {
            _syncingUnplayedFilter = false;
        }
        UpdateFilter();
    }

    private void SetStarredOnlyFilterState(bool enabled)
    {
        _syncingUnplayedFilter = true;
        try
        {
            StarredOnlyMenuItem.IsChecked = enabled;
        }
        finally
        {
            _syncingUnplayedFilter = false;
        }

        UpdateFilter();
    }

    private void ArticleGridSplitter_DragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(SaveArticleListWidthRatio));
    }

    private void SaveArticleListWidthRatio()
    {
        var availableWidth = ArticleListColumn.ActualWidth + ArticlePlayerColumn.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(ArticleListColumn.ActualWidth / availableWidth, 0.2, 0.8);
        try
        {
            _settingsService.SaveArticleListWidthRatio(ratio);
            _articleListWidthRatio = _settings.ArticleListWidthRatio = ratio;
            ApplyArticleListWidthRatio(ratio);
        }
        catch (Exception ex)
        {
            ApplyArticleListWidthRatio(_articleListWidthRatio);
            MessageBox.Show(this, ex.Message, "Could not save divider position", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyArticleListWidthRatio(double ratio)
    {
        ArticleListColumn.Width = new GridLength(ratio, GridUnitType.Star);
        ArticlePlayerColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ArticleList.SelectedItem is not ArticleAudio selected)
        {
            return;
        }

        SelectArticle(selected);
    }

    private void SelectArticle(ArticleAudio selected)
    {
        var changingArticle = _currentArticle is not null && !ReferenceEquals(_currentArticle, selected);
        if (changingArticle)
        {
            _stopAfterCurrentArticle = false;
            MarkCurrentAsSkipped();
            StopCurrentAudio();
        }

        _currentArticle = selected;
        NowPlayingTitle.Text = selected.Title;
        ArticleTextContent.Text = selected.HasRedundantBodyAudio
            ? string.Empty
            : string.IsNullOrWhiteSpace(selected.AudioText)
                ? "Article text is not available yet. Select Retrieve Latest Feeds to update saved article details."
                : selected.AudioText;
        ArticleTextScrollViewer.ScrollToTop();
        UpdateCurrentState();
        ArticleImage.Cursor = TryGetArticleUri(selected.ArticleUrl, out _) ? Cursors.Hand : Cursors.Arrow;
        PositionSlider.Value = 0;
        PositionText.Text = "0:00";
        DurationText.Text = "0:00";
        PlaySelected();

        _ = LoadArticleImageAsync(selected);
    }

    private void ArticleImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentArticle is null || !TryGetArticleUri(_currentArticle.ArticleUrl, out var articleUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(articleUri.AbsoluteUri) { UseShellExecute = true });
            _stopAfterCurrentArticle = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open article", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScanCountdownOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_currentArticle is null || !_scanCountdownActive)
        {
            return;
        }

        if (!_currentArticle.HasUsableBodyAudio)
        {
            StatusText.Text = "This article has no distinct body audio to play.";
            return;
        }

        var bodyPath = _currentArticle.FilePath;
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        _isPlaying = false;
        ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        UpdateArticleMediaLayout();
        OpenAudioSegment(AudioSegment.Body, bodyPath);
    }

    private void ArticleTitleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArticleAudio article })
        {
            return;
        }

        if (!ReferenceEquals(ArticleList.SelectedItem, article))
        {
            ArticleList.SelectedItem = article;
            if (ReferenceEquals(_currentArticle, article) && IsArticleAudioActive())
            {
                return;
            }

            SelectArticle(article);
            return;
        }

        if (!ReferenceEquals(_currentArticle, article))
        {
            SelectArticle(article);
        }
        else
        {
            PlaySelected();
        }
    }

    private void StarArticleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ArticleAudio article })
        {
            ToggleStar(article);
        }
    }

    private void StarCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is not null)
        {
            ToggleStar(_currentArticle);
        }
    }

    private void ToggleStar(ArticleAudio article)
    {
        article.IsStarred = !article.IsStarred;
        _library.SaveStarred(article);
        if (ReferenceEquals(_currentArticle, article))
        {
            UpdateCurrentState();
        }
        UpdateFilter();
    }

    private async Task LoadArticleImageAsync(ArticleAudio article)
    {
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _imageLoadCancellation.Token;
        _currentImageLoaded = false;
        ArticleImage.Source = null;
        UpdateArticleMediaLayout();

        var cachedPath = File.Exists(article.CachedImagePath)
            ? article.CachedImagePath
            : ArticleMediaCache.FindCachedImage(article.ArticleFolderPath);
        if (cachedPath is not null)
        {
            try
            {
                ArticleImage.Source = ArticleMediaCache.LoadBitmap(cachedPath);
                article.CachedImagePath = cachedPath;
                _currentImageLoaded = true;
                UpdateArticleMediaLayout();
                return;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
            {
                StatusText.Text = $"Could not load cached article image: {ex.Message}";
            }
        }

        var candidates = new[] { article.ImageUrl, article.SourceLogoUrl }
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        string? imageLoadError = null;
        foreach (var candidate in candidates)
        {
            if (!TryGetArticleUri(candidate, out var imageUri))
            {
                continue;
            }

            try
            {
                var imagePath = await ArticleMediaCache.DownloadImageAsync(
                    imageUri.AbsoluteUri,
                    article.ArticleFolderPath,
                    cancellationToken);
                if (imagePath is null || cancellationToken.IsCancellationRequested ||
                    !ReferenceEquals(_currentArticle, article))
                {
                    continue;
                }

                ArticleImage.Source = ArticleMediaCache.LoadBitmap(imagePath);
                article.CachedImagePath = imagePath;
                _currentImageLoaded = true;
                UpdateArticleMediaLayout();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException ex)
            {
                imageLoadError = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                imageLoadError = ex.Message;
            }
            catch (NotSupportedException ex)
            {
                imageLoadError = ex.Message;
            }
            catch (FileFormatException ex)
            {
                imageLoadError = ex.Message;
            }
            catch (IOException ex)
            {
                imageLoadError = ex.Message;
            }
            catch (InvalidDataException ex)
            {
                imageLoadError = ex.Message;
            }
        }

        if (imageLoadError is not null && !cancellationToken.IsCancellationRequested &&
            ReferenceEquals(_currentArticle, article))
        {
            StatusText.Text = $"Could not load article image: {imageLoadError}";
        }
    }

    private void UpdateArticleMediaLayout()
    {
        var showImageArea = _currentImageLoaded || _scanCountdownActive;
        var showArticleText = _settings.ShowArticleText && _currentArticle?.HasRedundantBodyAudio != true;
        ArticleImagePanel.Visibility = showImageArea ? Visibility.Visible : Visibility.Collapsed;
        ArticleTextPanel.Visibility = showArticleText ? Visibility.Visible : Visibility.Collapsed;

        if (showImageArea && showArticleText)
        {
            ArticleImageRow.Height = new GridLength(2, GridUnitType.Star);
            ArticleTextRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else if (showImageArea)
        {
            ArticleImageRow.Height = new GridLength(1, GridUnitType.Star);
            ArticleTextRow.Height = new GridLength(0);
        }
        else if (showArticleText)
        {
            ArticleImageRow.Height = new GridLength(0);
            ArticleTextRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            ArticleImageRow.Height = new GridLength(0);
            ArticleTextRow.Height = new GridLength(0);
        }
    }

    private static bool TryGetArticleUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlaySelected();

    private void PlaySelected()
    {
        if (ArticleList.SelectedItem is not ArticleAudio selected)
        {
            return;
        }

        if (_scanCountdownActive)
        {
            _isPlaying = true;
            _scanCountdownTimer.Start();
            StatusText.Text = $"Next article in {_scanCountdownRemaining} second(s).";
            return;
        }

        if (ReferenceEquals(_currentArticle, selected) && IsArticleAudioActive())
        {
            if (_audioSegment == AudioSegment.Title)
            {
                TitleAudioPlayer.Play();
            }
            else
            {
                AudioPlayer.Play();
                _positionTimer.Start();
            }
            _isPlaying = true;
            MarkOnStart();
            return;
        }

        StopCurrentAudio();
        _currentArticle = selected;
        _playbackStarted = false;
        _scanModeForCurrentArticle = _settings.ScanMode;
        NowPlayingTitle.Text = selected.Title;
        UpdateCurrentState();
        var titlePath = selected.TitleAudioPath;
        if (!string.IsNullOrWhiteSpace(titlePath) && File.Exists(titlePath))
        {
            OpenTitleAudio(titlePath);
        }
        else
        {
            OpenAudioSegment(AudioSegment.Body, selected.FilePath);
        }
    }

    private bool OpenAudioSegment(AudioSegment segment, string audioPath)
    {
        if (!File.Exists(audioPath))
        {
            _audioSegment = AudioSegment.None;
            _isPlaying = false;
            _positionTimer.Stop();
            StatusText.Text = $"Audio file not found: {audioPath}";
            MessageBox.Show(
                this,
                $"The selected article audio file could not be found:\n{audioPath}",
                "Article audio missing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        _audioSegment = segment;
        _audioSourceReady = false;
        _positionTimer.Stop();
        if (AudioPlayer.Source is not null)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }
        AudioPlayer.Source = new Uri(audioPath);
        PositionSlider.Value = 0;
        PositionText.Text = "0:00.00";
        DurationText.Text = "0:00.00";
        StatusText.Text = segment == AudioSegment.Title
            ? $"Opening title: {_currentArticle?.Title}"
            : $"Opening article: {_currentArticle?.Title}";
        return true;
    }

    private void OpenTitleAudio(string audioPath)
    {
        _audioSegment = AudioSegment.Title;
        _titleAudioSourceReady = false;
        _positionTimer.Stop();
        if (TitleAudioPlayer.Source is not null)
        {
            TitleAudioPlayer.Stop();
            TitleAudioPlayer.Source = null;
        }

        TitleAudioPlayer.Source = new Uri(audioPath);
        PositionSlider.Value = 0;
        PositionText.Text = "0:00.00";
        DurationText.Text = "0:00.00";
        StatusText.Text = $"Opening title: {_currentArticle?.Title}";
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPlaying)
        {
            return;
        }

        _scanCountdownTimer.Stop();
        if (!_scanCountdownActive)
        {
            if (_audioSegment == AudioSegment.Title)
            {
                TitleAudioPlayer.Pause();
            }
            else
            {
                AudioPlayer.Pause();
            }
        }
        _isPlaying = false;
        _positionTimer.Stop();
        StatusText.Text = "Paused.";
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (ArticleList.Items.Count == 0)
        {
            return;
        }

        var nextArticle = FindNextArticle(wrap: true);
        MarkCurrentAsSkipped();
        if (nextArticle is not null)
        {
            NavigateToArticle(nextArticle);
        }
        else
        {
            StopCurrentAudio();
            StatusText.Text = "No other visible articles.";
        }
    }

    private void MarkButton_Click(object sender, RoutedEventArgs e)
    {
        var article = sender is Button { Tag: ArticleAudio taggedArticle }
            ? taggedArticle
            : _currentArticle;
        if (article is null)
        {
            return;
        }

        if (article.IsPlayed)
        {
            _library.MarkUnplayed(article);
        }
        else
        {
            _library.MarkPlayed(article);
        }
        if (ReferenceEquals(_currentArticle, article))
        {
            UpdateCurrentState();
        }
        UpdateFilter();
    }

    private void MarkCurrentReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is null)
        {
            return;
        }

        if (!_currentArticle.IsPlayed)
        {
            _library.MarkPlayed(_currentArticle);
            UpdateCurrentState();
            UpdateFilter();
        }

        StopCurrentAudio();
        StatusText.Text = "Article marked as read.";
    }

    private void AudioPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is null || _audioSegment != AudioSegment.Body)
        {
            return;
        }

        _audioSourceReady = true;
        AudioPlayer.SpeedRatio = _settings.PlaybackSpeed;
        AudioPlayer.Position = TimeSpan.Zero;
        AudioPlayer.Play();
        _isPlaying = true;
        _playbackStarted = true;
        _positionTimer.Start();
        if (AudioPlayer.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            PositionSlider.Value = 0;
            PositionText.Text = FormatTime(TimeSpan.Zero);
            DurationText.Text = FormatTime(AudioPlayer.NaturalDuration.TimeSpan);
        }
        MarkOnStart();
        StatusText.Text = $"Playing: {_currentArticle.Title}";
    }

    private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!_audioSourceReady ||
            AudioPlayer.NaturalDuration.HasTimeSpan &&
            AudioPlayer.Position + TimeSpan.FromMilliseconds(750) < AudioPlayer.NaturalDuration.TimeSpan)
        {
            return;
        }

        _audioSourceReady = false;
        _isPlaying = false;
        _positionTimer.Stop();
        CompleteCurrentArticle(_scanModeForCurrentArticle);
    }

    private void TitleAudioPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is null || _audioSegment != AudioSegment.Title)
        {
            return;
        }

        _titleAudioSourceReady = true;
        TitleAudioPlayer.SpeedRatio = _settings.PlaybackSpeed;
        TitleAudioPlayer.Position = TimeSpan.Zero;
        TitleAudioPlayer.Play();
        _isPlaying = true;
        _playbackStarted = true;
        _positionTimer.Stop();
        PositionSlider.Value = 0;
        PositionText.Text = "0:00.00";
        DurationText.Text = "0:00.00";
        MarkOnStart();
        StatusText.Text = $"Reading title: {_currentArticle.Title}";
    }

    private void TitleAudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!_titleAudioSourceReady ||
            TitleAudioPlayer.NaturalDuration.HasTimeSpan &&
            TitleAudioPlayer.Position + TimeSpan.FromMilliseconds(750) < TitleAudioPlayer.NaturalDuration.TimeSpan)
        {
            return;
        }

        _titleAudioSourceReady = false;
        _isPlaying = false;
        if (TitleAudioPlayer.Source is not null)
        {
            TitleAudioPlayer.Stop();
            TitleAudioPlayer.Source = null;
        }

        if (_currentArticle is null)
        {
            return;
        }

        if (_scanModeForCurrentArticle)
        {
            StartScanCountdown();
        }
        else if (_currentArticle.HasUsableBodyAudio)
        {
            OpenAudioSegment(AudioSegment.Body, _currentArticle.FilePath);
        }
        else
        {
            CompleteCurrentArticle(scanMode: false);
        }
    }

    private void TitleAudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _titleAudioSourceReady = false;
        _audioSegment = AudioSegment.None;
        _isPlaying = false;
        _positionTimer.Stop();
        if (TitleAudioPlayer.Source is not null)
        {
            TitleAudioPlayer.Stop();
            TitleAudioPlayer.Source = null;
        }
        StatusText.Text = $"Title playback failed: {e.ErrorException.Message}";
        MessageBox.Show(this, e.ErrorException.Message, "Title playback failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void StartScanCountdown()
    {
        _audioSegment = AudioSegment.None;
        if (AudioPlayer.Source is not null)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }
        _scanCountdownRemaining = _settings.ScanCountdownSeconds;
        _scanCountdownActive = true;
        _isPlaying = true;
        ScanCountdownText.Text = _scanCountdownRemaining.ToString(CultureInfo.InvariantCulture);
        ScanCountdownOverlay.Visibility = Visibility.Visible;
        UpdateArticleMediaLayout();
        _scanCountdownTimer.Start();
        StatusText.Text = $"Next article in {_scanCountdownRemaining} second(s).";
    }

    private void ScanCountdownTimer_Tick(object? sender, EventArgs e)
    {
        if (!_scanCountdownActive || !_isPlaying)
        {
            _scanCountdownTimer.Stop();
            return;
        }

        _scanCountdownRemaining--;
        if (_scanCountdownRemaining <= 0)
        {
            _scanCountdownTimer.Stop();
            _scanCountdownActive = false;
            _isPlaying = false;
            ScanCountdownOverlay.Visibility = Visibility.Collapsed;
            UpdateArticleMediaLayout();
            CompleteCurrentArticle(scanMode: true);
            return;
        }

        ScanCountdownText.Text = _scanCountdownRemaining.ToString(CultureInfo.InvariantCulture);
        StatusText.Text = $"Next article in {_scanCountdownRemaining} second(s).";
    }

    private void CompleteCurrentArticle(bool scanMode)
    {
        _isPlaying = false;
        _positionTimer.Stop();
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        _audioSegment = AudioSegment.None;
        _audioSourceReady = false;
        _titleAudioSourceReady = false;
        if (AudioPlayer.Source is not null)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }
        if (TitleAudioPlayer.Source is not null)
        {
            TitleAudioPlayer.Stop();
            TitleAudioPlayer.Source = null;
        }
        UpdateArticleMediaLayout();
        var stopAfterCurrentArticle = _stopAfterCurrentArticle;
        _stopAfterCurrentArticle = false;
        var shouldAdvance = scanMode || _settings.AutoPlayNext;
        var nextArticle = shouldAdvance && !stopAfterCurrentArticle
            ? FindNextArticle(wrap: false)
            : null;
        var completedArticle = _currentArticle;
        if (!_settings.MarkOnStart && _playbackStarted && completedArticle is not null)
        {
            _library.MarkPlayed(completedArticle);
        }
        PositionSlider.Value = scanMode ? 0 : PositionSlider.Maximum;
        UpdateCurrentState();
        if (nextArticle is not null)
        {
            PageTurnSound.Play();
            NavigateToArticle(nextArticle);
            return;
        }
        StatusText.Text = "Playback finished.";
    }

    private void AudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _audioSourceReady = false;
        _isPlaying = false;
        _positionTimer.Stop();
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        _audioSegment = AudioSegment.None;
        ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        UpdateArticleMediaLayout();
        StatusText.Text = $"Playback failed: {e.ErrorException.Message}";
        MessageBox.Show(this, e.ErrorException.Message, "Playback failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioSegment != AudioSegment.Body || _isSeeking || !_isPlaying ||
            !AudioPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var position = AudioPlayer.Position;
        PositionSlider.Value = position.TotalSeconds;
        PositionText.Text = FormatTime(position);
        if (ArticleTextPanel.Visibility == Visibility.Visible &&
            AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0)
        {
            var progress = Math.Clamp(
                position.TotalSeconds / AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds,
                0,
                1);
            ArticleTextScrollViewer.ScrollToVerticalOffset(
                ArticleTextScrollViewer.ScrollableHeight * progress);
        }
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_currentArticle is not null && AudioPlayer.Source is not null)
        {
            AudioPlayer.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PositionText is not null && _isSeeking)
        {
            PositionText.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
        }
    }

    private void MarkOnStart()
    {
        if (_settings.MarkOnStart && _currentArticle is not null && !_currentArticle.IsPlayed)
        {
            _library.MarkPlayed(_currentArticle);
            UpdateCurrentState();
            UpdateFilter();
        }
    }

    private void MarkCurrentAsSkipped()
    {
        if (!_settings.MarkOnStart && _currentArticle is not null && !_currentArticle.IsPlayed)
        {
            _library.MarkPlayed(_currentArticle);
            UpdateCurrentState();
            UpdateFilter();
        }
    }

    private void StopCurrentAudio()
    {
        _positionTimer.Stop();
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        _scanCountdownRemaining = 0;
        _audioSegment = AudioSegment.None;
        _audioSourceReady = false;
        _titleAudioSourceReady = false;
        if (ScanCountdownOverlay is not null)
        {
            ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        }
        if (ArticleImagePanel is not null && _settings is not null)
        {
            UpdateArticleMediaLayout();
        }
        if (AudioPlayer.Source is not null)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }
        if (TitleAudioPlayer.Source is not null)
        {
            TitleAudioPlayer.Stop();
            TitleAudioPlayer.Source = null;
        }
        _isPlaying = false;
        _playbackStarted = false;
    }

    private bool IsArticleAudioActive() =>
        _audioSegment == AudioSegment.Title && TitleAudioPlayer.Source is not null ||
        _audioSegment == AudioSegment.Body && AudioPlayer.Source is not null;

    private void UpdateFilter()
    {
        if (ArticleList.ItemsSource is null)
        {
            return;
        }

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_library.Items);
        view.Filter = item => item is ArticleAudio article &&
            (!UnplayedOnlyMenuItem.IsChecked || !article.IsPlayed || article.IsStarred) &&
            (!StarredOnlyMenuItem.IsChecked || article.IsStarred);
        view.Refresh();
    }

    private void UpdateCurrentState()
    {
        if (_currentArticle is null)
        {
            StarCurrentButton.IsEnabled = false;
            MarkCurrentReadButton.IsEnabled = false;
            return;
        }

        StarCurrentButton.IsEnabled = true;
        MarkCurrentReadButton.IsEnabled = true;
        StarCurrentButton.Content = _currentArticle.StarLabel;
    }

    private void SelectPlayedRule(string value)
    {
        var whenStarted = value.Equals("when_started", StringComparison.OrdinalIgnoreCase);
        WhenStartedMenuItem.IsChecked = whenStarted;
        WhenEndedMenuItem.IsChecked = !whenStarted;
    }

    private void SelectPlaybackSpeed(double speed)
    {
        Speed07MenuItem.IsChecked = speed == 0.7;
        Speed08MenuItem.IsChecked = speed == 0.8;
        Speed09MenuItem.IsChecked = speed == 0.9;
        Speed1MenuItem.IsChecked = speed == 1.0;
        Speed11MenuItem.IsChecked = speed == 1.1;
        Speed12MenuItem.IsChecked = speed == 1.2;
        Speed13MenuItem.IsChecked = speed == 1.3;
    }

    private ArticleAudio? FindNextArticle(bool wrap)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_library.Items);
        var visibleItems = view.Cast<ArticleAudio>().ToList();
        if (visibleItems.Count < 2)
        {
            return null;
        }

        var currentVisibleIndex = _currentArticle is null
            ? -1
            : visibleItems.FindIndex(article =>
                article.FilePath.Equals(_currentArticle.FilePath, StringComparison.OrdinalIgnoreCase));
        if (currentVisibleIndex < 0)
        {
            return visibleItems[0];
        }

        var nextIndex = currentVisibleIndex + 1;
        if (nextIndex < visibleItems.Count)
        {
            return visibleItems[nextIndex];
        }

        return wrap ? visibleItems[0] : null;
    }

    private void NavigateToArticle(ArticleAudio article)
    {
        ArticleList.SelectedItem = article;
        ArticleList.ScrollIntoView(article);
    }

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

    protected override void OnClosed(EventArgs e)
    {
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        StopCurrentAudio();
        if (_library is not null)
        {
            _library.Save();
        }
        base.OnClosed(e);
    }
}
