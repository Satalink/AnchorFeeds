using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using AnchorFeeds.Models;
using AnchorFeeds.Services;
using Windows.Media;

namespace AnchorFeeds;

public partial class MainWindow : Window
{
    private const double ArticleTextInitialScrollLag = 0.03;
    private const int WmNcRightButtonUp = 0x00A5;
    private const int WmMouseHorizontalWheel = 0x020E;
    private const int WmAppCommand = 0x0319;
    private const int AppCommandBrowserBack = 1;
    private const int AppCommandBrowserForward = 2;
    private const int HtClose = 20;
    private const string ScanModeIconData =
        "M 12,2 A 10,10 0 0 1 22,12 A 10,10 0 0 1 12,22 A 10,10 0 0 1 2,12 A 10,10 0 0 1 12,2 M 12,12 L 6,5 M 12,2 V 4 M 22,12 H 20 M 12,22 V 20 M 2,12 H 4 M 12,8 A 4,4 0 0 1 16,12";
    private const string FullModeIconData =
        "M 5,2 H 15 L 20,7 V 22 H 5 Z M 15,2 V 7 H 20 M 8,12 H 17 M 8,16 H 17";
    private const string UnmutedIconData =
        "M 3,9 H 7 L 12,5 V 19 L 7,15 H 3 Z M 16,9 A 4,4 0 0 1 16,15 M 18,6 A 8,8 0 0 1 18,18";
    private const string MutedIconData =
        "M 3,9 H 7 L 12,5 V 19 L 7,15 H 3 Z M 16,9 L 21,15 M 21,9 L 16,15";

    private enum AudioSegment
    {
        None,
        Title,
        Body
    }

    private enum NavigationMode
    {
        AllFeeds,
        Starred,
        Favorites,
        Search,
        History
    }

    private readonly SettingsService _settingsService = null!;
    private readonly UiPreferencesService _uiPreferencesService = null!;
    private AppSettings _settings = null!;
    private AudioLibraryService _library = null!;
    private NewsGenerationService _generator = null!;
    private readonly DispatcherTimer _positionTimer = new();
    private readonly DispatcherTimer _relativeTimeTimer = new();
    private readonly DispatcherTimer _scanCountdownTimer = new();
    private readonly DispatcherTimer _feedUpdateTimer = new();
    private bool _isRetrievingFeeds;
    private bool _isRebuildingArticle;
    private CancellationTokenSource? _feedUpdateCancellation;
    private ArticleAudio? _currentArticle;
    private ArticleAudio? _sourceThumbnailArticle;
    private IReadOnlyList<Uri> _sourceThumbnailFallbacks = [];
    private int _sourceThumbnailFallbackIndex;
    private bool _playbackStarted;
    private bool _isPlayingState;

    private bool _isPlaying
    {
        get => _isPlayingState;
        set
        {
            _isPlayingState = value;
            UpdatePlayPauseButton();
        }
    }

    private const string PlayIconData = "M 3,1 L 15,8 L 3,15 Z";
    private const string PauseIconData = "M 2,1 H 6 V 15 H 2 Z M 10,1 H 14 V 15 H 10 Z";
    private const string ErrorIconData = "M 2,2 L 14,14 M 14,2 L 2,14";

    private void UpdatePlayPauseButton()
    {
        if (PlayPauseIcon is null || PlayPauseButton is null)
        {
            return;
        }

        if (!_isPlayingState && (ArticleList?.SelectedItem as ArticleAudio ?? _currentArticle) is { HasAudioError: true } failed)
        {
            PlayPauseIcon.Data = Geometry.Parse(ErrorIconData);
            PlayPauseIcon.Fill = Brushes.Transparent;
            PlayPauseIcon.Stroke = Brushes.IndianRed;
            PlayPauseIcon.StrokeThickness = 2.4;
            PlayPauseButton.ToolTip = $"Audio could not be generated: {failed.AudioError}";
            System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, "Audio generation failed");
            return;
        }

        PlayPauseIcon.ClearValue(System.Windows.Shapes.Shape.StrokeProperty);
        PlayPauseIcon.ClearValue(System.Windows.Shapes.Shape.StrokeThicknessProperty);
        PlayPauseIcon.SetBinding(
            System.Windows.Shapes.Shape.FillProperty,
            new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
        var label = _isPlayingState ? "Pause" : "Play";
        PlayPauseIcon.Data = Geometry.Parse(_isPlayingState ? PauseIconData : PlayIconData);
        PlayPauseButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, label);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlayingState)
        {
            PauseButton_Click(sender, e);
        }
        else
        {
            PlayButton_Click(sender, e);
        }
    }
    private double _playerVolume = 1;
    private bool _audioSourceReady;
    private bool _isSeeking;
    private bool _restoreMuteAfterPlayback;
    private bool _playBodyAudioOnNextSelection;
    private bool _initializing;
    private bool _syncingUnplayedFilter;
    private bool _currentImageLoaded;
    private bool _splashVideoFailed;
    private bool _splashVideoStopped;
    private int _splashVideoLoopCount;
    private bool _stopAfterCurrentArticle;
    private bool _scanModeForCurrentArticle;
    private readonly List<ArticleMediaItem> _mediaItems = [];
    private int _mediaIndex;
    private bool _mediaNavigated;
    private BitmapSource? _primaryBitmap;
    private bool _scanCountdownActive;
    private int _scanCountdownRemaining;
    private AudioSegment _audioSegment;
    private CancellationTokenSource? _imageLoadCancellation;
    private double _articleListWidthRatio = UiPreferencesService.DefaultArticleListWidthRatio;
    private double _articleImageHeightRatio = 2d / 3d;
    private double _navigationPaneWidth = UiPreferencesService.DefaultNavigationPaneWidth;
    private bool _navigationPaneCollapsed;
    private NavigationMode _navigationMode;
    private readonly List<FeedCategorySelection> _selectedFeedCategories = [];
    private readonly AudioPlaylistExportService _playlistExportService = new();

    private readonly WindowPlacementService _windowPlacementService = new();
    private readonly FeedCategoryPreferencesService _feedCategoryPreferences = new();
    private TrayIconService? _trayIconService;
    private TickerWindow? _tickerWindow;
    private bool _tickerSuppressed;
    private ArticleAudio? _notificationArticle;
    private MediaPlayer? _notificationPlayer;
    private HwndSource? _windowSource;
    private SystemMediaTransportControls? _systemMediaTransportControls;
    private bool _loadOnlyNoPlayback;
    private bool _allowExit;

    public bool StartMinimizedToTray { get; set; }
    public bool StartupSucceeded { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        InitializeSystemMediaTransportControls(windowHandle);
    }

    private void InitializeSystemMediaTransportControls(IntPtr windowHandle)
    {
        try
        {
            var controls = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsPreviousEnabled = true;
            controls.IsNextEnabled = true;
            controls.ButtonPressed += SystemMediaTransportControls_ButtonPressed;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            _systemMediaTransportControls = controls;
            UpdateSystemMediaTransportControls();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Exception("Could not initialize system media transport controls", ex);
            if (StatusText is not null)
            {
                StatusText.Text = $"Headphone media controls are unavailable: {ex.Message}";
            }
        }
    }

    private void SystemMediaTransportControls_ButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        Dispatcher.BeginInvoke(() =>
        {
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play:
                    if (!_isPlaying)
                    {
                        PlayButton_Click(this, new RoutedEventArgs());
                    }
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    PauseButton_Click(this, new RoutedEventArgs());
                    break;
                case SystemMediaTransportControlsButton.Next:
                    NextButton_Click(this, new RoutedEventArgs());
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    PreviousButton_Click(this, new RoutedEventArgs());
                    break;
            }
        });
    }

    private void UpdateSystemMediaTransportControls()
    {
        if (_systemMediaTransportControls is not { } controls)
        {
            return;
        }

        controls.PlaybackStatus = _isPlaying
            ? MediaPlaybackStatus.Playing
            : _currentArticle is not null && (_scanCountdownActive || IsArticleAudioActive())
                ? MediaPlaybackStatus.Paused
                : MediaPlaybackStatus.Stopped;
        controls.DisplayUpdater.Type = MediaPlaybackType.Music;
        controls.DisplayUpdater.MusicProperties.Title = _currentArticle?.Title ?? "AnchorFeeds";
        controls.DisplayUpdater.MusicProperties.Artist = _currentArticle?.FeedDisplayName ?? string.Empty;
        controls.DisplayUpdater.MusicProperties.AlbumTitle = "AnchorFeeds";
        controls.DisplayUpdater.Update();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmNcRightButtonUp && wParam.ToInt32() == HtClose)
        {
            handled = true;
            Dispatcher.BeginInvoke(ExitFromTray);
        }
        else if (message == WmMouseHorizontalWheel)
        {
            var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            if (delta != 0)
            {
                handled = true;
                ExecuteTiltNavigation(delta > 0);
            }
        }
        else if (message == WmAppCommand)
        {
            var command = (int)((lParam.ToInt64() >> 16) & 0x7FF);
            if (command is AppCommandBrowserBack or AppCommandBrowserForward)
            {
                handled = true;
                ExecuteTiltNavigation(command == AppCommandBrowserForward);
                return new IntPtr(1);
            }
        }

        return IntPtr.Zero;
    }

    private void ExecuteTiltNavigation(bool next)
    {
        var action = next
            ? nameof(HotkeySettings.SkipNext)
            : nameof(HotkeySettings.PreviousBack);
        DiagnosticLog.Write("Input", $"Mouse tilt {(next ? "right" : "left")} mapped to {action}.");
        ExecuteHotkey(action);
    }

    private void RestoreWindowPlacement()
    {
        var placement = _windowPlacementService.Load();
        if (placement is null)
        {
            return;
        }

        var saved = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var visible = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var overlap = Rect.Intersect(saved, visible);
        if (overlap.IsEmpty || overlap.Width < 100 || overlap.Height < 100)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = Math.Max(placement.Width, MinWidth);
        Height = Math.Max(placement.Height, MinHeight);
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DiagnosticLog.Write("Startup", "Main window initialization started.");
        RestoreWindowPlacement();
        LocalizationService.Apply(this);
        _initializing = true;
        _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _relativeTimeTimer.Tick += RelativeTimeTimer_Tick;
        _scanCountdownTimer.Interval = TimeSpan.FromSeconds(1);
        _scanCountdownTimer.Tick += ScanCountdownTimer_Tick;
        SettingsService settingsService;
        UiPreferencesService uiPreferencesService;
        double articleListWidthRatio;
        double navigationPaneWidth;
        bool navigationPaneCollapsed;
        AppSettings settings;
        AudioLibraryService library;
        NewsGenerationService generator;
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.yaml");
            settingsService = new SettingsService(settingsPath);
            uiPreferencesService = new UiPreferencesService();
            articleListWidthRatio = uiPreferencesService.LoadArticleListWidthRatio(settingsPath);
            navigationPaneWidth = uiPreferencesService.LoadNavigationPaneWidth();
            navigationPaneCollapsed = uiPreferencesService.LoadNavigationPaneCollapsed();
            settings = settingsService.Load();
            library = new AudioLibraryService(settings, AppContext.BaseDirectory);
            library.Refresh();
            generator = new NewsGenerationService(settings, AppContext.BaseDirectory, _feedCategoryPreferences);
            _settings = settings;
            ApplyFavoriteCategoryFlags(library.Items);
            DiagnosticLog.Write("Startup", $"Loaded {library.Items.Count} article(s) from {library.OutputDirectory}.");
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
        AudioMuteService.SetMuted(_settings.AudioMuted);
        ThemeService.Apply(_settings.AppearanceTheme);
        ApplyInterfaceTypography();
        UpdateScanModeButton();
        _library = library;
        _generator = generator;
        _articleListWidthRatio = _settings.ArticleListWidthRatio = articleListWidthRatio;
        _articleImageHeightRatio = _settings.ArticleImageHeightRatio;
        _navigationPaneWidth = navigationPaneWidth;
        _navigationPaneCollapsed = navigationPaneCollapsed;
        NavigationSortComboBox.SelectedIndex = 0;
        NavigationAgeComboBox.SelectedIndex = 0;
        NavigationConsumptionComboBox.SelectedIndex = 0;
        ApplyNavigationPaneLayout();
        ApplyArticleListWidthRatio(_articleListWidthRatio);
        ArticleList.ItemsSource = _library.Items;
        HookLibraryCounts();
        BuildNavigationTree();
        ShowArticleTextToggle.IsChecked = _settings.ShowArticleText;
        SetUnplayedFilterState(_settings.ShowUnplayedOnly);
        SetStarredOnlyFilterState(_settings.ShowStarredOnly);
        SetFavoriteCategoriesOnlyFilterState(_settings.ShowFavoriteCategoriesOnly);
        SetArticleSortState(_settings.ArticleSortOrder);
        GroupByFeedMenuItem.IsChecked = _settings.GroupArticlesByFeed;
        ApplyArticleListSorting();
        UpdateArticleMediaLayout();
        UpdateFilter();
        StatusText.Text = $"Ready. Audio folder: {_library.OutputDirectory}";
        _positionTimer.Tick += PositionTimer_Tick;
        ConfigureRelativeTimeTimer();
        _initializing = false;
        AudioMuteService.MuteChanged += AudioMuteService_MuteChanged;
        AudioMuteService_MuteChanged(AudioMuteService.IsMuted);
        _trayIconService = new TrayIconService(RestoreFromTray, ExitFromTray);
        _trayIconService.NotificationClicked += () =>
        {
            if (_notificationArticle is not null)
            {
                OpenArticleFromTicker(_notificationArticle, _settings.Ticker.ClickAction);
            }
        };
        UpdateUnreadTrayIndicator();
        StartupSucceeded = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox ||
            Keyboard.FocusedElement is ComboBox { IsEditable: true })
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Space &&
            Keyboard.Modifiers == ModifierKeys.None &&
            _audioSegment == AudioSegment.Body &&
            IsArticleAudioActive())
        {
            e.Handled = true;
            ToggleBodyAudioFromSpace();
            return;
        }

        if (key == Key.Space &&
            Keyboard.Modifiers == ModifierKeys.None &&
            ((_isPlaying && _audioSegment == AudioSegment.Title) || _scanCountdownActive))
        {
            e.Handled = true;
            PlayBodyAudioFromSpace();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control &&
            key is Key.Left or Key.Right)
        {
            e.Handled = true;
            SeekAudio(TimeSpan.FromSeconds(key == Key.Right ? 30 : -10));
            return;
        }

        foreach (var (action, shortcut) in _settings.Hotkeys.GetShortcuts())
        {
            if (!HotkeySettings.TryParse(shortcut, out var gesture) ||
                gesture.Key != key ||
                gesture.Modifiers != Keyboard.Modifiers)
            {
                continue;
            }

            e.Handled = true;
            ExecuteHotkey(action);
            return;
        }
    }

    private void ToggleBodyAudioFromSpace()
    {
        if (_isPlaying)
        {
            PauseButton_Click(this, new RoutedEventArgs());
            return;
        }

        if (AudioMuteService.IsMuted)
        {
            StatusText.Text = "Audio is muted. Unmute to play.";
            return;
        }

        AudioPlayer.Play();
        _positionTimer.Start();
        _isPlaying = true;
        MarkOnStart();
        UpdateSystemMediaTransportControls();
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        e.Handled = true;
        ExecuteHotkey(nameof(HotkeySettings.PlayPause));
    }

    private void ExecuteHotkey(string action)
    {
        var args = new RoutedEventArgs();
        switch (action)
        {
            case nameof(HotkeySettings.PlayPause):
                if (_isPlaying)
                {
                    PauseButton_Click(this, args);
                }
                else
                {
                    PlayButton_Click(this, args);
                }
                break;
            case nameof(HotkeySettings.SkipNext):
                NextButton_Click(this, args);
                break;
            case nameof(HotkeySettings.PreviousBack):
                PreviousButton_Click(this, args);
                break;
            case nameof(HotkeySettings.VolumeUp):
                AdjustPlayerVolume(0.1);
                break;
            case nameof(HotkeySettings.VolumeDown):
                AdjustPlayerVolume(-0.1);
                break;
            case nameof(HotkeySettings.Forward):
                if (_mediaItems.Count > 1 && _mediaIndex < _mediaItems.Count - 1)
                {
                    MoveMedia(1);
                }
                else
                {
                    SeekAudio(TimeSpan.FromSeconds(30));
                }
                break;
            case nameof(HotkeySettings.Backward):
                if (_mediaItems.Count > 1 && _mediaIndex > 0)
                {
                    MoveMedia(-1);
                }
                else
                {
                    SeekAudio(TimeSpan.FromSeconds(-10));
                }
                break;
            case nameof(HotkeySettings.Mute):
                AudioMuteButton_Click(this, args);
                break;
            case nameof(HotkeySettings.FullMode):
                SetScanMode(false);
                break;
            case nameof(HotkeySettings.ScanMode):
                SetScanMode(true);
                break;
            case nameof(HotkeySettings.RefreshLibrary):
                GenerateButton_Click(this, args);
                break;
            case nameof(HotkeySettings.RefreshArticle):
                RebuildArticleButton_Click(this, args);
                break;
            case nameof(HotkeySettings.MarkAllAsRead):
                MarkAllReadButton_Click(this, args);
                break;
            case nameof(HotkeySettings.Settings):
                SettingsButton_Click(this, args);
                break;
            default:
                throw new InvalidOperationException($"Unknown hotkey action: {action}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DiagnosticLog.Write("Startup", "Main window loaded.");
        if (StartMinimizedToTray)
        {
            HideToTray();
        }

        ConfigureFeedUpdateTimer();
        var splashVideoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AnchorFeeds_splash.mp4");
        if (!_splashVideoStopped && File.Exists(splashVideoPath))
        {
            DiagnosticLog.Write("Startup", $"Starting splash video: {splashVideoPath}");
            StartupSplashVideo.Volume = 0;
            StartupSplashVideo.Source = new Uri(splashVideoPath);
            StartupSplashVideo.Play();
        }
        else if (!File.Exists(splashVideoPath))
        {
            _splashVideoFailed = true;
            StatusText.Text = $"Startup splash video was not found: {splashVideoPath}";
            UpdateArticleMediaLayout();
        }

        if (_settings.RetrieveLatestFeedsOnLaunch)
        {
            await RetrieveLatestFeedsAsync();
        }
    }

    private void StartupSplashVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is null && !_splashVideoStopped)
        {
            StartupSplashVideo.Play();
        }
    }

    private void StartupSplashVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is null && !_splashVideoStopped)
        {
            _splashVideoLoopCount++;
            if (_splashVideoLoopCount >= 1)
            {
                StopStartupSplash();
                UpdateArticleMediaLayout();
            }
            else
            {
                StartupSplashVideo.Position = TimeSpan.Zero;
                StartupSplashVideo.Play();
            }
        }
    }

    private void StopStartupSplash()
    {
        if (_splashVideoStopped)
        {
            return;
        }

        _splashVideoStopped = true;
        StartupSplashVideo.Stop();
        if (IsLoaded)
        {
            UpdateArticleMediaLayout();
        }
    }

    private void StartupSplashVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DiagnosticLog.Exception("Startup splash video failed", e.ErrorException);
        _splashVideoFailed = true;
        StatusText.Text = $"Could not play startup splash video: {e.ErrorException.Message}";
        UpdateArticleMediaLayout();
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRetrievingFeeds && !_isRebuildingArticle)
        {
            DiagnosticLog.Write("Feeds", "Feed update cancellation requested by user.");
            _feedUpdateCancellation?.Cancel();
            GenerateButton.IsEnabled = false;
            StatusText.Text = "Pausing feed update...";
            return;
        }

        await RetrieveLatestFeedsAsync();
    }

    private async Task RetrieveLatestFeedsAsync(bool automatic = false)
    {
        if (_isRetrievingFeeds)
        {
            return;
        }

        _isRetrievingFeeds = true;
        _isRebuildingArticle = false;
        DiagnosticLog.Write("Feeds", $"Feed update started. Automatic={automatic}; feeds={_settings.FeedSources.Count}; output={_library.OutputDirectory}.");
        _feedUpdateCancellation = new CancellationTokenSource();
        UpdateGenerateButtonState();
        RebuildArticleButton.IsEnabled = false;
        GenerationProgress.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var generatedArticles = new Progress<ArticleAudio>(article =>
            {
                ApplyFavoriteCategoryFlags([article]);
                var isNewArticle = !_library.Items.Any(existing =>
                    existing.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase));
                _library.AddOrUpdate(article);
                if (isNewArticle && article.IsFavoriteCategory && ShouldShowTicker())
                {
                    NotifyFavoriteArticle(article);
                }
                if (_currentArticle?.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _currentArticle.AudioText = article.AudioText;
                    _currentArticle.ArticleContentHtml = article.ArticleContentHtml;
                    _currentArticle.TitleAudioPath = article.TitleAudioPath;
                    _currentArticle.TitleAudioEngine = article.TitleAudioEngine;
                    _currentArticle.BodyAudioEngine = article.BodyAudioEngine;
                    _currentArticle.Categories = article.Categories;
                    _currentArticle.IsFavoriteCategory = article.IsFavoriteCategory;
                    _currentArticle.CachedImagePath = article.CachedImagePath;
                    _currentArticle.ArticleFolderPath = article.ArticleFolderPath;
                    _currentArticle.SourceLogoUrl = article.SourceLogoUrl;
                    ShowArticleText(article.ArticleContentHtml, article.AudioText);
                    UpdateCurrentState();
                    _ = LoadArticleImageAsync(_currentArticle);
                }
            });
            var result = await _generator.GenerateAsync(
                progress,
                generatedArticles,
                _feedUpdateCancellation.Token,
                automatic: automatic);
            _library.Refresh();
            ApplyFavoriteCategoryFlags();
            UpdateFilter();
            StatusText.Text = $"Generation complete: {result.GeneratedCount} new article(s).";
            DiagnosticLog.Write("Feeds", $"Feed update completed. Generated={result.GeneratedCount}; failed feeds={result.FailedFeeds.Count}; media warnings={result.MediaWarnings.Count}.");
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
                if (!automatic)
                {
                    MessageBox.Show(
                        this,
                        $"Audio generation completed with warnings:\n\n{string.Join("\n\n", warningSections)}",
                        "Feed retrieval or media cache warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (OperationCanceledException) when (_feedUpdateCancellation?.IsCancellationRequested == true)
        {
            DiagnosticLog.Write("Feeds", "Feed update cancelled.");
            _library.Refresh();
            ApplyFavoriteCategoryFlags();
            UpdateFilter();
            StatusText.Text = "Feed update paused. Select Update Feeds or wait for the next scheduled update to resume.";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Exception("Feed update failed", ex);
            var stackLines = (ex.StackTrace ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(6);
            StatusText.Text = $"Generation failed: {ex.Message}";
            if (automatic)
            {
                return;
            }
            MessageBox.Show(
                this,
                $"{ex.Message}\n\n{ex.GetType().FullName}\n{string.Join("\n", stackLines)}",
                "Audio generation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GenerationProgress.Visibility = Visibility.Collapsed;
            _feedUpdateCancellation?.Dispose();
            _feedUpdateCancellation = null;
            _isRetrievingFeeds = false;
            _isRebuildingArticle = false;
            UpdateGenerateButtonState();
            UpdateCurrentState();
        }
    }

    private void UpdateGenerateButtonState()
    {
        if (GenerateButton is null)
        {
            return;
        }

        var canCancelUpdate = _isRetrievingFeeds && !_isRebuildingArticle;
        GenerateButton.IsEnabled = !_isRebuildingArticle &&
            (!canCancelUpdate || _feedUpdateCancellation?.IsCancellationRequested != true);
        GeneratePauseGlyph.Visibility = canCancelUpdate ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.ToolTip = canCancelUpdate ? "Pause Update" : "Update Feeds";
        System.Windows.Automation.AutomationProperties.SetName(
            GenerateButton,
            canCancelUpdate ? "Pause Update" : "Update Feeds");
    }

    private void ConfigureFeedUpdateTimer()
    {
        _feedUpdateTimer.Stop();
        var interval = _settings.FeedSources
            .Where(feed => feed.Enabled)
            .Select(feed => feed.GetEffectiveUpdatePeriod(_settings.FeedUpdateIntervalPeriod))
            .Where(period => period.HasValue)
            .Select(period => period!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Min();
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        _feedUpdateTimer.Interval = interval;
        _feedUpdateTimer.Tick -= FeedUpdateTimer_Tick;
        _feedUpdateTimer.Tick += FeedUpdateTimer_Tick;
        _feedUpdateTimer.Start();
    }

    private async void FeedUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_isRetrievingFeeds || OwnedWindows.Count > 0)
        {
            return;
        }

        await RetrieveLatestFeedsAsync(automatic: true);
    }

    private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var visibleArticles = System.Windows.Data.CollectionViewSource
                .GetDefaultView(_library.Items)
                .Cast<ArticleAudio>()
                .ToList();
            var count = _library.MarkAllPlayed(visibleArticles);
            UpdateCurrentState();
            UpdateFilter();
            StatusText.Text = count == 0
                ? "All currently visible articles are already marked as read."
                : $"Marked {count} currently visible article(s) as read.";
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
                ClearArticleMedia();
                _currentImageLoaded = false;
                ShowArticleText(string.Empty, string.Empty);
                NowPlayingTitle.Text = "Select an article to listen";
                CurrentArticleDateTime.Text = string.Empty;
                PositionSlider.Value = 0;
                PositionText.Text = "0:00";
                DurationText.Text = "0:00";
                UpdateArticleMediaLayout();
                UpdateCurrentState();
                UpdateSystemMediaTransportControls();
            }

            UpdateFilter();
            StatusText.Text = $"Deleted article audio and saved details: {article.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not delete article", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string RebuildLibrary()
    {
        StopCurrentAudio();
        _imageLoadCancellation?.Cancel();
        _currentArticle = null;
        ArticleList.SelectedItem = null;
        var removedCount = _library.Rebuild();
        ResetAfterLibraryChange();
        return $"Library reset: deleted {removedCount} article folder(s) or audio file(s).";
    }

    private string PruneLibrary()
    {
        StopCurrentAudio();
        _imageLoadCancellation?.Cancel();
        _currentArticle = null;
        ArticleList.SelectedItem = null;
        var removed = _library.Prune();
        ResetAfterLibraryChange();
        return $"Pruned {removed} expired article(s). {_library.Items.Count} remain.";
    }

    private void ResetAfterLibraryChange()
    {
        ArticleList.ItemsSource = _library.Items;
        ClearArticleMedia();
        _currentImageLoaded = false;
        ShowArticleText(string.Empty, string.Empty);
        NowPlayingTitle.Text = "Select an article to listen";
        CurrentArticleDateTime.Text = string.Empty;
        PositionSlider.Value = 0;
        PositionText.Text = "0:00";
        DurationText.Text = "0:00";
        UpdateArticleMediaLayout();
        UpdateCurrentState();
        UpdateFilter();
        UpdateSystemMediaTransportControls();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var currentLanguage = _settings.ApplicationLanguage;
        var dialog = new SettingsWindow(_settings, _feedCategoryPreferences)
        {
            Owner = this,
            RebuildLibrary = RebuildLibrary,
            PruneLibrary = PruneLibrary
        };
        if (dialog.ShowDialog() != true || dialog.UpdatedSettings is not { } updatedSettings)
        {
            return;
        }

        try
        {
            DiagnosticLog.Write("Settings", $"Saving settings. Verbose debug logging requested={updatedSettings.DebugLoggingEnabled}.");
            _settingsService.SaveAll(updatedSettings);
            try
            {
                StartupRegistrationService.SetEnabled(updatedSettings.LaunchAtStartup);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Settings were saved, but Windows startup could not be updated: {ex.Message}",
                    "Startup registration failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            StopCurrentAudio();
            _imageLoadCancellation?.Cancel();
            _currentArticle = null;
            ArticleList.SelectedItem = null;
            UpdateSystemMediaTransportControls();
            _settings = _settingsService.Load();
            DiagnosticLog.Configure(_settings.DebugLoggingEnabled);
            DiagnosticLog.Write("Settings", "Settings saved and reloaded.");
            _articleImageHeightRatio = _settings.ArticleImageHeightRatio;
            ThemeService.Apply(_settings.AppearanceTheme);
            ApplyInterfaceTypography();
            UpdateScanModeButton();
            _library = new AudioLibraryService(_settings, AppContext.BaseDirectory);
            _library.Refresh();
            _generator = new NewsGenerationService(_settings, AppContext.BaseDirectory, _feedCategoryPreferences);
            ConfigureFeedUpdateTimer();
            ArticleList.ItemsSource = _library.Items;
            HookLibraryCounts();
            ShowArticleTextToggle.IsChecked = _settings.ShowArticleText;
            SetUnplayedFilterState(_settings.ShowUnplayedOnly);
            SetStarredOnlyFilterState(_settings.ShowStarredOnly);
            ApplyFavoriteCategoryFlags();
            SetFavoriteCategoriesOnlyFilterState(_settings.ShowFavoriteCategoriesOnly);
            SetArticleSortState(_settings.ArticleSortOrder);
            GroupByFeedMenuItem.IsChecked = _settings.GroupArticlesByFeed;
            ApplyArticleListSorting();
            UpdateArticleMediaLayout();
            UpdateCurrentState();
            UpdateFilter();
            UpdateTickerVisibility();
            StatusText.Text = string.Equals(
                currentLanguage,
                _settings.ApplicationLanguage,
                StringComparison.OrdinalIgnoreCase)
                    ? $"Settings saved. Audio folder: {_library.OutputDirectory}"
                    : LocalizationService.Get("Settings saved. Restart the app to apply the selected language.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Exception("Could not apply settings", ex);
            MessageBox.Show(this, ex.Message, "Could not apply settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScanModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetScanMode(!_settings.ScanMode);
    }

    private void SetScanMode(bool scanMode)
    {
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

        ScanModeGlyph.Data = Geometry.Parse(_settings.ScanMode ? ScanModeIconData : FullModeIconData);
        ScanModeButton.ToolTip = _settings.ScanMode
            ? "Scan mode is on. Click for Full mode."
            : "Full mode is on. Click for Scan mode.";
    }

    private void AudioMuteButton_Click(object sender, RoutedEventArgs e)
    {
        AudioMuteService.SetMuted(!AudioMuteService.IsMuted);
        StatusText.Text = AudioMuteService.IsMuted ? "Audio muted." : "Audio unmuted.";
    }

    private void AudioMuteService_MuteChanged(bool isMuted)
    {
        ApplyPlayerVolume();
        StartupSplashVideo.Volume = 0;
        UpdateAudioMuteButton();
        if (_settings.AudioMuted != isMuted)
        {
            _settings.AudioMuted = isMuted;
            try
            {
                _settingsService.SaveAudioMuted(isMuted);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"The audio mute state changed, but could not be saved for the next session: {ex.Message}",
                    "Could not save audio mute state",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        if (isMuted)
        {
            PageTurnSound.Stop();
            StopCurrentAudio();
        }
    }

    private void SeekAudio(TimeSpan offset)
    {
        if (_currentArticle is null ||
            AudioPlayer.Source is null ||
            _audioSegment is not (AudioSegment.Title or AudioSegment.Body) ||
            !AudioPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var duration = AudioPlayer.NaturalDuration.TimeSpan;
        var target = AudioPlayer.Position + offset;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }
        else if (target > duration)
        {
            target = duration;
        }

        AudioPlayer.Position = target;
        PositionSlider.Value = target.TotalSeconds;
        PositionText.Text = FormatTime(target);
    }

    private void AdjustPlayerVolume(double amount)
    {
        _playerVolume = Math.Clamp(_playerVolume + amount, 0, 1);
        if (AudioMuteService.IsMuted && amount > 0)
        {
            AudioMuteService.SetMuted(false);
        }
        else
        {
            ApplyPlayerVolume();
        }

        StatusText.Text = $"Player volume: {(int)Math.Round(_playerVolume * 100)}%.";
    }

    private void ApplyPlayerVolume()
    {
        var volume = AudioMuteService.IsMuted ? 0 : _playerVolume;
        AudioPlayer.Volume = volume;
    }

    private void UpdateAudioMuteButton()
    {
        if (AudioMuteGlyph is null)
        {
            return;
        }

        AudioMuteGlyph.Data = Geometry.Parse(AudioMuteService.IsMuted ? MutedIconData : UnmutedIconData);
        AudioMuteButton.ToolTip = AudioMuteService.IsMuted ? "Unmute audio" : "Mute audio";
        System.Windows.Automation.AutomationProperties.SetName(
            AudioMuteButton,
            AudioMuteService.IsMuted ? "Unmute audio" : "Mute audio");
    }

    private void ShowArticleTextToggle_Click(object sender, RoutedEventArgs e)
    {
        var showArticleText = ShowArticleTextToggle.IsChecked == true;
        try
        {
            _settingsService.SaveShowArticleText(showArticleText);
            _settings.ShowArticleText = showArticleText;
            UpdateArticleMediaLayout();
        }
        catch (Exception ex)
        {
            ShowArticleTextToggle.IsChecked = _settings.ShowArticleText;
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

    private void AllFeedsNavigationButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationMode(NavigationMode.AllFeeds);

    private void StarredNavigationButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationMode(NavigationMode.Starred);

    private void FavoritesNavigationButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationMode(NavigationMode.Favorites);

    private void SearchNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        SetNavigationMode(NavigationMode.Search);
        NavigationSearchTextBox.Focus();
    }

    private void HistoryNavigationButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationMode(NavigationMode.History);

    private void SetNavigationMode(NavigationMode mode)
    {
        _navigationMode = mode;
        _selectedFeedCategories.Clear();
        SearchOptionsPanel.Visibility = mode is NavigationMode.Search or NavigationMode.History
            ? Visibility.Visible
            : Visibility.Collapsed;
        BuildNavigationTree();
        NavigationSearchTextBox.Visibility = mode == NavigationMode.Search
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateFilter();
    }

    private void NavigationSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_navigationMode == NavigationMode.Search)
        {
            UpdateFilter();
        }
    }

    private void NavigationOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navigationMode is NavigationMode.Search or NavigationMode.History)
        {
            UpdateFilter();
        }
    }

    private void BuildNavigationTree()
    {
        if (NavigationTreeItems is null || _library is null)
        {
            return;
        }

        NavigationTreeItems.Children.Clear();
        var feedGroups = _library.Items
            .GroupBy(article => string.IsNullOrWhiteSpace(article.FeedUrl)
                ? article.FeedDisplayName
                : article.FeedUrl, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().FeedDisplayName, StringComparer.CurrentCultureIgnoreCase);
        var sourcesPanel = new StackPanel();
        foreach (var feedGroup in feedGroups)
        {
            var articles = feedGroup.ToList();
            var feed = articles[0];
            var categoriesPanel = new StackPanel();
            var categoryNames = articles
                .SelectMany(article => article.Categories)
                .Where(FeedCategoryPreferencesService.IsDisplayableCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase);
            foreach (var category in categoryNames)
            {
                var count = articles.Count(article => article.Categories.Any(value =>
                    value.Equals(category, StringComparison.OrdinalIgnoreCase)));
                var categoryButton = new Button
                {
                    Content = $"{category}  [{count}]",
                    Tag = new FeedCategorySelection(GetFeedIdentity(feed), category),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(18, 4, 4, 4),
                    Margin = new Thickness(0, 1, 0, 1),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontWeight = IsFeedCategorySelected(feed, category)
                        ? FontWeights.SemiBold
                        : FontWeights.Normal
                };
                categoryButton.Click += FeedCategoryNavigationButton_Click;
                categoriesPanel.Children.Add(categoryButton);
            }

            var feedButton = new Button
            {
                Content = $"{feed.FeedDisplayName}  [{articles.Count}]",
                Tag = new FeedCategorySelection(GetFeedIdentity(feed), null),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 4, 5),
                Margin = new Thickness(0, 2, 0, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontWeight = IsFeedSelected(feed) ? FontWeights.SemiBold : FontWeights.Normal
            };
            feedButton.Click += FeedNavigationButton_Click;

            var categoriesExpander = new Expander
            {
                Header = "Categories",
                Content = categoriesPanel,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var feedGroupPanel = new StackPanel();
            feedGroupPanel.Children.Add(feedButton);
            feedGroupPanel.Children.Add(categoriesExpander);
            sourcesPanel.Children.Add(feedGroupPanel);
        }

        NavigationTreeItems.Children.Add(new Expander
        {
            Header = "Sources",
            Content = sourcesPanel,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    private void FeedCategoryNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FeedCategorySelection selection })
        {
            SelectFeedCategory(selection, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
    }

    private void FeedNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FeedCategorySelection selection })
        {
            SelectFeedCategory(selection, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
    }

    private void SelectFeedCategory(FeedCategorySelection selection, bool addToSelection)
    {
        _navigationMode = NavigationMode.AllFeeds;
        if (!addToSelection)
        {
            _selectedFeedCategories.Clear();
        }

        if (!_selectedFeedCategories.Any(selected => AreSelectionsEqual(selected, selection)))
        {
            _selectedFeedCategories.Add(selection);
        }

        SearchOptionsPanel.Visibility = Visibility.Collapsed;
        BuildNavigationTree();
        UpdateFilter();
    }

    private static bool AreSelectionsEqual(FeedCategorySelection left, FeedCategorySelection right) =>
        left.FeedIdentity.Equals(right.FeedIdentity, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);

    private bool IsFeedSelected(ArticleAudio feed) =>
        _selectedFeedCategories.Any(selection =>
            selection.FeedIdentity.Equals(GetFeedIdentity(feed), StringComparison.OrdinalIgnoreCase) &&
            selection.Category is null);

    private bool IsFeedCategorySelected(ArticleAudio feed, string category) =>
        _selectedFeedCategories.Any(selection =>
            selection.FeedIdentity.Equals(GetFeedIdentity(feed), StringComparison.OrdinalIgnoreCase) &&
            (selection.Category is null ||
             selection.Category.Equals(category, StringComparison.OrdinalIgnoreCase)));

    private static string GetFeedIdentity(ArticleAudio article) =>
        string.IsNullOrWhiteSpace(article.FeedUrl) ? article.FeedDisplayName : article.FeedUrl;

    private sealed record FeedCategorySelection(string FeedIdentity, string? Category);

    private void ExportLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = (UIElement)sender;
            menu.IsOpen = true;
        }
    }

    private async void ExportTitlesOnlyMenuItem_Click(object sender, RoutedEventArgs e) =>
        await ExportVisibleLibraryAsync(includeBodies: false);

    private async void ExportTitlesAndBodiesMenuItem_Click(object sender, RoutedEventArgs e) =>
        await ExportVisibleLibraryAsync(includeBodies: true);

    private async Task ExportVisibleLibraryAsync(bool includeBodies)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_library.Items);
        var visibleArticles = view.Cast<ArticleAudio>().ToList();
        if (visibleArticles.Count == 0)
        {
            MessageBox.Show(this, "There are no articles in the current Feeds Library view to export.",
                "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Feeds Library audio",
            Filter = "MP3 audio (*.mp3)|*.mp3",
            DefaultExt = ".mp3",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"AnchorFeeds_{DateTime.Now:yyyyMMdd-HH-mm}.mp3"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportLibraryButton.IsEnabled = false;
        ExportProgressText.Text = "Preparing the audio playlist...";
        ExportProgressText.Visibility = Visibility.Visible;
        ExportProgressBar.Value = 0;
        ExportProgressBar.IsIndeterminate = true;
        ExportProgressBar.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<PlaylistExportProgress>(update =>
            {
                ExportProgressText.Text = update.Status;
                ExportProgressBar.IsIndeterminate = update.PercentComplete is null;
                if (update.PercentComplete is { } percent)
                {
                    ExportProgressBar.Value = Math.Clamp(percent, 0, 100);
                }
            });
            var result = await _playlistExportService.ExportAsync(
                visibleArticles,
                includeBodies,
                dialog.FileName,
                _settings.Mp3BitrateKbps,
                progress);
            ExportProgressText.Text = "Export complete.";
            ExportProgressBar.IsIndeterminate = false;
            ExportProgressBar.Value = 100;
            var skippedText = result.SkippedArticles > 0
                ? $" {result.SkippedArticles} article(s) without matching audio were skipped."
                : string.Empty;
            MessageBox.Show(this,
                $"Exported {result.ExportedArticles} article(s) to:{Environment.NewLine}{dialog.FileName}{skippedText}",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ExportProgressText.Text = "Export failed.";
            ExportProgressBar.IsIndeterminate = false;
            MessageBox.Show(this, ex.Message, "Could not export Feeds Library audio",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportLibraryButton.IsEnabled = true;
        }
    }

    private void CollapseNavigationPaneButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationPaneCollapsed(true);

    private void ExpandNavigationPaneButton_Click(object sender, RoutedEventArgs e) =>
        SetNavigationPaneCollapsed(false);

    private void SetNavigationPaneCollapsed(bool collapsed)
    {
        if (_navigationPaneCollapsed == collapsed)
        {
            return;
        }

        if (!collapsed && _navigationPaneWidth < UiPreferencesService.DefaultNavigationPaneWidth)
        {
            _navigationPaneWidth = UiPreferencesService.DefaultNavigationPaneWidth;
        }

        _navigationPaneCollapsed = collapsed;
        ApplyNavigationPaneLayout();
        try
        {
            _uiPreferencesService.SaveNavigationPaneCollapsed(collapsed);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save navigation-pane state",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyNavigationPaneLayout()
    {
        NavigationPaneColumn.MinWidth = _navigationPaneCollapsed ? 34 : 160;
        NavigationPaneColumn.Width = new GridLength(
            _navigationPaneCollapsed ? 34 : _navigationPaneWidth);
        NavigationPaneContent.Visibility = _navigationPaneCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        ExpandNavigationPaneButton.Visibility = _navigationPaneCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        NavigationPaneSplitter.Visibility = _navigationPaneCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void NavigationPaneSplitter_DragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _navigationPaneWidth = Math.Clamp(NavigationPaneColumn.ActualWidth, 160, 600);
        try
        {
            _uiPreferencesService.SaveNavigationPaneWidth(_navigationPaneWidth);
        }
        catch (Exception ex)
        {
            NavigationPaneColumn.Width = new GridLength(_navigationPaneWidth);
            MessageBox.Show(this, ex.Message, "Could not save navigation-pane width",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ArticleAudio.FeedDisplayName)));
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

    private void FavoriteCategoriesOnlyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var showFavoritesOnly = FavoriteCategoriesOnlyMenuItem.IsChecked;
        if (_syncingUnplayedFilter || _initializing)
        {
            UpdateFilter();
            return;
        }

        try
        {
            _settingsService.SaveShowFavoriteCategoriesOnly(showFavoritesOnly);
            _settings.ShowFavoriteCategoriesOnly = showFavoritesOnly;
        }
        catch (Exception ex)
        {
            FavoriteCategoriesOnlyMenuItem.IsChecked = _settings.ShowFavoriteCategoriesOnly;
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

    private void SetFavoriteCategoriesOnlyFilterState(bool enabled)
    {
        _syncingUnplayedFilter = true;
        try
        {
            FavoriteCategoriesOnlyMenuItem.IsChecked = enabled;
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

    private void ArticleImageTextSplitter_DragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(SaveArticleImageHeightRatio));
    }

    private void SaveArticleImageHeightRatio()
    {
        var availableHeight = ArticleImageRow.ActualHeight + ArticleTextRow.ActualHeight;
        if (availableHeight <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(ArticleImageRow.ActualHeight / availableHeight, 0.2, 0.8);
        try
        {
            _settingsService.SaveArticleImageHeightRatio(ratio);
            _articleImageHeightRatio = _settings.ArticleImageHeightRatio = ratio;
            ApplyArticleImageHeightRatio(ratio);
        }
        catch (Exception ex)
        {
            ApplyArticleImageHeightRatio(_articleImageHeightRatio);
            MessageBox.Show(this, ex.Message, "Could not save divider position", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyArticleImageHeightRatio(double ratio)
    {
        ArticleImageRow.Height = new GridLength(ratio, GridUnitType.Star);
        ArticleTextRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArticleList.SelectedItems.Count == 1 && ArticleList.SelectedItem is ArticleAudio single)
        {
            _selectionAnchor = single;
        }

        var multiSelectGesture = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (_initializing ||
            multiSelectGesture ||
            ArticleList.SelectedItems.Count != 1 ||
            ArticleList.SelectedItem is not ArticleAudio selected)
        {
            return;
        }

        DiagnosticLog.Write("Playback", $"Article selected: title=\"{selected.Title}\", body=\"{selected.FilePath}\", title audio=\"{selected.TitleAudioPath}\".");
        SelectArticle(selected);
    }

    private void SelectArticle(ArticleAudio selected)
    {
        DiagnosticLog.Write("Playback", $"Loading article into view: \"{selected.Title}\".");
        StopStartupSplash();
        var changingArticle = _currentArticle is not null && !ReferenceEquals(_currentArticle, selected);
        if (changingArticle)
        {
            _stopAfterCurrentArticle = false;
            MarkCurrentAsSkipped();
            StopCurrentAudio();
        }

        _currentArticle = selected;
        _library.RecordViewed(selected);
        UpdatePlayPauseButton();
        NowPlayingTitle.Text = selected.Title;
        CurrentArticleDateTime.Text = selected.DisplayDateTime;
        UpdateSystemMediaTransportControls();
        if (selected.HasRedundantBodyAudio && string.IsNullOrWhiteSpace(selected.ArticleContentHtml))
        {
            ShowArticleText(string.Empty, string.Empty);
        }
        else
        {
            ShowArticleText(
                selected.ArticleContentHtml,
                string.IsNullOrWhiteSpace(selected.AudioText)
                    ? "Article text is not available yet. Select Retrieve Latest Feeds to update saved article details."
                    : selected.AudioText);
        }
        FindArticleTextScrollViewer()?.ScrollToTop();
        UpdateCurrentState();
        ArticleImage.Cursor = TryGetArticleUri(selected.ArticleUrl, out _) ? Cursors.Hand : Cursors.Arrow;
        PositionSlider.Value = 0;
        PositionText.Text = "0:00";
        DurationText.Text = "0:00";
        var playBodyOnly = _playBodyAudioOnNextSelection;
        _playBodyAudioOnNextSelection = false;
        if (!_loadOnlyNoPlayback)
        {
            DiagnosticLog.Write("Playback", $"Starting playback after article selection: \"{selected.Title}\".");
            PlaySelected(bodyOnly: playBodyOnly);
        }

        _ = LoadArticleImageAsync(selected);
    }

    private void ShowArticleText(string? articleHtml, string? fallbackText)
    {
        var document = ArticleContentFormatter.CreateDocument(
            articleHtml,
            fallbackText,
            _settings.ArticleTextFontFamily,
            _settings.ArticleTextFontSize);
        document.SetResourceReference(TextElement.ForegroundProperty, "ArticleTextForegroundBrush");
        ArticleTextContent.Document = document;
    }

    private void ArticleTextContent_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = !string.IsNullOrWhiteSpace(GetSelectedArticleText());
        ArticleTextSearchMenuItem.IsEnabled = hasSelection;
        ArticleTextCopyMenuItem.IsEnabled = hasSelection;
    }

    private void ArticleTextSearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string searchEngine })
        {
            return;
        }

        var selectedText = GetSelectedArticleText();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        var encodedText = Uri.EscapeDataString(selectedText);
        var searchUrl = searchEngine switch
        {
            "google" => $"https://www.google.com/search?q={encodedText}",
            "bing" => $"https://www.bing.com/search?q={encodedText}",
            "duckduckgo" => $"https://duckduckgo.com/?q={encodedText}",
            "brave" => $"https://search.brave.com/search?q={encodedText}",
            _ => throw new InvalidOperationException($"Unsupported search engine: {searchEngine}")
        };

        try
        {
            Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open search: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Could not open search", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ArticleTextCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = GetSelectedArticleText();
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(selectedText);
            StatusText.Text = "Selected article text copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not copy article text: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Could not copy article text", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetSelectedArticleText()
    {
        var selection = ArticleTextContent.Selection;
        return selection is null
            ? string.Empty
            : new TextRange(selection.Start, selection.End).Text.Trim();
    }

    private void ApplyInterfaceTypography()
    {
        ArticleList.FontFamily = new FontFamily(_settings.ArticleListFontFamily);
        ArticleList.FontSize = _settings.ArticleListFontSize;
    }

    private void RelativeTimeTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var article in _library.Items)
        {
            article.RefreshRelativeDateTime();
        }

        ConfigureRelativeTimeTimer();
    }

    private void ConfigureRelativeTimeTimer()
    {
        if (_library.Items.Count == 0)
        {
            _relativeTimeTimer.Stop();
            return;
        }

        var now = DateTime.Now;
        _relativeTimeTimer.Interval = _library.Items
            .Select(article => article.GetNextRelativeDateTimeChange(now))
            .Min();
        if (!_relativeTimeTimer.IsEnabled)
        {
            _relativeTimeTimer.Start();
        }
    }

    private ScrollViewer? FindArticleTextScrollViewer()
    {
        ArticleTextContent.ApplyTemplate();
        return FindDescendantScrollViewer(ArticleTextContent);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var descendant = FindDescendantScrollViewer(VisualTreeHelper.GetChild(element, index));
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ArticleImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mediaItems.Count > 0 &&
            _mediaItems[_mediaIndex] is { Kind: ArticleMediaKind.YouTubeVideo } video)
        {
            try
            {
                Process.Start(new ProcessStartInfo(video.WatchUrl) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                StatusText.Text = $"Could not open the video: {ex.Message}";
            }

            return;
        }

        if (OpenCurrentArticleInBrowser())
        {
            e.Handled = true;
        }
    }

    private void ClearArticleMedia()
    {
        _mediaItems.Clear();
        _mediaIndex = 0;
        _mediaNavigated = false;
        _primaryBitmap = null;
        ArticleImage?.Source = null;
        if (ArticleImage is null || MediaNavigationPanel is null)
        {
            return;
        }

        ArticleImage.Visibility = Visibility.Visible;
        MediaNavigationPanel.Visibility = Visibility.Collapsed;
        OpenVideoButton.Visibility = Visibility.Collapsed;
    }

    private void PrepareExtraMedia(ArticleAudio article)
    {
        var extras = ArticleMediaExtractor.Extract(
            article.ArticleContentHtml,
            article.ImageUrl,
            article.SourceLogoUrl);
        if (extras.Count == 0)
        {
            return;
        }

        _mediaItems.AddRange(extras);
        ShowCurrentMedia();
    }

    private void SetPrimaryImage(BitmapSource bitmap)
    {
        _primaryBitmap = bitmap;
        if (_mediaItems.Count == 0 || _mediaItems[0].Kind != ArticleMediaKind.PrimaryImage)
        {
            _mediaItems.Insert(0, new ArticleMediaItem(ArticleMediaKind.PrimaryImage, string.Empty));
            _mediaIndex = _mediaNavigated ? _mediaIndex + 1 : 0;
        }

        ShowCurrentMedia();
    }

    private void MediaPreviousButton_Click(object sender, RoutedEventArgs e) => MoveMedia(-1);

    private void MediaNextButton_Click(object sender, RoutedEventArgs e) => MoveMedia(1);

    private void MoveMedia(int step)
    {
        if (_mediaItems.Count < 2)
        {
            return;
        }

        _mediaNavigated = true;
        _mediaIndex = (_mediaIndex + step + _mediaItems.Count) % _mediaItems.Count;
        ShowCurrentMedia();
    }

    private void ShowCurrentMedia()
    {
        if (_mediaItems.Count == 0)
        {
            OpenVideoButton.Visibility = Visibility.Collapsed;
            return;
        }

        var item = _mediaItems[_mediaIndex];
        MediaNavigationPanel.Visibility = _mediaItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        MediaPositionText.Text = $"{_mediaIndex + 1} / {_mediaItems.Count}" +
            (item.Kind == ArticleMediaKind.YouTubeVideo ? " · Video" : string.Empty);
        OpenVideoButton.Visibility = _mediaItems.Any(media => media.Kind == ArticleMediaKind.YouTubeVideo)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var buttonVideo = item.Kind == ArticleMediaKind.YouTubeVideo
            ? item
            : _mediaItems.FirstOrDefault(media => media.Kind == ArticleMediaKind.YouTubeVideo);
        if (buttonVideo is not null)
        {
            OpenVideoButton.Content = CreateVideoHostIcon(buttonVideo.Host);
            OpenVideoButton.ToolTip = $"Open video on {buttonVideo.Host}";
        }
        UpdateArticleMediaLayout();

        if (item.Kind == ArticleMediaKind.YouTubeVideo)
        {
            ArticleImage.Visibility = Visibility.Visible;
            ArticleImage.Source = CreateRemoteBitmap(item.YouTubeThumbnailUrl, item);
            return;
        }

        ArticleImage.Visibility = Visibility.Visible;
        ArticleImage.Source = item.Kind == ArticleMediaKind.PrimaryImage
            ? _primaryBitmap
            : CreateRemoteBitmap(item.Url, item);
    }

    private BitmapImage? CreateRemoteBitmap(string url, ArticleMediaItem owner)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        if (bitmap.IsDownloading)
        {
            bitmap.DownloadFailed += (_, _) => RemoveFailedMedia(owner);
        }

        return bitmap;
    }

    private void RemoveFailedMedia(ArticleMediaItem item)
    {
        var index = _mediaItems.IndexOf(item);
        if (index < 0 || item.Kind == ArticleMediaKind.PrimaryImage)
        {
            return;
        }

        var current = _mediaItems[_mediaIndex];
        _mediaItems.RemoveAt(index);
        if (_mediaItems.Count == 0)
        {
            ClearArticleMedia();
            UpdateArticleMediaLayout();
            return;
        }

        _mediaIndex = ReferenceEquals(current, item)
            ? Math.Min(index, _mediaItems.Count - 1)
            : _mediaItems.IndexOf(current);
        ShowCurrentMedia();
    }

    private static UIElement CreateVideoHostIcon(VideoHost host)
    {
        var (color, bitchuteStyle) = host switch
        {
            VideoHost.Rumble => (Color.FromRgb(0x85, 0xC7, 0x42), false),
            VideoHost.BitChute => (Color.FromRgb(0xEF, 0x41, 0x36), true),
            _ => (Color.FromRgb(0xFF, 0x00, 0x00), false)
        };

        System.Windows.Shapes.Shape background = host == VideoHost.Rumble
            ? new System.Windows.Shapes.Ellipse()
            : new System.Windows.Shapes.Rectangle { RadiusX = bitchuteStyle ? 8 : 12, RadiusY = bitchuteStyle ? 8 : 12 };
        background.Fill = new SolidColorBrush(color);
        background.Width = host == VideoHost.Rumble ? 44 : 56;
        background.Height = 44;

        var triangle = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 16,10 L 0,20 Z"),
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0)
        };

        var grid = new Grid
        {
            Width = background.Width,
            Height = background.Height,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.5
            }
        };
        grid.Children.Add(background);
        grid.Children.Add(triangle);
        return grid;
    }

    private void OpenVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaItems.Count == 0)
        {
            return;
        }

        var video = _mediaItems[_mediaIndex].Kind == ArticleMediaKind.YouTubeVideo
            ? _mediaItems[_mediaIndex]
            : _mediaItems.FirstOrDefault(item => item.Kind == ArticleMediaKind.YouTubeVideo);
        if (video is null)
        {
            return;
        }

        if (_isPlaying)
        {
            PauseButton_Click(sender, e);
        }

        try
        {
            Process.Start(new ProcessStartInfo(video.WatchUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = $"Could not open the video: {ex.Message}";
        }
    }

    private void LoadInBrowserButton_Click(object sender, RoutedEventArgs e) => OpenCurrentArticleInBrowser();

    private void AiChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.AiChatEnabled ||
            _currentArticle is null ||
            AiChatButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = AiChatButton;
        menu.IsOpen = true;
    }

    private void AiChatMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string agent } ||
            _currentArticle is not { } article)
        {
            return;
        }

        var homeUrl = agent switch
        {
            "chatgpt" => "https://chatgpt.com/",
            "claude" => "https://claude.ai/new",
            "copilot" => "https://copilot.microsoft.com/",
            "perplexity" => "https://www.perplexity.ai/",
            "grok" => "https://grok.com/",
            "gemini" => "https://gemini.google.com/",
            _ => throw new InvalidOperationException($"Unsupported AI chat agent: {agent}")
        };
        var query = BuildAiQuery(article);
        var encodedQuery = Uri.EscapeDataString(query);
        var launchUrl = agent switch
        {
            "chatgpt" => $"https://chatgpt.com/?q={encodedQuery}",
            "claude" => $"https://claude.ai/new?q={encodedQuery}",
            "copilot" => $"https://copilot.microsoft.com/?q={encodedQuery}",
            "perplexity" => $"https://www.perplexity.ai/search?q={encodedQuery}",
            "grok" => $"https://grok.com/?q={encodedQuery}",
            _ => homeUrl
        };

        try
        {
            Clipboard.SetText(agent == "gemini"
                ? BuildAiClipboardText(article, query)
                : BuildAiBodyClipboardText(article));
            Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
            StatusText.Text = agent == "gemini"
                ? "Opening Google Gemini. The AI query and article body were copied to the clipboard."
                : $"Opening {agent}. The article body was copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open AI chat: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Could not open AI chat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildAiBodyClipboardText(ArticleAudio article)
    {
        return string.IsNullOrWhiteSpace(article.AudioText)
            ? "(No article body text is available.)"
            : article.AudioText.Trim();
    }

    private string BuildAiClipboardText(ArticleAudio article, string query)
    {
        return $"{query}\n\nArticle body:\n{BuildAiBodyClipboardText(article)}";
    }

    private string BuildAiQuery(ArticleAudio article)
    {
        var source = string.IsNullOrWhiteSpace(article.ArticleUrl) ? string.Empty : article.ArticleUrl.Trim();
        return $"{_settings.AiPromptInstruction.Trim()}\n\n{article.Title}\n\n{source}";
    }

    private void ShareArticleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string platform } ||
            _currentArticle is not { } article ||
            !TryGetArticleUri(article.ArticleUrl, out var articleUri))
        {
            return;
        }

        var encodedTitle = Uri.EscapeDataString(article.Title);
        var encodedUrl = Uri.EscapeDataString(articleUri.AbsoluteUri);
        var shareUri = platform switch
        {
            "x" => $"https://x.com/intent/post?text={encodedTitle}&url={encodedUrl}",
            "facebook" => $"https://www.facebook.com/sharer/sharer.php?u={encodedUrl}&quote={encodedTitle}",
            "email" => $"mailto:?subject={encodedTitle}&body={Uri.EscapeDataString($"{article.Title}{Environment.NewLine}{Environment.NewLine}{articleUri.AbsoluteUri}")}",
            _ => throw new InvalidOperationException($"Unsupported article sharing platform: {platform}")
        };

        try
        {
            Process.Start(new ProcessStartInfo(shareUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open share composer: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Could not share article", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadInBrowserButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_currentArticle is null || string.IsNullOrWhiteSpace(_currentArticle.ArticleFolderPath))
        {
            return;
        }

        var articleFolder = Path.GetFullPath(_currentArticle.ArticleFolderPath);
        if (!Directory.Exists(articleFolder))
        {
            MessageBox.Show(
                this,
                $"The article folder does not exist: {articleFolder}",
                "Could not open article folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(articleFolder);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Could not open article folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RebuildArticleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArticle is not { } article || _isRetrievingFeeds)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Rebuild \"{article.Title}\" from its current feed entry?\n\nThis permanently replaces its audio and cached media, including images. This may take a few moments.",
            "Rebuild Article",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await RebuildArticleAsync(article, ttsEngineOverride: null);
    }

    private enum MissingAudioChoice
    {
        Cancel,
        RetryEdgeTts,
        UseWindowsSpeech
    }

    private MissingAudioChoice PromptMissingAudio(ArticleAudio article)
    {
        var choice = MissingAudioChoice.Cancel;
        var dialog = new Window
        {
            Title = "Audio missing",
            Owner = IsVisible ? this : null,
            WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 480,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBackgroundBrush");
        var message = new TextBlock
        {
            Text = $"The audio for \"{article.Title}\" could not be generated.\n\n{article.AudioError}\n\nHow would you like to continue?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button CreateButton(string text, MissingAudioChoice result, bool isCancel = false)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = isCancel };
            button.Click += (_, _) =>
            {
                choice = result;
                dialog.DialogResult = true;
            };
            return button;
        }

        var retry = CreateButton("Rebuild (Edge TTS)", MissingAudioChoice.RetryEdgeTts);
        retry.IsDefault = true;
        buttons.Children.Add(retry);
        buttons.Children.Add(CreateButton("Use Windows Speech", MissingAudioChoice.UseWindowsSpeech));
        buttons.Children.Add(CreateButton("Cancel", MissingAudioChoice.Cancel, isCancel: true));
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(message);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.ShowDialog();
        return choice;
    }

    private async Task HandleMissingAudioAsync(ArticleAudio article)
    {
        if (_isRetrievingFeeds)
        {
            StatusText.Text = "Wait for the current update to finish, then try again.";
            return;
        }

        var target = _library.Items.FirstOrDefault(existing =>
            existing.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase)) ?? article;
        switch (PromptMissingAudio(target))
        {
            case MissingAudioChoice.RetryEdgeTts:
                await RebuildArticleAsync(target, ttsEngineOverride: AppSettings.EdgeTtsOnlyEngine);
                break;
            case MissingAudioChoice.UseWindowsSpeech:
                await RebuildArticleAsync(target, ttsEngineOverride: AppSettings.WindowsSpeechEngine);
                break;
        }
    }

    private async Task RebuildArticleAsync(ArticleAudio article, string? ttsEngineOverride)
    {
        _isRetrievingFeeds = true;
        _isRebuildingArticle = true;
        UpdateGenerateButtonState();
        RebuildArticleButton.IsEnabled = false;
        GenerationProgress.Visibility = Visibility.Visible;
        StopCurrentAudio();
        _imageLoadCancellation?.Cancel();
        ClearArticleMedia();
        _currentImageLoaded = false;
        UpdateArticleMediaLayout();

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var generatedArticles = new Progress<ArticleAudio>(rebuiltArticle =>
                _library.AddOrUpdate(rebuiltArticle));
            var result = await _generator.GenerateAsync(
                progress,
                generatedArticles,
                CancellationToken.None,
                article,
                ttsEngineOverride: ttsEngineOverride);

            _currentArticle = null;
            _library.Refresh();
            ApplyFavoriteCategoryFlags();
            UpdateFilter();
            var rebuilt = _library.Items.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(article.ArticleUrl)
                    ? string.Equals(item.ArticleUrl, article.ArticleUrl, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(item.Title, article.Title, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(item.Feed, article.Feed, StringComparison.OrdinalIgnoreCase));
            if (rebuilt is null)
            {
                throw new InvalidOperationException(
                    $"The audio was rebuilt, but \"{article.Title}\" could not be found in the refreshed library.");
            }

            _loadOnlyNoPlayback = true;
            try
            {
                ArticleList.SelectedItem = rebuilt;
                if (!ReferenceEquals(_currentArticle, rebuilt))
                {
                    SelectArticle(rebuilt);
                }
            }
            finally
            {
                _loadOnlyNoPlayback = false;
            }

            StatusText.Text = rebuilt.HasAudioError
                ? $"Audio could not be generated for {rebuilt.Title}: {rebuilt.AudioError}"
                : $"Rebuilt audio and media: {rebuilt.Title}";
            if (result.MediaWarnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    $"The article audio was rebuilt, but some media could not be downloaded:\n\n{string.Join("\n\n", result.MediaWarnings)}",
                    "Article rebuilt with media warnings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Article rebuild failed: {ex.Message}";
            MessageBox.Show(
                this,
                $"{ex.Message}\n\n{ex.GetType().FullName}",
                "Could not rebuild article",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GenerationProgress.Visibility = Visibility.Collapsed;
            _isRetrievingFeeds = false;
            _isRebuildingArticle = false;
            UpdateGenerateButtonState();
            UpdateCurrentState();
        }
    }

    private bool OpenCurrentArticleInBrowser()
    {
        if (_currentArticle is null || !OpenArticleInBrowser(_currentArticle, this))
        {
            return false;
        }

        _stopAfterCurrentArticle = true;
        if (_scanCountdownActive)
        {
            _scanCountdownTimer.Stop();
            _scanCountdownActive = false;
            ScanCountdownOverlay.Visibility = Visibility.Collapsed;
            UpdateArticleMediaLayout();
        }

        return true;
    }

    private static bool OpenArticleInBrowser(ArticleAudio article, Window owner)
    {
        if (!TryGetArticleUri(article.ArticleUrl, out var articleUri))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(articleUri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(owner, ex.Message, "Could not open article", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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
            if (!OpenCurrentArticleInBrowser())
            {
                StatusText.Text = "This article has no distinct body audio to play.";
            }

            return;
        }

        PlayBodyAudioFromSpace();
    }

    private void PlayBodyAudioFromSpace()
    {
        if (_currentArticle is not { HasUsableBodyAudio: true } article)
        {
            StatusText.Text = "This article has no distinct body audio to play.";
            return;
        }

        var bodyPath = article.FilePath;
        DiagnosticLog.Write("Playback", $"Opening body audio for \"{article.Title}\": {bodyPath}.");
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

    private ArticleAudio? _selectionAnchor;

    private void ArticleTitleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (sender is not Button { Tag: ArticleAudio article } ||
            (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
        {
            return;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            var anchor = _selectionAnchor ?? ArticleList.SelectedItem as ArticleAudio ?? article;
            var from = ArticleList.Items.IndexOf(anchor);
            var to = ArticleList.Items.IndexOf(article);
            if (from < 0)
            {
                from = to;
            }

            if ((modifiers & ModifierKeys.Control) == 0)
            {
                ArticleList.SelectedItems.Clear();
            }

            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            {
                if (ArticleList.Items[i] is ArticleAudio item && !ArticleList.SelectedItems.Contains(item))
                {
                    ArticleList.SelectedItems.Add(item);
                }
            }
        }
        else if (ArticleList.SelectedItems.Contains(article))
        {
            ArticleList.SelectedItems.Remove(article);
            _selectionAnchor = article;
        }
        else
        {
            ArticleList.SelectedItems.Add(article);
            _selectionAnchor = article;
        }

        e.Handled = true;
    }

    private ContextMenu CreateMultiSelectContextMenu(FrameworkElement element)
    {
        var count = ArticleList.SelectedItems.Count;
        var contextMenu = new ContextMenu
        {
            PlacementTarget = element,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var markRead = new MenuItem { Header = $"Mark {count} articles as read" };
        markRead.Click += (_, _) => MarkSelectedArticlesRead();
        var delete = new MenuItem { Header = $"Delete {count} articles" };
        delete.Click += (_, _) => DeleteSelectedArticles();
        contextMenu.Items.Add(markRead);
        contextMenu.Items.Add(delete);
        return contextMenu;
    }

    private void MarkSelectedArticlesRead()
    {
        var selected = ArticleList.SelectedItems.OfType<ArticleAudio>().ToList();
        try
        {
            foreach (var article in selected.Where(article => !article.IsPlayed))
            {
                _library.MarkPlayed(article);
            }

            UpdateCurrentState();
            UpdateFilter();
            StatusText.Text = $"Marked {selected.Count} article(s) as read.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not mark articles as read", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedArticles()
    {
        var selected = ArticleList.SelectedItems.OfType<ArticleAudio>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete {selected.Count} selected article(s), including their audio and saved details?",
            "Delete articles",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            if (selected.Any(article => ReferenceEquals(article, _currentArticle)))
            {
                StopCurrentAudio();
                _imageLoadCancellation?.Cancel();
                _currentArticle = null;
                ClearArticleMedia();
                _currentImageLoaded = false;
                ShowArticleText(string.Empty, string.Empty);
                NowPlayingTitle.Text = "Select an article to listen";
                CurrentArticleDateTime.Text = string.Empty;
                PositionSlider.Value = 0;
                PositionText.Text = "0:00";
                DurationText.Text = "0:00";
                UpdateArticleMediaLayout();
                UpdateCurrentState();
                UpdateSystemMediaTransportControls();
            }

            ArticleList.SelectedItems.Clear();
            foreach (var article in selected)
            {
                _library.Delete(article);
            }

            _selectionAnchor = null;
            UpdateFilter();
            StatusText.Text = $"Deleted {selected.Count} article(s).";
        }
        catch (Exception ex)
        {
            UpdateFilter();
            MessageBox.Show(this, ex.Message, "Could not delete articles", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ArticleItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ArticleAudio article } element)
        {
            return;
        }

        if (ArticleList.SelectedItems.Count > 1 && ArticleList.SelectedItems.Contains(article))
        {
            var multiMenu = CreateMultiSelectContextMenu(element);
            element.ContextMenu = multiMenu;
            multiMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        try
        {
            var preferences = _feedCategoryPreferences.Get(article.FeedUrl);
            var favoriteCategories = new HashSet<string>(
                preferences.FavoriteCategories,
                StringComparer.OrdinalIgnoreCase);
            var blockedCategories = new HashSet<string>(
                preferences.BlockedCategories,
                StringComparer.OrdinalIgnoreCase);
            var contextMenu = new ContextMenu
            {
                PlacementTarget = element,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            var displayableCategories = article.Categories
                .Where(FeedCategoryPreferencesService.IsDisplayableCategory)
                .ToList();
            if (displayableCategories.Count == 0)
            {
                contextMenu.Items.Add(new MenuItem { Header = "No feed categories", IsEnabled = false });
            }
            else
            {
                foreach (var category in displayableCategories)
                {
                    var categoryMenu = new MenuItem { Header = category };
                    categoryMenu.Items.Add(CreateCategoryMenuItem(
                        "Favorite",
                        article.FeedUrl,
                        category,
                        CategoryDisposition.Favorite,
                        favoriteCategories.Contains(category)));
                    categoryMenu.Items.Add(CreateCategoryMenuItem(
                        "Block",
                        article.FeedUrl,
                        category,
                        CategoryDisposition.Blocked,
                        blockedCategories.Contains(category)));
                    contextMenu.Items.Add(categoryMenu);
                }
            }

            element.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            e.Handled = true;
            MessageBox.Show(this, ex.Message, "Could not load feed categories", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private MenuItem CreateCategoryMenuItem(
        string label,
        string feedUrl,
        string category,
        CategoryDisposition disposition,
        bool isChecked)
    {
        var menuItem = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = isChecked,
            Tag = new CategoryMenuAction(feedUrl, category, disposition)
        };
        menuItem.Click += CategoryMenuItem_Click;
        return menuItem;
    }

    private void CategoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: CategoryMenuAction action } menuItem)
        {
            return;
        }

        var blockingCategory = action.Disposition == CategoryDisposition.Blocked && menuItem.IsChecked;
        var articlesToRemove = blockingCategory
            ? _library.Items.Where(article =>
                    article.FeedUrl.Equals(action.FeedUrl, StringComparison.OrdinalIgnoreCase) &&
                    article.Categories.Any(category =>
                        category.Equals(action.Category, StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : [];
        if (blockingCategory)
        {
            var unreadCount = articlesToRemove.Count(article => !article.IsPlayed);
            var confirmation = MessageBox.Show(
                this,
                $"Blocking category {action.Category} will remove unread {unreadCount} / total {articlesToRemove.Count} articles.",
                "Block feed category",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.OK)
            {
                menuItem.IsChecked = false;
                return;
            }
        }

        var categoryPreferenceSaved = false;
        try
        {
            _feedCategoryPreferences.SetCategoryDisposition(
                action.FeedUrl,
                action.Category,
                action.Disposition == CategoryDisposition.Favorite && menuItem.IsChecked,
                action.Disposition == CategoryDisposition.Blocked && menuItem.IsChecked);
            categoryPreferenceSaved = true;

            if (articlesToRemove.Any(article => ReferenceEquals(article, _currentArticle)))
            {
                StopCurrentAudio();
                _imageLoadCancellation?.Cancel();
                _currentArticle = null;
                ArticleList.SelectedItem = null;
                ClearArticleMedia();
                _currentImageLoaded = false;
                ShowArticleText(string.Empty, string.Empty);
                NowPlayingTitle.Text = string.Empty;
                CurrentArticleDateTime.Text = string.Empty;
                PositionSlider.Value = 0;
                PositionText.Text = "0:00";
                DurationText.Text = "0:00";
                UpdateArticleMediaLayout();
                UpdateCurrentState();
                UpdateSystemMediaTransportControls();
            }

            foreach (var article in articlesToRemove)
            {
                _library.Delete(article);
            }

            var favoriteCategories = new HashSet<string>(
                _feedCategoryPreferences.Get(action.FeedUrl).FavoriteCategories,
                StringComparer.OrdinalIgnoreCase);
            var phraseMatcher = new PhraseMatcher(_settings.FavoritedPhrases);
            foreach (var article in _library.Items.Where(item =>
                         item.FeedUrl.Equals(action.FeedUrl, StringComparison.OrdinalIgnoreCase)))
            {
                article.IsFavoriteCategory = article.Categories.Any(favoriteCategories.Contains) ||
                    phraseMatcher.IsMatch(article.Title, article.AudioText);
            }

            UpdateFilter();
            if (blockingCategory)
            {
                StatusText.Text = $"Removed {articlesToRemove.Count} article(s) in category {action.Category}.";
            }
        }
        catch (Exception ex)
        {
            if (!categoryPreferenceSaved)
            {
                menuItem.IsChecked = !menuItem.IsChecked;
            }

            var title = blockingCategory && categoryPreferenceSaved
                ? "Could not remove all articles in category"
                : "Could not update feed category";
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            if (categoryPreferenceSaved)
            {
                UpdateFilter();
            }
        }
    }

    private enum CategoryDisposition
    {
        Favorite,
        Blocked
    }

    private sealed record CategoryMenuAction(
        string FeedUrl,
        string Category,
        CategoryDisposition Disposition);

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
        ClearArticleMedia();
        PrepareExtraMedia(article);
        UpdateArticleMediaLayout();

        var cachedPath = File.Exists(article.CachedImagePath)
            ? article.CachedImagePath
            : ArticleMediaCache.FindCachedImage(article.ArticleFolderPath);
        if (cachedPath is not null)
        {
            try
            {
                SetPrimaryImage(ArticleMediaCache.LoadBitmap(cachedPath));
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

                SetPrimaryImage(ArticleMediaCache.LoadBitmap(imagePath));
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
        ShowArticleTextToggle.IsEnabled = _currentArticle?.HasRedundantBodyAudio != true;
        LoadInBrowserButton.IsEnabled = _currentArticle?.HasArticleUrl == true;
        AiChatButton.Visibility = _settings.AiChatEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        AiChatButton.IsEnabled = _settings.AiChatEnabled && _currentArticle is not null;
        ShareButtonsPanel.IsEnabled = _currentArticle?.HasArticleUrl == true;
        var hasSelectedArticle = _currentArticle is not null;
        var hasVisibleArticles = ArticleList.Items.Count > 0;
        StartupSplashVideo.Visibility = hasVisibleArticles && !hasSelectedArticle &&
                                        !_splashVideoFailed && !_splashVideoStopped
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupLogoImage.Visibility = !hasSelectedArticle &&
                                      (!hasVisibleArticles || _splashVideoFailed || _splashVideoStopped)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var showImageArea = hasSelectedArticle &&
            (_currentImageLoaded || _scanCountdownActive || _mediaItems.Count > 0);
        var showArticleText = hasSelectedArticle &&
            _settings.ShowArticleText && _currentArticle?.HasRedundantBodyAudio != true;
        ArticleImagePanel.Visibility = showImageArea ? Visibility.Visible : Visibility.Collapsed;
        ArticleTextPanel.Visibility = showArticleText ? Visibility.Visible : Visibility.Collapsed;
        var showSplitter = showImageArea && showArticleText;
        ArticleImageTextSplitter.Visibility = showSplitter ? Visibility.Visible : Visibility.Collapsed;

        if (showImageArea && showArticleText)
        {
            ArticleImageTextSplitterRow.Height = new GridLength(6);
            ApplyArticleImageHeightRatio(_articleImageHeightRatio);
        }
        else if (showImageArea)
        {
            ArticleImageRow.Height = new GridLength(1, GridUnitType.Star);
            ArticleImageTextSplitterRow.Height = new GridLength(0);
            ArticleTextRow.Height = new GridLength(0);
        }
        else if (showArticleText)
        {
            ArticleImageRow.Height = new GridLength(0);
            ArticleImageTextSplitterRow.Height = new GridLength(0);
            ArticleTextRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            ArticleImageRow.Height = new GridLength(0);
            ArticleImageTextSplitterRow.Height = new GridLength(0);
            ArticleTextRow.Height = new GridLength(0);
        }

        if (!hasSelectedArticle)
        {
            ArticleImageRow.Height = new GridLength(1, GridUnitType.Star);
            ArticleImageTextSplitterRow.Height = new GridLength(0);
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

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlaySelected(allowTemporaryUnmute: sender is Button);
    }

    private void PlaySelected(bool allowTemporaryUnmute = false, bool bodyOnly = false)
    {
        DiagnosticLog.Write("Playback", $"PlaySelected requested. Selected=\"{(ArticleList.SelectedItem as ArticleAudio)?.Title ?? "(none)"}\", current=\"{_currentArticle?.Title ?? "(none)"}\", segment={_audioSegment}, countdown={_scanCountdownActive}, muted={AudioMuteService.IsMuted}.");
        if (ArticleList.SelectedItem is not ArticleAudio selected)
        {
            return;
        }

        if (bodyOnly && AudioMuteService.IsMuted)
        {
            StatusText.Text = "Audio is muted.";
            return;
        }

        if (selected.HasAudioError)
        {
            UpdatePlayPauseButton();
            _ = HandleMissingAudioAsync(selected);
            return;
        }

        if (AudioMuteService.IsMuted)
        {
            if (!allowTemporaryUnmute)
            {
                StatusText.Text = "Audio is muted. Play to unmute.";
                return;
            }

            _restoreMuteAfterPlayback = true;
            _stopAfterCurrentArticle = true;
            AudioMuteService.SetMuted(false);
        }

        if (bodyOnly)
        {
            if (!selected.HasUsableBodyAudio)
            {
                StatusText.Text = "This article has no distinct body audio to play.";
                return;
            }

            _scanCountdownTimer.Stop();
            _scanCountdownActive = false;
            ScanCountdownOverlay.Visibility = Visibility.Collapsed;
            UpdateArticleMediaLayout();
            StopCurrentAudio();
            _currentArticle = selected;
            _playbackStarted = false;
            _scanModeForCurrentArticle = _settings.ScanMode &&
                !(_settings.FullModeForFavorites && selected.IsFavoriteCategory);
            NowPlayingTitle.Text = selected.Title;
            CurrentArticleDateTime.Text = selected.DisplayDateTime;
            UpdateSystemMediaTransportControls();
            UpdateCurrentState();
            OpenAudioSegment(AudioSegment.Body, selected.FilePath);
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
            AudioPlayer.Play();
            _positionTimer.Start();
            _isPlaying = true;
            MarkOnStart();
            return;
        }

        StopCurrentAudio();
        _currentArticle = selected;
        _playbackStarted = false;
        _scanModeForCurrentArticle = _settings.ScanMode &&
            !(_settings.FullModeForFavorites && selected.IsFavoriteCategory);
        NowPlayingTitle.Text = selected.Title;
        CurrentArticleDateTime.Text = selected.DisplayDateTime;
        UpdateSystemMediaTransportControls();
        UpdateCurrentState();
        var titlePath = selected.TitleAudioPath;
        if (!string.IsNullOrWhiteSpace(titlePath) && File.Exists(titlePath))
        {
            OpenAudioSegment(AudioSegment.Title, titlePath);
        }
        else
        {
            OpenAudioSegment(AudioSegment.Body, selected.FilePath);
        }
    }

    private bool OpenAudioSegment(AudioSegment segment, string audioPath)
    {
        DiagnosticLog.Write("Playback", $"Opening {segment} audio: {audioPath}; exists={File.Exists(audioPath)}.");
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
        AudioPlayer.SpeedRatio = 1.0;
        AudioPlayer.Source = new Uri(audioPath);
        DiagnosticLog.Write("Playback", $"Body player source set for {segment} audio; playback will start when the media is ready.");
        PositionSlider.Value = 0;
        PositionText.Text = "0:00.00";
        DurationText.Text = "0:00.00";
        StatusText.Text = segment == AudioSegment.Title
            ? $"Opening title: {_currentArticle?.Title}"
            : $"Opening article: {_currentArticle?.Title}";
        return true;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPlaying)
        {
            if (_restoreMuteAfterPlayback)
            {
                _restoreMuteAfterPlayback = false;
                _stopAfterCurrentArticle = false;
                AudioMuteService.SetMuted(true);
            }

            return;
        }

        var restoreMuteAfterPlayback = _restoreMuteAfterPlayback;
        _restoreMuteAfterPlayback = false;
        if (restoreMuteAfterPlayback)
        {
            _stopAfterCurrentArticle = false;
        }
        _scanCountdownTimer.Stop();
        if (!_scanCountdownActive)
        {
            AudioPlayer.Pause();
        }
        _isPlaying = false;
        _positionTimer.Stop();
        StatusText.Text = "Paused.";
        UpdateSystemMediaTransportControls();
        if (restoreMuteAfterPlayback)
        {
            AudioMuteService.SetMuted(true);
        }
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

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (ArticleList.Items.Count == 0)
        {
            return;
        }

        var previousArticle = FindPreviousArticle();
        MarkCurrentAsSkipped();
        if (previousArticle is not null)
        {
            NavigateToArticle(previousArticle);
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

        if (_currentArticle.IsPlayed)
        {
            _library.MarkUnplayed(_currentArticle);
            StatusText.Text = "Article marked as unread.";
        }
        else
        {
            _library.MarkPlayed(_currentArticle);
            StopCurrentAudio();
            StatusText.Text = "Article marked as read.";
        }

        UpdateCurrentState();
        UpdateFilter();
    }

    private void AudioPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        DiagnosticLog.Write("Playback", $"MediaOpened event. Current=\"{_currentArticle?.Title ?? "(none)"}\", segment={_audioSegment}, source={AudioPlayer.Source}.");
        if (_currentArticle is null || _audioSegment is not (AudioSegment.Title or AudioSegment.Body))
        {
            return;
        }

        _audioSourceReady = true;
        AudioPlayer.Position = TimeSpan.Zero;
        AudioPlayer.Play();
        _isPlaying = true;
        _playbackStarted = true;
        _library.RecordListened(_currentArticle);
        _positionTimer.Start();
        UpdateSystemMediaTransportControls();
        if (AudioPlayer.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        }
        PositionSlider.Value = 0;
        PositionText.Text = "0:00.00";
        DurationText.Text = AudioPlayer.NaturalDuration.HasTimeSpan
            ? FormatTime(AudioPlayer.NaturalDuration.TimeSpan)
            : "0:00.00";
        MarkOnStart();
        StatusText.Text = _audioSegment == AudioSegment.Title
            ? $"Reading title: {_currentArticle.Title}"
            : $"Playing: {_currentArticle.Title}";
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
        UpdateSystemMediaTransportControls();
        if (_audioSegment == AudioSegment.Title)
        {
            if (_currentArticle is null)
            {
                return;
            }

            if (_scanModeForCurrentArticle && !_restoreMuteAfterPlayback)
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

            return;
        }

        CompleteCurrentArticle(_scanModeForCurrentArticle);
    }

    private void StartScanCountdown()
    {
        DiagnosticLog.Write("Playback", $"Starting scan countdown for \"{_currentArticle?.Title ?? "(none)"}\" for {_settings.ScanCountdownSeconds} second(s).");
        if (_settings.ScanCountdownSeconds <= 0)
        {
            CompleteCurrentArticle(scanMode: true);
            return;
        }

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
        UpdateSystemMediaTransportControls();
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
        var restoreMuteAfterPlayback = _restoreMuteAfterPlayback;
        _restoreMuteAfterPlayback = false;
        _isPlaying = false;
        _positionTimer.Stop();
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        _audioSegment = AudioSegment.None;
        _audioSourceReady = false;
        if (AudioPlayer.Source is not null)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }
        UpdateArticleMediaLayout();
        var stopAfterCurrentArticle = _stopAfterCurrentArticle;
        _stopAfterCurrentArticle = false;
        var shouldAdvance = !restoreMuteAfterPlayback &&
            (scanMode || _settings.AutoPlayNext);
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
        if (restoreMuteAfterPlayback)
        {
            AudioMuteService.SetMuted(true);
        }

        if (nextArticle is not null)
        {
            if (!AudioMuteService.IsMuted)
            {
                PageTurnSound.Play();
            }
            NavigateToArticle(nextArticle);
            return;
        }

        StatusText.Text = "Playback finished.";
        UpdateSystemMediaTransportControls();
    }

    private void AudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        var failedSegment = _audioSegment;
        DiagnosticLog.Exception($"{failedSegment} media playback failed", e.ErrorException);
        _audioSourceReady = false;
        _isPlaying = false;
        _positionTimer.Stop();
        _scanCountdownTimer.Stop();
        _scanCountdownActive = false;
        _audioSegment = AudioSegment.None;
        var restoreMuteAfterPlayback = _restoreMuteAfterPlayback;
        _restoreMuteAfterPlayback = false;
        ScanCountdownOverlay.Visibility = Visibility.Collapsed;
        UpdateArticleMediaLayout();
        if (restoreMuteAfterPlayback)
        {
            AudioMuteService.SetMuted(true);
        }
        StatusText.Text = $"{(failedSegment == AudioSegment.Title ? "Title playback" : "Playback")} failed: {e.ErrorException.Message}";
        UpdateSystemMediaTransportControls();
        MessageBox.Show(
            this,
            e.ErrorException.Message,
            failedSegment == AudioSegment.Title ? "Title playback failed" : "Playback failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSeeking || !_isPlaying)
        {
            return;
        }

        if (_audioSegment is not (AudioSegment.Title or AudioSegment.Body) ||
            !AudioPlayer.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var position = AudioPlayer.Position;
        PositionSlider.Value = position.TotalSeconds;
        PositionText.Text = FormatTime(position);
        if (_audioSegment == AudioSegment.Body &&
            ArticleTextPanel.Visibility == Visibility.Visible &&
            AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0)
        {
            var progress = Math.Clamp(
                position.TotalSeconds / AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds,
                0,
                1);
            var scrollLag = ArticleTextInitialScrollLag * (1 - progress);
            var scrollProgress = Math.Max(0, progress - scrollLag);
            var scrollViewer = FindArticleTextScrollViewer();
            if (scrollViewer is not null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight * scrollProgress);
            }
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
        _isPlaying = false;
        _playbackStarted = false;
        UpdateSystemMediaTransportControls();
    }

    private bool IsArticleAudioActive() =>
        _audioSegment is AudioSegment.Title or AudioSegment.Body && AudioPlayer.Source is not null;

    private void UpdateFilter()
    {
        if (ArticleList.ItemsSource is null)
        {
            return;
        }

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_library.Items);
        var navigationSort = GetSelectedTag(NavigationSortComboBox, "recent");
        var ageFilter = GetSelectedTag(NavigationAgeComboBox, "any");
        var consumptionFilter = GetSelectedTag(NavigationConsumptionComboBox, "any");
        var searchText = NavigationSearchTextBox.Text.Trim();
        var applyHistoryOptions = _navigationMode is NavigationMode.Search or NavigationMode.History;
        if (applyHistoryOptions)
        {
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.GroupDescriptions.Clear();
                switch (navigationSort)
                {
                    case "title_ascending":
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.Title), ListSortDirection.Ascending));
                        break;
                    case "title_descending":
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.Title), ListSortDirection.Descending));
                        break;
                    case "age_oldest":
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.LastActivityAt), ListSortDirection.Ascending));
                        break;
                    case "age_newest":
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.LastActivityAt), ListSortDirection.Descending));
                        break;
                    default:
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.LastActivityAt), ListSortDirection.Descending));
                        view.SortDescriptions.Add(new SortDescription(nameof(ArticleAudio.SortDate), ListSortDirection.Descending));
                        break;
                }
            }
        }
        else
        {
            ApplyArticleListSorting();
        }

        view.Filter = item => item is ArticleAudio article &&
            (!UnplayedOnlyMenuItem.IsChecked || !article.IsPlayed || article.IsStarred) &&
            (!StarredOnlyMenuItem.IsChecked || article.IsStarred) &&
            (!FavoriteCategoriesOnlyMenuItem.IsChecked || article.IsFavoriteCategory) &&
            (_selectedFeedCategories.Count == 0 || _selectedFeedCategories.Any(selection =>
                selection.FeedIdentity.Equals(GetFeedIdentity(article), StringComparison.OrdinalIgnoreCase) &&
                (selection.Category is null || article.Categories.Any(category =>
                    category.Equals(selection.Category, StringComparison.OrdinalIgnoreCase))))) &&
            (_navigationMode != NavigationMode.Starred || article.IsStarred) &&
            (_navigationMode != NavigationMode.Favorites || article.IsFavoriteCategory) &&
            (_navigationMode != NavigationMode.History || article.LastActivityAt.HasValue) &&
            (_navigationMode != NavigationMode.Search || MatchesNavigationSearch(article, searchText)) &&
            (!applyHistoryOptions || MatchesConsumption(article, consumptionFilter)) &&
            MatchesHistoryAge(article, ageFilter, applyHistoryOptions);
        view.Refresh();
        if (ArticleList.Items.Count == 0)
        {
            ClearArticlePaneForEmptyList();
        }
        else
        {
            UpdateArticleMediaLayout();
        }

        UpdateLibraryCounts();
    }

    private static string GetSelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static bool MatchesConsumption(ArticleAudio article, string method) =>
        method switch
        {
            "viewed" => article.HasBeenViewed,
            "listened" => article.HasBeenListenedTo,
            _ => true
        };

    private static bool MatchesHistoryAge(ArticleAudio article, string age, bool apply)
    {
        if (!apply || age == "any")
        {
            return true;
        }

        if (article.LastActivityAt is not { } lastActivity)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        var cutoff = age switch
        {
            "day" => now.AddDays(-1),
            "week" => now.AddDays(-7),
            "month" => now.AddMonths(-1),
            "year" => now.AddYears(-1),
            _ => DateTimeOffset.MinValue
        };
        return lastActivity >= cutoff;
    }

    private static bool MatchesNavigationSearch(ArticleAudio article, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return article.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               article.FeedDisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               article.Categories.Any(category => category.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
               article.AudioText.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               article.ArticleContentHtml.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ClearArticlePaneForEmptyList()
    {
        StopCurrentAudio();
        _imageLoadCancellation?.Cancel();
        _currentArticle = null;
        ArticleList.SelectedItem = null;
        ClearArticleMedia();
        _currentImageLoaded = false;
        ShowArticleText(string.Empty, string.Empty);
        NowPlayingTitle.Text = string.Empty;
        CurrentArticleDateTime.Text = string.Empty;
        PositionSlider.Value = 0;
        PositionText.Text = "0:00";
        DurationText.Text = "0:00";
        StopStartupSplash();
        UpdateArticleMediaLayout();
        UpdateCurrentState();
        UpdateSystemMediaTransportControls();
    }

    private void ApplyFavoriteCategoryFlags(IEnumerable<ArticleAudio>? articles = null)
    {
        var targets = (articles ?? _library.Items).ToList();
        var phraseMatcher = new PhraseMatcher(_settings.FavoritedPhrases);
        foreach (var group in targets.GroupBy(article => article.FeedUrl, StringComparer.OrdinalIgnoreCase))
        {
            var favoriteCategories = new HashSet<string>(
                _feedCategoryPreferences.Get(group.Key).FavoriteCategories,
                StringComparer.OrdinalIgnoreCase);
            foreach (var article in group)
            {
                article.IsFavoriteCategory = article.Categories.Any(favoriteCategories.Contains) ||
                    phraseMatcher.IsMatch(article.Title, article.AudioText);
            }
        }
    }

    private void UpdateLibraryCounts()
    {
        var totalCount = _library.Items.Count;
        var unreadCount = _library.Items.Count(article => !article.IsPlayed);
        LibraryCountsText.Text = $"({unreadCount} / {totalCount})";
        _trayIconService?.SetUnreadCount(unreadCount);
        RefreshTickerArticles();
    }

    private void UpdateUnreadTrayIndicator()
    {
        if (_library is not null && _trayIconService is not null)
        {
            _trayIconService.SetUnreadCount(_library.Items.Count(article => !article.IsPlayed));
        }
    }

    private void HookLibraryCounts()
    {
        foreach (var article in _library.Items)
        {
            article.PropertyChanged += Article_PropertyChanged;
        }

        _library.Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (ArticleAudio article in e.NewItems)
                {
                    article.PropertyChanged += Article_PropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (ArticleAudio article in e.OldItems)
                {
                    article.PropertyChanged -= Article_PropertyChanged;
                }
            }

            ConfigureRelativeTimeTimer();
            BuildNavigationTree();
            UpdateLibraryCounts();
        };
        UpdateLibraryCounts();
    }

    private void Article_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArticleAudio.LastViewedAt) or nameof(ArticleAudio.LastListenedAt))
        {
            if (_navigationMode is NavigationMode.Search or NavigationMode.History)
            {
                UpdateFilter();
            }
        }

        if (e.PropertyName == nameof(ArticleAudio.PublishedAt))
        {
            ConfigureRelativeTimeTimer();
        }

        if (e.PropertyName == nameof(ArticleAudio.IsPlayed))
        {
            UpdateLibraryCounts();
            if (ReferenceEquals(sender, _currentArticle))
            {
                UpdateCurrentState();
            }
        }
        else if (sender is ArticleAudio article &&
                 e.PropertyName is nameof(ArticleAudio.PublishedAt) or nameof(ArticleAudio.RelativeDateTime) &&
                 ReferenceEquals(article, _currentArticle))
        {
            CurrentArticleDateTime.Text = article.DisplayDateTime;
        }
    }

    private void UpdateCurrentState()
    {
        var sourceThumbnailUri = _currentArticle?.SourceThumbnailUri;
        UpdatePlayerSourceThumbnail(sourceThumbnailUri);

        if (_currentArticle is null)
        {
            StarCurrentButton.IsEnabled = false;
            MarkCurrentReadButton.IsEnabled = false;
            RebuildArticleButton.IsEnabled = false;
            return;
        }

        StarCurrentButton.IsEnabled = true;
        MarkCurrentReadButton.IsEnabled = true;
        RebuildArticleButton.IsEnabled = !_isRetrievingFeeds;
        StarCurrentButton.Content = _currentArticle.StarLabel;
        MarkCurrentReadButton.Content = "✓";
        MarkCurrentReadButton.Foreground = _currentArticle.IsPlayed
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.White;
        var markArticleText = _currentArticle.IsPlayed ? "Mark article as unread" : "Mark article as read";
        MarkCurrentReadButton.ToolTip = markArticleText;
        System.Windows.Automation.AutomationProperties.SetName(MarkCurrentReadButton, markArticleText);
    }

    private void UpdatePlayerSourceThumbnail(Uri? sourceThumbnailUri)
    {
        if (ReferenceEquals(_sourceThumbnailArticle, _currentArticle) &&
            Equals(_sourceThumbnailArticle?.SourceThumbnailUri, sourceThumbnailUri))
        {
            return;
        }

        _sourceThumbnailArticle = _currentArticle;
        _sourceThumbnailFallbacks = _currentArticle is null
            ? []
            : GetSourceThumbnailFallbacks(_currentArticle);
        _sourceThumbnailFallbackIndex = 0;
        PlayerSourceThumbnailImage.Source = null;

        if (sourceThumbnailUri is null)
        {
            PlayerSourceThumbnail.Visibility = Visibility.Collapsed;
            return;
        }

        PlayerSourceThumbnail.Visibility = Visibility.Visible;
        try
        {
            PlayerSourceThumbnailImage.Source = sourceThumbnailUri.IsFile
                ? ArticleMediaCache.LoadBitmap(sourceThumbnailUri.LocalPath)
                : new BitmapImage(sourceThumbnailUri);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            ShowNextSourceThumbnailFallback();
        }
    }

    private static IReadOnlyList<Uri> GetSourceThumbnailFallbacks(ArticleAudio article)
    {
        var sourceUri = TryGetArticleUri(article.ArticleUrl, out var articleUri)
            ? articleUri
            : Uri.TryCreate(article.SourceLogoUrl, UriKind.Absolute, out var logoUri) &&
              logoUri.Scheme is "http" or "https"
                ? logoUri
                : null;
        if (sourceUri is null)
        {
            return [];
        }

        var origin = sourceUri.GetLeftPart(UriPartial.Authority);
        return
        [
            new Uri(origin + "/favicon.ico"),
            new Uri(origin + "/apple-touch-icon.png")
        ];
    }

    private void PlayerSourceThumbnailImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ShowNextSourceThumbnailFallback();
    }

    private void ShowNextSourceThumbnailFallback()
    {
        while (_sourceThumbnailFallbackIndex < _sourceThumbnailFallbacks.Count)
        {
            var fallback = _sourceThumbnailFallbacks[_sourceThumbnailFallbackIndex++];
            if (fallback == _currentArticle?.SourceThumbnailUri)
            {
                continue;
            }

            PlayerSourceThumbnailImage.Source = new BitmapImage(fallback);
            return;
        }

        PlayerSourceThumbnail.Visibility = Visibility.Collapsed;
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

    private ArticleAudio? FindPreviousArticle()
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
            return visibleItems[^1];
        }

        return currentVisibleIndex > 0 ? visibleItems[currentVisibleIndex - 1] : visibleItems[^1];
    }

    private void NavigateToArticle(ArticleAudio article)
    {
        ArticleList.SelectedItem = article;
        ArticleList.ScrollIntoView(article);
    }

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

    private void HideToTray()
    {
        StopStartupSplash();
        ShowInTaskbar = false;
        Hide();
        UpdateTickerVisibility();
    }

    private bool ShouldShowTicker()
    {
        if (_settings?.Ticker is not { } ticker || !StartupSucceeded || _tickerSuppressed)
        {
            return false;
        }

        var minimized = WindowState == WindowState.Minimized;
        var closedToTray = !IsVisible && !minimized;
        return (minimized && ticker.ShowWhenMinimized) || (closedToTray && ticker.ShowWhenClosedToTray);
    }

    private List<ArticleAudio> GetTickerArticles() =>
        _library.Items
            .Where(article => !article.IsPlayed && !string.IsNullOrWhiteSpace(article.Title))
            .OrderByDescending(article => article.SortDate)
            .Take(40)
            .ToList();

    private void UpdateTickerVisibility()
    {
        if (!ShouldShowTicker())
        {
            _tickerWindow?.Hide();
            return;
        }

        var articles = GetTickerArticles();
        if (articles.Count == 0 && _tickerWindow?.IsVisible != true)
        {
            return;
        }

        if (_tickerWindow is null)
        {
            _tickerWindow = new TickerWindow();
            _tickerWindow.ArticleClicked += OpenArticleFromTicker;
            _tickerWindow.ArticleDismissed += DismissArticleFromTicker;
            _tickerWindow.AudioErrorClicked += article =>
            {
                RestoreFromTray();
                _ = HandleMissingAudioAsync(article);
            };
            _tickerWindow.TitleRead += article => DismissArticleFromTicker(article, false);
            _tickerWindow.PositionChanged += position =>
            {
                try
                {
                    _settings.Ticker.Position = position;
                    _settingsService.SaveAll(_settings);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Exception("Could not save ticker position", ex);
                }
            };
            _tickerWindow.SpeedChanged += speed =>
            {
                try
                {
                    _settings.Ticker.ScrollSpeed = speed;
                    _settingsService.SaveAll(_settings);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Exception("Could not save ticker speed", ex);
                }
            };
            _tickerWindow.HeightAdjusted += height =>
            {
                try
                {
                    _settings.Ticker.Height = height;
                    _settingsService.SaveAll(_settings);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Exception("Could not save ticker height", ex);
                }
            };
            _tickerWindow.CloseRequested += () =>
            {
                _tickerSuppressed = true;
                _tickerWindow?.Hide();
            };
        }

        _tickerWindow.Configure(_settings.Ticker);
        _tickerWindow.SetArticles(articles);
        if (!_tickerWindow.IsVisible)
        {
            _tickerWindow.Show();
        }
    }

    private void RefreshTickerArticles()
    {
        if (_library is null || (_tickerWindow?.IsVisible != true && !ShouldShowTicker()))
        {
            return;
        }

        UpdateTickerVisibility();
    }

    private void OpenArticleFromTicker(ArticleAudio article, string action)
    {
        var target = _library.Items.FirstOrDefault(existing =>
            existing.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        if (action == TickerSettings.ClickOpenSourceInBrowser)
        {
            Window owner = _tickerWindow is { IsVisible: true } ticker ? ticker : this;
            if (!TryGetArticleUri(target.ArticleUrl, out _))
            {
                MessageBox.Show(owner, "This article does not have a valid source URL.", "Could not open article",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenArticleInBrowser(target, owner);
            return;
        }

        if (action == TickerSettings.ClickMarkAsRead)
        {
            DismissArticleFromTicker(target, delete: false);
            return;
        }

        var playBodyAudio = action == TickerSettings.ClickPlayBodyAudio;
        RestoreFromTray();
        if (!ArticleList.Items.Contains(target))
        {
            SetNavigationMode(NavigationMode.AllFeeds);
        }

        if (!ArticleList.Items.Contains(target))
        {
            UnplayedOnlyMenuItem.IsChecked = false;
            StarredOnlyMenuItem.IsChecked = false;
            FavoriteCategoriesOnlyMenuItem.IsChecked = false;
            UpdateFilter();
        }

        if (!ArticleList.Items.Contains(target))
        {
            return;
        }

        if (ReferenceEquals(ArticleList.SelectedItem, target))
        {
            if (playBodyAudio)
            {
                PlaySelected(bodyOnly: true);
            }
            else
            {
                if (_isPlaying)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                }

                _loadOnlyNoPlayback = true;
                try
                {
                    SelectArticle(target);
                }
                finally
                {
                    _loadOnlyNoPlayback = false;
                }
            }
        }
        else
        {
            _playBodyAudioOnNextSelection = playBodyAudio;
            _loadOnlyNoPlayback = !playBodyAudio;
            try
            {
                ArticleList.SelectedItem = target;
            }
            finally
            {
                _loadOnlyNoPlayback = false;
                _playBodyAudioOnNextSelection = false;
            }
        }

        ArticleList.ScrollIntoView(target);
    }

    private void DismissArticleFromTicker(ArticleAudio article, bool delete)
    {
        var target = _library.Items.FirstOrDefault(existing =>
            existing.FilePath.Equals(article.FilePath, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        try
        {
            if (!delete)
            {
                _library.MarkPlayed(target);
                UpdateFilter();
                return;
            }

            if (ReferenceEquals(_currentArticle, target))
            {
                StopCurrentAudio();
                _imageLoadCancellation?.Cancel();
                ArticleList.SelectedItems.Clear();
                _currentArticle = null;
            }

            _library.Delete(target);
            _selectionAnchor = null;
            UpdateFilter();
            StatusText.Text = $"Deleted \"{target.Title}\".";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Exception("Ticker dismiss failed", ex);
            StatusText.Text = $"Could not {(delete ? "delete" : "mark")} article: {ex.Message}";
        }
    }

    private void NotifyFavoriteArticle(ArticleAudio article)
    {
        var ticker = _settings.Ticker;
        _notificationArticle = article;
        if (ticker.FavoritesNotification == TickerSettings.NotifyWindows)
        {
            _trayIconService?.ShowNotification($"Favorite: {article.FeedDisplayName}", article.Title);
        }
        else if (ticker.FavoritesNotification == TickerSettings.NotifySound && !AudioMuteService.IsMuted)
        {
            if (!string.IsNullOrWhiteSpace(ticker.FavoritesSoundPath) && File.Exists(ticker.FavoritesSoundPath))
            {
                _notificationPlayer ??= new MediaPlayer();
                _notificationPlayer.Open(new Uri(ticker.FavoritesSoundPath, UriKind.Absolute));
                _notificationPlayer.Play();
            }
            else
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        _tickerSuppressed = false;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        UpdateTickerVisibility();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            StopStartupSplash();
            if (_settings?.MinimizeToTray == true)
            {
                HideToTray();
            }
            else
            {
                UpdateTickerVisibility();
            }

            if (_isPlaying && _tickerWindow?.IsVisible == true)
            {
                PauseButton_Click(this, new RoutedEventArgs());
            }
        }
        else
        {
            _tickerSuppressed = false;
            UpdateTickerVisibility();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit && _settings?.CloseToTray == true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (StartupSucceeded)
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            _windowPlacementService.Save(new WindowPlacement(
                bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized));
        }

        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _relativeTimeTimer.Stop();
        AudioMuteService.MuteChanged -= AudioMuteService_MuteChanged;
        StopCurrentAudio();
        if (_systemMediaTransportControls is { } controls)
        {
            controls.ButtonPressed -= SystemMediaTransportControls_ButtonPressed;
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            _systemMediaTransportControls = null;
        }
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        _trayIconService?.Dispose();
        _trayIconService = null;
        _tickerWindow?.Close();
        _tickerWindow = null;
        if (_library is not null)
        {
            _library.Save();
        }
        base.OnClosed(e);
    }
}
