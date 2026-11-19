using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AnchorFeeds.Models;
using AnchorFeeds.Services;

namespace AnchorFeeds;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly FeedCategoryPreferencesService _feedCategoryPreferencesService;
    private readonly ObservableCollection<FeedConfiguration> _feedConfigurations = [];
    private readonly Dictionary<string, FeedCategoryPreferences> _pendingCategoryPreferences =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _voiceSampleCancellation;
    private string? _voiceSamplePath;
    private VoiceOption? _currentVoiceOption;

    public Func<string>? RebuildLibrary { get; init; }

    public Func<string>? PruneLibrary { get; init; }

    private void RebuildLibraryButton_Click(object sender, RoutedEventArgs e) => RunMaintenance(
        "Permanently delete all matching article audio files and article folders in the configured audio library?\n\nThis removes audio, cached images, and article text. This action cannot be undone.",
        "Rebuild Library",
        RebuildLibrary);

    private void PruneLibraryButton_Click(object sender, RoutedEventArgs e) => RunMaintenance(
        "Delete articles older than their applicable saved retention period? Starred and favorite articles follow their own retention settings.\n\nThis permanently removes their audio, images, and text.",
        "Prune Feed Library",
        PruneLibrary);

    private void RunMaintenance(string prompt, string title, Func<string>? action)
    {
        if (action is null ||
            MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            MessageBox.Show(this, action(), title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public SettingsWindow(AppSettings settings, FeedCategoryPreferencesService feedCategoryPreferencesService)
    {
        InitializeComponent();
        VoiceSamplePlayer.Volume = AudioMuteService.IsMuted ? 0 : 1;
        AudioMuteService.MuteChanged += AudioMuteService_MuteChanged;
        LocalizationService.Apply(this);
        _settings = settings;
        _feedCategoryPreferencesService = feedCategoryPreferencesService;
        ApplicationLanguageComboBox.SelectedItem = ApplicationLanguageComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.ApplicationLanguage, StringComparison.OrdinalIgnoreCase));
        AiPromptInstructionTextBox.Text = settings.AiPromptInstruction;
        AiChatEnabledCheckBox.IsChecked = settings.AiChatEnabled;
        InterfaceThemeComboBox.SelectedItem = InterfaceThemeComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.AppearanceTheme, StringComparison.OrdinalIgnoreCase));
        ArticleListFontFamilyComboBox.Text = settings.ArticleListFontFamily;
        ArticleListFontSizeComboBox.Text = settings.ArticleListFontSize.ToString("0.##", CultureInfo.InvariantCulture);
        ArticleTextFontFamilyComboBox.Text = settings.ArticleTextFontFamily;
        ArticleTextFontSizeComboBox.Text = settings.ArticleTextFontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var ticker = settings.Ticker;
        TickerWhenMinimizedCheckBox.IsChecked = ticker.ShowWhenMinimized;
        TickerWhenClosedToTrayCheckBox.IsChecked = ticker.ShowWhenClosedToTray;
        TickerMarkReadCheckBox.IsChecked = ticker.MarkReadWhenTitleRead;
        TickerClickActionComboBox.SelectedItem = TickerClickActionComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, ticker.ClickAction, StringComparison.Ordinal));
        TickerRightClickActionComboBox.SelectedItem = TickerRightClickActionComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, ticker.RightClickAction, StringComparison.Ordinal));
        TickerPositionComboBox.SelectedItem = TickerPositionComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, ticker.Position, StringComparison.Ordinal));
        TickerFontFamilyComboBox.Text = ticker.FontFamily;
        TickerFontSizeComboBox.Text = ticker.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        TickerHeightComboBox.Text = ticker.Height.ToString("0.##", CultureInfo.InvariantCulture);
        TickerScrollSpeedComboBox.SelectedItem = TickerScrollSpeedComboBox.Items
            .Cast<ComboBoxItem>()
            .MinBy(item => Math.Abs(double.Parse((string)item.Tag, CultureInfo.InvariantCulture) - ticker.ScrollSpeed));
        TickerNotificationComboBox.SelectedItem = TickerNotificationComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, ticker.FavoritesNotification, StringComparison.Ordinal));
        TickerSoundPathTextBox.Text = ticker.FavoritesSoundPath;
        SetHotkeyText(PlayPauseHotkeyTextBox, settings.Hotkeys.PlayPause);
        SetHotkeyText(SkipNextHotkeyTextBox, settings.Hotkeys.SkipNext);
        SetHotkeyText(PreviousBackHotkeyTextBox, settings.Hotkeys.PreviousBack);
        SetHotkeyText(VolumeUpHotkeyTextBox, settings.Hotkeys.VolumeUp);
        SetHotkeyText(VolumeDownHotkeyTextBox, settings.Hotkeys.VolumeDown);
        SetHotkeyText(ForwardHotkeyTextBox, settings.Hotkeys.Forward);
        SetHotkeyText(BackwardHotkeyTextBox, settings.Hotkeys.Backward);
        SetHotkeyText(MuteHotkeyTextBox, settings.Hotkeys.Mute);
        SetHotkeyText(FullModeHotkeyTextBox, settings.Hotkeys.FullMode);
        SetHotkeyText(ScanModeHotkeyTextBox, settings.Hotkeys.ScanMode);
        SetHotkeyText(RefreshLibraryHotkeyTextBox, settings.Hotkeys.RefreshLibrary);
        SetHotkeyText(RefreshArticleHotkeyTextBox, settings.Hotkeys.RefreshArticle);
        SetHotkeyText(MarkAllAsReadHotkeyTextBox, settings.Hotkeys.MarkAllAsRead);
        SetHotkeyText(SettingsHotkeyTextBox, settings.Hotkeys.Settings);
        PortableModeCheckBox.IsChecked = settings.PortableMode;
        ScanCountdownComboBox.SelectedItem = ScanCountdownComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == settings.ScanCountdownSeconds);
        FullModeForFavoritesCheckBox.IsChecked = settings.FullModeForFavorites;
        BannedPhrasesEditor.Phrases = settings.BannedPhrases;
        FavoritedPhrasesEditor.Phrases = settings.FavoritedPhrases;
        StarredPhrasesEditor.Phrases = settings.StarredPhrases;
        ShowMatchingKeywordsCheckBox.IsChecked = settings.ShowMatchingKeywords;
        MarkReadWhenScrolledOutOfViewCheckBox.IsChecked = settings.MarkReadWhenScrolledOutOfView;
        foreach (var feed in settings.FeedSources)
        {
            _feedConfigurations.Add(feed.Copy());
        }
        FeedGrid.ItemsSource = _feedConfigurations;
        OutputDirectoryTextBox.Text = settings.OutputDirectory;
        RetentionTextBox.Text = settings.Retention;
        StarredRetentionTextBox.Text = settings.StarredRetention;
        FavoritesRetentionTextBox.Text = settings.FavoritesRetention;
        var feedUpdateInterval = string.IsNullOrWhiteSpace(settings.FeedUpdateInterval)
            ? "Manual only"
            : settings.FeedUpdateInterval.Trim();
        FeedUpdateIntervalComboBox.SelectedItem = FeedUpdateIntervalComboBox.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                (string)item.Content,
                feedUpdateInterval,
                StringComparison.OrdinalIgnoreCase))
            ?? FeedUpdateIntervalComboBox.Items
                .Cast<ComboBoxItem>()
                .First(item => string.Equals((string)item.Content, "Manual only", StringComparison.Ordinal));
        if (!NewsGenerationService.IsFfmpegAvailable() && settings.TtsEngine != AppSettings.WindowsSpeechEngine)
        {
            WindowsTtsItem.IsEnabled = false;
            WindowsTtsItem.Content = "Windows speech (unavailable: FFmpeg not found on PATH)";
        }

        TtsEngineComboBox.SelectedItem = TtsEngineComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.TtsEngine, StringComparison.Ordinal));
        Mp3BitrateComboBox.SelectedItem = Mp3BitrateComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == settings.Mp3BitrateKbps);
        AutoPlayNextCheckBox.IsChecked = settings.AutoPlayNext;
        RetrieveLatestFeedsOnLaunchCheckBox.IsChecked = settings.RetrieveLatestFeedsOnLaunch;
        MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
        CloseToTrayCheckBox.IsChecked = settings.CloseToTray;
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
        DebugLoggingCheckBox.IsChecked = settings.DebugLoggingEnabled;
        ShowArticleTextCheckBox.IsChecked = settings.ShowArticleText;
        ShowUnplayedOnlyCheckBox.IsChecked = settings.ShowUnplayedOnly;
        MarkAsPlayedComboBox.SelectedItem = MarkAsPlayedComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.MarkAsPlayed, StringComparison.OrdinalIgnoreCase));
        PopulateVoices([], settings.Voices.GetVoices());
    }

    public AppSettings? UpdatedSettings { get; private set; }

    public void SelectFeedsTab() => FeedsTab.IsSelected = true;

    private void TickerNotificationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        TickerSoundPanel.Visibility = TickerNotificationComboBox.SelectedItem is ComboBoxItem { Tag: "sound" }
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void BrowseTickerSoundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select notification sound",
            Filter = "Sound files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            TickerSoundPathTextBox.Text = dialog.FileName;
        }
    }

    private void ClearTickerSoundButton_Click(object sender, RoutedEventArgs e) =>
        TickerSoundPathTextBox.Clear();

    private static void SetHotkeyText(TextBox textBox, string shortcut) =>
        textBox.Text = shortcut;

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            throw new InvalidOperationException("The hotkey editor is unavailable.");
        }

        e.Handled = true;
        if (e.Key is Key.Back or Key.Delete)
        {
            textBox.Clear();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0)
        {
            return;
        }

        var shortcut = HotkeySettings.Format(key, Keyboard.Modifiers);
        if (HotkeySettings.TryParse(shortcut, out _))
        {
            textBox.Text = shortcut;
        }
    }

    private void BrowseOutputDirectoryButton_Click(object sender, RoutedEventArgs e) =>
        SelectOutputDirectory();

    private void OutputDirectoryTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SelectOutputDirectory();
    }

    private void SelectOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select media output directory",
            InitialDirectory = Directory.Exists(OutputDirectoryTextBox.Text)
                ? OutputDirectoryTextBox.Text
                : AppContext.BaseDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void SettingHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ToolTip: string helpText })
        {
            throw new InvalidOperationException("The settings help button is missing its help text.");
        }

        MessageBox.Show(this, helpText, "Settings help", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddFeedButton_Click(object sender, RoutedEventArgs e)
    {
        var feed = new FeedConfiguration { Name = "New feed" };
        _feedConfigurations.Add(feed);
        FeedGrid.SelectedItem = feed;
        FeedGrid.CurrentCell = new DataGridCellInfo(feed, FeedGrid.Columns[0]);
        FeedGrid.ScrollIntoView(feed);
        FeedGrid.BeginEdit();
    }

    private void RemoveFeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (FeedGrid.SelectedItem is FeedConfiguration feed)
        {
            _feedConfigurations.Remove(feed);
        }
    }

    private void FeedPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FeedConfiguration feed })
        {
            throw new InvalidOperationException("The selected feed properties are unavailable.");
        }

        if (!Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(
                this,
                "Enter a valid feed URL before configuring its categories.",
                "Feed URL required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preferences = _pendingCategoryPreferences.TryGetValue(feed.Url, out var pending)
            ? pending
            : _feedCategoryPreferencesService.Get(feed.Url);
        var dialog = new FeedPropertiesWindow(feed, preferences)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.UpdatedPreferences is { } updated)
        {
            _pendingCategoryPreferences[feed.Url] = updated;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        VoiceCatalogStatus.Text = "Loading available voices from edge-tts...";
        try
        {
            var voices = await EdgeTtsVoiceCatalog.GetAvailableVoicesAsync(CancellationToken.None);
            var selectedVoices = GetSelectedVoices();
            PopulateVoices(voices, selectedVoices);
            VoiceCatalogStatus.Text = $"Loaded {voices.Count} voices. Expand a language/country and check voices to include.";
        }
        catch (Exception ex)
        {
            VoiceCatalogStatus.Text = $"Could not load the edge-tts voice catalog: {ex.Message}";
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void VoiceSampleButton_Click(object sender, RoutedEventArgs e)
    {
        if (AudioMuteService.IsMuted)
        {
            VoiceCatalogStatus.Text = "Audio is muted. Play a sample to unmute.";
            return;
        }

        if (sender is not Button { Tag: VoiceOption voiceOption })
        {
            return;
        }

        StopVoiceSample();
        var cancellation = new CancellationTokenSource();
        _voiceSampleCancellation = cancellation;
        _currentVoiceOption = voiceOption;
        voiceOption.IsLoading = true;
        VoiceCatalogStatus.Text = $"Creating {voiceOption.Name} sample...";
        StopVoiceSampleButton.IsEnabled = false;
        try
        {
            var samplePath = await EdgeTtsVoiceCatalog.CreateSampleAsync(voiceOption.Name, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_currentVoiceOption, voiceOption))
            {
                File.Delete(samplePath);
                return;
            }

            _voiceSamplePath = samplePath;
            VoiceSamplePlayer.Source = new Uri(samplePath);
            VoiceSamplePlayer.Play();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_currentVoiceOption, voiceOption))
            {
                VoiceCatalogStatus.Text = $"Could not play the {voiceOption.Name} sample: {ex.Message}";
                StopVoiceSample();
            }
        }
        finally
        {
            if (ReferenceEquals(_voiceSampleCancellation, cancellation))
            {
                _voiceSampleCancellation.Dispose();
                _voiceSampleCancellation = null;
            }
        }
    }

    private void StopVoiceSampleButton_Click(object sender, RoutedEventArgs e)
    {
        StopVoiceSample();
        VoiceCatalogStatus.Text = "Voice sample stopped.";
    }

    private void VoiceSamplePlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_currentVoiceOption is not { } voiceOption)
        {
            return;
        }

        voiceOption.IsLoading = false;
        voiceOption.IsPlaying = true;
        StopVoiceSampleButton.IsEnabled = true;
        VoiceCatalogStatus.Text = $"Playing sample: {voiceOption.Name}";
    }

    private void VoiceSamplePlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        StopVoiceSample();
        VoiceCatalogStatus.Text = "Voice sample finished.";
    }

    private void VoiceSamplePlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        VoiceCatalogStatus.Text = $"Voice sample playback failed: {e.ErrorException.Message}";
        StopVoiceSample();
    }

    private void AudioMuteService_MuteChanged(bool isMuted)
    {
        VoiceSamplePlayer.Volume = isMuted ? 0 : 1;
        if (isMuted)
        {
            StopVoiceSample();
            VoiceCatalogStatus.Text = "Voice sample stopped because audio was muted.";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        AudioMuteService.MuteChanged -= AudioMuteService_MuteChanged;
        StopVoiceSample();
        base.OnClosed(e);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FeedGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            FeedGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (MarkAsPlayedComboBox.SelectedItem is not ComboBoxItem { Tag: string markAsPlayed })
            {
                throw new InvalidDataException("Select when articles should be marked as played.");
            }

            if (ScanCountdownComboBox.SelectedItem is not ComboBoxItem { Tag: string countdownText } ||
                !int.TryParse(countdownText, NumberStyles.None, CultureInfo.InvariantCulture, out var scanCountdownSeconds))
            {
                throw new InvalidDataException("Select a supported Scan mode countdown.");
            }

            if (Mp3BitrateComboBox.SelectedItem is not ComboBoxItem { Tag: string bitrateText } ||
                !int.TryParse(bitrateText, NumberStyles.None, CultureInfo.InvariantCulture, out var mp3BitrateKbps))
            {
                throw new InvalidDataException("Select a supported MP3 bitrate.");
            }

            if (FeedUpdateIntervalComboBox.SelectedItem is not ComboBoxItem { Content: string feedUpdateInterval })
            {
                throw new InvalidDataException("Select a feed update polling interval.");
            }

            if (!double.TryParse(
                    ArticleListFontSizeComboBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var articleListFontSize))
            {
                throw new InvalidDataException("Title List font size must be a number between 8 and 48.");
            }

            if (!double.TryParse(
                    ArticleTextFontSizeComboBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var articleTextFontSize))
            {
                throw new InvalidDataException("Article Text font size must be a number between 8 and 48.");
            }

            if (!double.TryParse(
                    TickerFontSizeComboBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var tickerFontSize))
            {
                throw new InvalidDataException("Ticker font size must be a number between 8 and 48.");
            }

            if (!double.TryParse(
                    TickerHeightComboBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var tickerHeight))
            {
                throw new InvalidDataException("Ticker height must be a number between 48 and 200.");
            }

            if (TickerPositionComboBox.SelectedItem is not ComboBoxItem { Tag: string tickerPosition } ||
                TickerScrollSpeedComboBox.SelectedItem is not ComboBoxItem { Tag: string tickerSpeedText } ||
                TickerNotificationComboBox.SelectedItem is not ComboBoxItem { Tag: string tickerNotification } ||
                TickerClickActionComboBox.SelectedItem is not ComboBoxItem { Tag: string tickerClickAction } ||
                TickerRightClickActionComboBox.SelectedItem is not ComboBoxItem { Tag: string tickerRightClickAction })
            {
                throw new InvalidDataException("Select the ticker position, scroll speed, favorites notification, and both click actions.");
            }

            var voices = GetSelectedVoices();
            if (voices.Count == 0)
            {
                throw new InvalidDataException("Select at least one voice for Anchor(s).");
            }

            var updatedSettings = new AppSettings
            {
                Feeds = _feedConfigurations.Select(feed => feed.Url.Trim()).ToList(),
                FeedSources = _feedConfigurations
                    .Select(feed => new FeedConfiguration
                    {
                        Name = feed.Name.Trim(),
                        Url = feed.Url.Trim(),
                        Group = feed.Group,
                        UpdatePeriod = feed.UpdatePeriod,
                        Enabled = feed.Enabled
                    })
                    .ToList(),
                OutputDirectory = OutputDirectoryTextBox.Text.Trim(),
                FinalOutput = ArticleFileNaming.BuildFinalOutputTemplate(OutputDirectoryTextBox.Text.Trim()),
                Retention = RetentionTextBox.Text.Trim(),
                StarredRetention = StarredRetentionTextBox.Text.Trim(),
                FavoritesRetention = FavoritesRetentionTextBox.Text.Trim(),
                FeedUpdateInterval = feedUpdateInterval,
                PortableMode = PortableModeCheckBox.IsChecked.GetValueOrDefault(),
                MarkAsPlayed = markAsPlayed,
                ScanMode = _settings.ScanMode,
                ScanCountdownSeconds = scanCountdownSeconds,
                FullModeForFavorites = FullModeForFavoritesCheckBox.IsChecked.GetValueOrDefault(),
                BannedPhrases = BannedPhrasesEditor.Phrases.ToList(),
                FavoritedPhrases = FavoritedPhrasesEditor.Phrases.ToList(),
                StarredPhrases = StarredPhrasesEditor.Phrases.ToList(),
                ShowMatchingKeywords = ShowMatchingKeywordsCheckBox.IsChecked.GetValueOrDefault(),
                MarkReadWhenScrolledOutOfView = MarkReadWhenScrolledOutOfViewCheckBox.IsChecked.GetValueOrDefault(),
                AutoPlayNext = AutoPlayNextCheckBox.IsChecked.GetValueOrDefault(),
                RetrieveLatestFeedsOnLaunch = RetrieveLatestFeedsOnLaunchCheckBox.IsChecked.GetValueOrDefault(),
                AiPromptInstruction = AiPromptInstructionTextBox.Text.Trim(),
                AiChatEnabled = AiChatEnabledCheckBox.IsChecked.GetValueOrDefault(),
                MinimizeToTray = MinimizeToTrayCheckBox.IsChecked.GetValueOrDefault(),
                CloseToTray = CloseToTrayCheckBox.IsChecked.GetValueOrDefault(),
                LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked.GetValueOrDefault(),
                DebugLoggingEnabled = DebugLoggingCheckBox.IsChecked.GetValueOrDefault(),
                ShowArticleText = ShowArticleTextCheckBox.IsChecked.GetValueOrDefault(),
                ShowUnplayedOnly = ShowUnplayedOnlyCheckBox.IsChecked.GetValueOrDefault(),
                ShowStarredOnly = _settings.ShowStarredOnly,
                ShowFavoriteCategoriesOnly = _settings.ShowFavoriteCategoriesOnly,
                ArticleSortOrder = _settings.ArticleSortOrder,
                GroupArticlesByFeed = _settings.GroupArticlesByFeed,
                ApplicationLanguage = ApplicationLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string language }
                    ? language
                    : throw new InvalidDataException("Select an application language."),
                AppearanceTheme = InterfaceThemeComboBox.SelectedItem is ComboBoxItem { Tag: string theme }
                    ? theme
                    : throw new InvalidDataException("Select an interface theme."),
                Hotkeys = new HotkeySettings
                {
                    PlayPause = PlayPauseHotkeyTextBox.Text.Trim(),
                    SkipNext = SkipNextHotkeyTextBox.Text.Trim(),
                    PreviousBack = PreviousBackHotkeyTextBox.Text.Trim(),
                    VolumeUp = VolumeUpHotkeyTextBox.Text.Trim(),
                    VolumeDown = VolumeDownHotkeyTextBox.Text.Trim(),
                    Forward = ForwardHotkeyTextBox.Text.Trim(),
                    Backward = BackwardHotkeyTextBox.Text.Trim(),
                    Mute = MuteHotkeyTextBox.Text.Trim(),
                    FullMode = FullModeHotkeyTextBox.Text.Trim(),
                    ScanMode = ScanModeHotkeyTextBox.Text.Trim(),
                    RefreshLibrary = RefreshLibraryHotkeyTextBox.Text.Trim(),
                    RefreshArticle = RefreshArticleHotkeyTextBox.Text.Trim(),
                    MarkAllAsRead = MarkAllAsReadHotkeyTextBox.Text.Trim(),
                    Settings = SettingsHotkeyTextBox.Text.Trim()
                },
                Ticker = new TickerSettings
                {
                    ShowWhenMinimized = TickerWhenMinimizedCheckBox.IsChecked.GetValueOrDefault(),
                    ShowWhenClosedToTray = TickerWhenClosedToTrayCheckBox.IsChecked.GetValueOrDefault(),
                    MarkReadWhenTitleRead = TickerMarkReadCheckBox.IsChecked.GetValueOrDefault(),
                    ClickAction = tickerClickAction,
                    RightClickAction = tickerRightClickAction,
                    Position = tickerPosition,
                    FontFamily = TickerFontFamilyComboBox.Text.Trim(),
                    FontSize = tickerFontSize,
                    Height = tickerHeight,
                    ScrollSpeed = double.Parse(tickerSpeedText, CultureInfo.InvariantCulture),
                    FavoritesNotification = tickerNotification,
                    FavoritesSoundPath = TickerSoundPathTextBox.Text.Trim()
                },
                ArticleListFontFamily = ArticleListFontFamilyComboBox.Text.Trim(),
                ArticleListFontSize = articleListFontSize,
                ArticleTextFontFamily = ArticleTextFontFamilyComboBox.Text.Trim(),
                ArticleTextFontSize = articleTextFontSize,
                ArticleListWidthRatio = _settings.ArticleListWidthRatio,
                ArticleImageHeightRatio = _settings.ArticleImageHeightRatio,
                Mp3BitrateKbps = mp3BitrateKbps,
                TtsEngine = TtsEngineComboBox.SelectedItem is ComboBoxItem { Tag: string ttsEngine }
                    ? ttsEngine
                    : throw new InvalidDataException("Select a speech engine."),
                Voices = new VoiceSettings()
            };
            updatedSettings.Voices.SetVoices(voices);
            updatedSettings.Validate();
            if (_pendingCategoryPreferences.Count > 0)
            {
                _feedCategoryPreferencesService.SavePreferences(_pendingCategoryPreferences.Values);
            }
            UpdatedSettings = updatedSettings;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PopulateVoices(IEnumerable<VoiceCatalogEntry> availableVoices, IEnumerable<string> selectedVoices)
    {
        var available = availableVoices.ToList();
        var selected = selectedVoices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0 && available.Count > 0)
        {
            selected = GetDefaultVoices(available).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var voiceEntries = available
            .Concat(selected
                .Where(name => available.All(voice => !voice.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(name => new VoiceCatalogEntry(name, GetLocale(name), string.Empty)));
        var locales = voiceEntries
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => item.Locale, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var systemCulture = CultureInfo.CurrentCulture;
        var defaultLanguage = systemCulture.Name;
        if (string.IsNullOrWhiteSpace(defaultLanguage))
        {
            systemCulture = CultureInfo.CurrentUICulture;
            defaultLanguage = systemCulture.Name;
        }
        var exactLocale = locales.Any(group =>
            group.Key.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase));

        var groups = locales.Select(group =>
        {
            var culture = TryGetCulture(group.Key);
            var languageMatches = group.Key.StartsWith(
                defaultLanguage.Split('-')[0] + "-",
                StringComparison.OrdinalIgnoreCase);
            var selectedInGroup = group.Any(item => selected.Contains(item.Name));
            var localeGroup = new VoiceLocaleGroup
            {
                Locale = group.Key,
                DisplayName = culture?.DisplayName ?? group.Key,
                IsExpanded = group.Key.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase) ||
                    (!exactLocale && languageMatches) ||
                    selectedInGroup
            };
            foreach (var item in group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                localeGroup.Voices.Add(new VoiceOption
                {
                    Name = item.Name,
                    IsSelected = selected.Contains(item.Name)
                });
            }
            return localeGroup;
        }).ToList();
        VoicesTreeView.ItemsSource = groups
            .OrderByDescending(group => group.Locale.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase))
            .ThenBy(group => group.Locale, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selected.Count == 0 && available.Count == 0)
        {
            VoiceCatalogStatus.Text = "No saved voices. Load the edge-tts catalog to choose a default voice pair.";
        }
    }

    private static IReadOnlyList<string> GetDefaultVoices(IReadOnlyList<VoiceCatalogEntry> availableVoices)
    {
        var culture = CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(culture.Name))
        {
            culture = CultureInfo.CurrentUICulture;
        }

        var localeVoices = availableVoices
            .Where(voice => voice.Locale.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (localeVoices.Count < 2)
        {
            var languageVoices = availableVoices
                .Where(voice => voice.Locale.StartsWith(
                    culture.TwoLetterISOLanguageName + "-",
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(voice => voice.Locale.Equals(culture.Name, StringComparison.OrdinalIgnoreCase));
            localeVoices = localeVoices
                .Concat(languageVoices)
                .DistinctBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var familiarUsPair = new[] { "en-US-GuyNeural", "en-US-JennyNeural" };
        if (culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) &&
            familiarUsPair.All(name => localeVoices.Any(voice => voice.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
        {
            return familiarUsPair;
        }

        var defaults = new List<string>(2);
        foreach (var gender in new[] { "Female", "Male" })
        {
            var match = localeVoices.FirstOrDefault(voice =>
                voice.Gender.Equals(gender, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                defaults.Add(match.Name);
            }
        }

        foreach (var voice in localeVoices)
        {
            if (defaults.Count == 2)
            {
                break;
            }
            if (!defaults.Contains(voice.Name, StringComparer.OrdinalIgnoreCase))
            {
                defaults.Add(voice.Name);
            }
        }

        return defaults.Take(2).ToList();
    }

    private List<string> GetSelectedVoices() =>
        VoicesTreeView.Items
            .Cast<VoiceLocaleGroup>()
            .SelectMany(group => group.Voices)
            .Where(voice => voice.IsSelected)
            .Select(voice => voice.Name)
            .ToList();

    private void StopVoiceSample()
    {
        _voiceSampleCancellation?.Cancel();
        _voiceSampleCancellation?.Dispose();
        _voiceSampleCancellation = null;
        if (_currentVoiceOption is { } voiceOption)
        {
            voiceOption.IsLoading = false;
            voiceOption.IsPlaying = false;
            _currentVoiceOption = null;
        }
        VoiceSamplePlayer.Stop();
        VoiceSamplePlayer.Source = null;
        StopVoiceSampleButton.IsEnabled = false;
        if (_voiceSamplePath is { } samplePath)
        {
            File.Delete(samplePath);
            _voiceSamplePath = null;
        }
    }

    private static string GetLocale(string voice)
    {
        var match = Regex.Match(voice, @"^(?<locale>[A-Za-z]{2,3}-[A-Za-z]{2,3})-");
        return match.Success ? match.Groups["locale"].Value : "Other";
    }

    private static CultureInfo? TryGetCulture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
