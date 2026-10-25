using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AnchorFeeds.Models;
using AnchorFeeds.Services;

namespace AnchorFeeds;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private CancellationTokenSource? _voiceSampleCancellation;
    private string? _voiceSamplePath;
    private VoiceOption? _currentVoiceOption;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _settings = settings;
        ApplicationLanguageComboBox.SelectedItem = ApplicationLanguageComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.ApplicationLanguage, StringComparison.OrdinalIgnoreCase));
        AppearanceThemeComboBox.SelectedItem = AppearanceThemeComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.AppearanceTheme, StringComparison.OrdinalIgnoreCase));
        ScanCountdownComboBox.SelectedItem = ScanCountdownComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == settings.ScanCountdownSeconds);
        FeedsTextBox.Text = string.Join(Environment.NewLine, settings.Feeds);
        MaxArticlesTextBox.Text = settings.MaxArticlesPerFeed.ToString(CultureInfo.InvariantCulture);
        OutputDirectoryTextBox.Text = settings.OutputDirectory;
        FinalOutputTextBox.Text = settings.FinalOutput;
        RetentionTextBox.Text = settings.Retention;
        Mp3BitrateComboBox.SelectedItem = Mp3BitrateComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == settings.Mp3BitrateKbps);
        AutoPlayNextCheckBox.IsChecked = settings.AutoPlayNext;
        RetrieveLatestFeedsOnLaunchCheckBox.IsChecked = settings.RetrieveLatestFeedsOnLaunch;
        ShowArticleTextCheckBox.IsChecked = settings.ShowArticleText;
        ShowUnplayedOnlyCheckBox.IsChecked = settings.ShowUnplayedOnly;
        MarkAsPlayedComboBox.SelectedItem = MarkAsPlayedComboBox.Items
            .Cast<ComboBoxItem>()
            .First(item => string.Equals((string)item.Tag, settings.MarkAsPlayed, StringComparison.OrdinalIgnoreCase));
        PopulateVoices([], settings.Voices.GetVoices());
    }

    public AppSettings? UpdatedSettings { get; private set; }

    private void SettingHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ToolTip: string helpText })
        {
            throw new InvalidOperationException("The settings help button is missing its help text.");
        }

        MessageBox.Show(this, helpText, "Settings help", MessageBoxButton.OK, MessageBoxImage.Information);
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

    protected override void OnClosed(EventArgs e)
    {
        StopVoiceSample();
        base.OnClosed(e);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(MaxArticlesTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var maxArticles))
            {
                throw new InvalidDataException("Maximum new articles per feed must be a positive integer.");
            }

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

            var voices = GetSelectedVoices();
            if (voices.Count == 0)
            {
                throw new InvalidDataException("Select at least one voice for Anchor(s).");
            }

            var updatedSettings = new AppSettings
            {
                Feeds = FeedsTextBox.Text
                    .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                MaxArticlesPerFeed = maxArticles,
                OutputDirectory = OutputDirectoryTextBox.Text.Trim(),
                FinalOutput = FinalOutputTextBox.Text.Trim(),
                Retention = RetentionTextBox.Text.Trim(),
                MarkAsPlayed = markAsPlayed,
                PlaybackSpeed = _settings.PlaybackSpeed,
                ScanMode = _settings.ScanMode,
                ScanCountdownSeconds = scanCountdownSeconds,
                AutoPlayNext = AutoPlayNextCheckBox.IsChecked.GetValueOrDefault(),
                RetrieveLatestFeedsOnLaunch = RetrieveLatestFeedsOnLaunchCheckBox.IsChecked.GetValueOrDefault(),
                ShowArticleText = ShowArticleTextCheckBox.IsChecked.GetValueOrDefault(),
                ShowUnplayedOnly = ShowUnplayedOnlyCheckBox.IsChecked.GetValueOrDefault(),
                ShowStarredOnly = _settings.ShowStarredOnly,
                ArticleSortOrder = _settings.ArticleSortOrder,
                GroupArticlesByFeed = _settings.GroupArticlesByFeed,
                ApplicationLanguage = ApplicationLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string language }
                    ? language
                    : throw new InvalidDataException("Select an application language."),
                AppearanceTheme = AppearanceThemeComboBox.SelectedItem is ComboBoxItem { Tag: string theme }
                    ? theme
                    : throw new InvalidDataException("Select an appearance theme."),
                ArticleListWidthRatio = _settings.ArticleListWidthRatio,
                Mp3BitrateKbps = mp3BitrateKbps,
                Voices = new VoiceSettings()
            };
            updatedSettings.Voices.SetVoices(voices);
            updatedSettings.Validate();
            UpdatedSettings = updatedSettings;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
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
