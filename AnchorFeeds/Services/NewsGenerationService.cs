using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class NewsGenerationService(
    AppSettings settings,
    string applicationDirectory,
    FeedCategoryPreferencesService categoryPreferences)
{
    private const int EdgeTtsDefaultBitrateKbps = 48;
    private const int MaxNewArticlesPerFeedPerUpdate = 100;
    private const string EdgeTtsEngineName = "Edge TTS";
    private const string WindowsSpeechEngineName = "Windows Speech";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly FeedUpdateStateService _feedUpdateState = new(settings.FeedSources);
    private readonly string _outputDirectory = ArticleFileNaming.ResolveOutputDirectory(
        applicationDirectory,
        settings.OutputDirectory);
    private string TempDirectory => Path.Combine(_outputDirectory, ".tmp");

    public async Task<(int GeneratedCount, IReadOnlyList<string> FailedFeeds, IReadOnlyList<string> MediaWarnings)> GenerateAsync(
        IProgress<string> progress,
        IProgress<ArticleAudio> generatedArticles,
        CancellationToken cancellationToken,
        ArticleAudio? articleToRebuild = null,
        bool automatic = false,
        string? ttsEngineOverride = null)
    {
        if (settings.Voices.GetVoices().Count == 0)
        {
            throw new InvalidDataException("Select at least one voice in Settings before retrieving feeds.");
        }

        var voices = settings.Voices.GetVoices();
        var ttsEngine = ttsEngineOverride ?? settings.TtsEngine;
        var edgeTtsPath = AppSettings.UsesEdgeTts(ttsEngine)
            ? EdgeTtsVoiceCatalog.FindExecutable()
            : string.Empty;
        Directory.CreateDirectory(_outputDirectory);
        Directory.CreateDirectory(TempDirectory);
        var generated = 0;
        var nextVoiceIndex = 0;
        var failedFeeds = new List<string>();
        var mediaWarnings = new List<string>();
        var rebuildArticleFound = false;
        var bannedMatcher = new PhraseMatcher(settings.BannedPhrases);
        var starredMatcher = new PhraseMatcher(settings.StarredPhrases);

        for (var feedIndex = 0; feedIndex < settings.FeedSources.Count; feedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feed = settings.FeedSources[feedIndex];
            var feedUrl = feed.Url;
            var feedUri = new Uri(feedUrl);
            if (articleToRebuild is null && !feed.Enabled)
            {
                continue;
            }

            if (articleToRebuild is not null &&
                !string.Equals(articleToRebuild.Feed, feedUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (automatic &&
                (feed.GetEffectiveUpdatePeriod(settings.FeedUpdateIntervalPeriod) is not { } interval ||
                 (_feedUpdateState.GetLastCheckedAt(feedUrl) is { } lastUpdated &&
                  DateTimeOffset.UtcNow - lastUpdated < interval)))
            {
                continue;
            }

            var feedSlug = ArticleFileNaming.BuildFeedSlug(feedUrl);
            progress.Report($"Fetching feed {feedIndex + 1}/{settings.FeedSources.Count}: {feed.Name}");
            string xml;
            try
            {
                xml = await HttpClient.GetStringAsync(feedUri, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                failedFeeds.Add($"{feedUrl}: {ex.Message}");
                progress.Report($"Skipping feed {feed.Name}: {ex.Message}");
                continue;
            }

            var articles = ParseArticles(xml, feedUri);
            var feedCategoryPreferences = categoryPreferences.Get(feedUrl);
            _feedUpdateState.MarkFeedChecked(feedUrl);
            if (articleToRebuild is null)
            {
                articles = articles
                    .OrderBy(article => article.PublishedAt ?? DateTimeOffset.MinValue)
                    .ToList();
            }

            var generatedForFeed = 0;

            foreach (var article in articles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (articleToRebuild is null &&
                    (FeedCategoryPreferencesService.IsBlocked(feedCategoryPreferences, article.Categories) ||
                     bannedMatcher.IsMatch(article.Title, article.Description)))
                {
                    _feedUpdateState.MarkItemProcessed(
                        feedUrl,
                        article.PublishedAt,
                        article.ArticleUrl,
                        article.Title);
                    continue;
                }

                if (articleToRebuild is null &&
                    article.PublishedAt is { } publishedAt &&
                    publishedAt < DateTimeOffset.UtcNow - settings.RetentionPeriod)
                {
                    _feedUpdateState.MarkItemProcessed(
                        feedUrl,
                        article.PublishedAt,
                        article.ArticleUrl,
                        article.Title);
                    continue;
                }

                if (articleToRebuild is not null &&
                    !MatchesArticle(articleToRebuild, article, feedUri.Host))
                {
                    continue;
                }

                if (articleToRebuild is null &&
                    _feedUpdateState.IsProcessed(
                        feedUrl,
                        article.PublishedAt,
                        article.ArticleUrl,
                        article.Title))
                {
                    continue;
                }

                if (articleToRebuild is null && generatedForFeed >= MaxNewArticlesPerFeedPerUpdate)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(article.Title))
                {
                    if (articleToRebuild is null)
                    {
                        _feedUpdateState.MarkItemProcessed(
                            feedUrl,
                            article.PublishedAt,
                            article.ArticleUrl,
                            article.Title);
                    }
                    continue;
                }

                if (articleToRebuild is not null)
                {
                    rebuildArticleFound = true;
                }

                var titleSlug = ArticleFileNaming.BuildTitleSlug(article.Title);
                var configuredOutputPath = ArticleFileNaming.BuildLegacyOutputPath(
                    _outputDirectory,
                    feedSlug,
                    titleSlug);
                var articleFolder = ArticleFileNaming.BuildArticleFolderPath(
                    _outputDirectory,
                    feedSlug,
                    titleSlug);
                var bodyPath = ArticleFileNaming.BuildBodyAudioPath(articleFolder);
                var titlePath = Path.Combine(articleFolder, "title.mp3");
                var selectedBodyPath = bodyPath;
                if (articleToRebuild is not null)
                {
                    ClearArticleFiles(articleToRebuild, articleFolder, configuredOutputPath);
                }
                Directory.CreateDirectory(articleFolder);

                var titleVoiceIndex = nextVoiceIndex;
                var titleAudioEngine = articleToRebuild?.TitleAudioEngine ?? string.Empty;
                if (!File.Exists(titlePath) || new FileInfo(titlePath).Length == 0)
                {
                    progress.Report($"Generating title audio for {article.Title}");
                    try
                    {
                        var titleGeneration = await GenerateAudioFileAsync(
                            edgeTtsPath,
                            voices,
                            nextVoiceIndex,
                            article.Title,
                            titlePath,
                            article.Title,
                            feedUri.Host,
                            settings.Mp3BitrateKbps,
                            ttsEngine,
                            progress,
                            cancellationToken);
                        titleVoiceIndex = titleGeneration.VoiceIndex;
                        titleAudioEngine = titleGeneration.EngineName;
                    }
                    catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        // A missing title clip is not fatal; the body audio is still attempted.
                        DiagnosticLog.Write("Generation", $"Title audio failed for \"{article.Title}\": {ex.Message}");
                        titlePath = string.Empty;
                    }
                }
                else if (string.IsNullOrWhiteSpace(titleAudioEngine))
                {
                    titleAudioEngine = "Unknown";
                }

                string? cachedImagePath = ArticleMediaCache.FindCachedImage(articleFolder);
                var imageUrl = string.IsNullOrWhiteSpace(article.ImageUrl)
                    ? article.SourceLogoUrl
                    : article.ImageUrl;
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    try
                    {
                        cachedImagePath = await ArticleMediaCache.DownloadImageAsync(
                            imageUrl,
                            articleFolder,
                            cancellationToken) ?? cachedImagePath;
                    }
                    catch (HttpRequestException ex)
                    {
                        mediaWarnings.Add($"{article.Title}: {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        mediaWarnings.Add($"{article.Title}: {ex.Message}");
                    }
                    catch (InvalidDataException ex)
                    {
                        mediaWarnings.Add($"{article.Title}: {ex.Message}");
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        mediaWarnings.Add($"{article.Title}: image caching timed out: {ex.Message}");
                    }
                }

                var bodyText = ArticleContentFormatter.CreatePlainText(
                    article.DescriptionHtml,
                    string.IsNullOrWhiteSpace(article.Description)
                        ? article.Title
                        : article.Description);
                var articleAudio = new ArticleAudio
                {
                    FilePath = selectedBodyPath,
                    TitleAudioPath = titlePath,
                    TitleAudioEngine = titleAudioEngine,
                    Categories = article.Categories.ToList(),
                    CachedImagePath = cachedImagePath ?? string.Empty,
                    ArticleFolderPath = articleFolder,
                    Title = article.Title,
                    Feed = feedUri.Host,
                    FeedName = feed.Name,
                    FeedUrl = feed.Url,
                    ArticleUrl = article.ArticleUrl,
                    ImageUrl = article.ImageUrl,
                    SourceLogoUrl = article.SourceLogoUrl,
                    AudioText = bodyText,
                    ArticleContentHtml = article.DescriptionHtml,
                    PublishedAt = article.PublishedAt,
                    CreatedAt = DateTime.Now,
                    IsStarred = articleToRebuild is null && starredMatcher.IsMatch(article.Title, bodyText)
                };
                nextVoiceIndex = (titleVoiceIndex + 1) % voices.Count;
                var errorMarkerPath = Path.Combine(articleFolder, AudioLibraryService.ErrorMarkerFileName);
                try
                {
                    var bodyGeneration = await GenerateAudioFileAsync(
                        edgeTtsPath,
                        voices,
                        titleVoiceIndex,
                        bodyText,
                        bodyPath,
                        article.Title,
                        feedUri.Host,
                        settings.Mp3BitrateKbps,
                        ttsEngine,
                        progress,
                        cancellationToken);
                    articleAudio.BodyAudioEngine = bodyGeneration.EngineName;
                    nextVoiceIndex = (bodyGeneration.VoiceIndex + 1) % voices.Count;
                    if (File.Exists(errorMarkerPath))
                    {
                        File.Delete(errorMarkerPath);
                    }

                    generated++;
                    generatedForFeed++;
                    articleAudio.CreatedAt = File.GetCreationTime(bodyPath);
                }
                catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // Keep the article in the library, flagged, instead of aborting the whole run.
                    var summary = ex.InnerException?.Message ?? ex.Message;
                    summary = summary.Split('\n', 2)[0].Trim();
                    articleAudio.AudioError = summary;
                    File.WriteAllText(errorMarkerPath, summary);
                    progress.Report($"Audio generation failed for {article.Title}; the article is marked with an error.");
                    DiagnosticLog.Write("Generation", $"Body audio failed for \"{article.Title}\": {ex}");
                }

                if (articleToRebuild is null)
                {
                    _feedUpdateState.MarkItemProcessed(
                        feedUrl,
                        article.PublishedAt,
                        article.ArticleUrl,
                        article.Title);
                }
                generatedArticles.Report(articleAudio);
            }
        }

        if (articleToRebuild is not null && !rebuildArticleFound)
        {
            var failedFeed = failedFeeds.FirstOrDefault();
            throw new InvalidOperationException(failedFeed is null
                ? $"Could not find \"{articleToRebuild.Title}\" in the configured feeds. The article may have been removed or changed."
                : $"Could not rebuild \"{articleToRebuild.Title}\" because its feed could not be retrieved: {failedFeed}");
        }

        progress.Report(
            failedFeeds.Count == 0
                ? $"Generation complete. Created {generated} new article(s)."
                : $"Generation complete. Created {generated} new article(s); skipped {failedFeeds.Count} feed(s).");
        return (generated, failedFeeds, mediaWarnings);
    }

    private static bool MatchesArticle(ArticleAudio target, NewsArticle article, string feedHost) =>
        string.Equals(target.Feed, feedHost, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(target.ArticleUrl, article.ArticleUrl, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(target.Title, article.Title, StringComparison.OrdinalIgnoreCase));

    private void ClearArticleFiles(ArticleAudio target, string articleFolder, string legacyAudioPath)
    {
        var targetFolder = Path.GetFullPath(target.ArticleFolderPath);
        if (IsDirectChildOfOutputDirectory(targetFolder) && Directory.Exists(targetFolder))
        {
            Directory.Delete(targetFolder, recursive: true);
        }

        var newFolder = Path.GetFullPath(articleFolder);
        if (IsDirectChildOfOutputDirectory(newFolder) && Directory.Exists(newFolder))
        {
            Directory.Delete(newFolder, recursive: true);
        }

        foreach (var audioPath in new[] { target.FilePath, legacyAudioPath }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(audioPath);
            if (IsDirectChildOfOutputDirectory(fullPath) &&
                Path.GetExtension(fullPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    private bool IsDirectChildOfOutputDirectory(string path) =>
        string.Equals(Path.GetDirectoryName(path), _outputDirectory, StringComparison.OrdinalIgnoreCase);

    private async Task<AudioGenerationResult> GenerateAudioFileAsync(
        string edgeTtsPath,
        IReadOnlyList<string> voices,
        int startingVoiceIndex,
        string text,
        string outputPath,
        string articleTitle,
        string feedHost,
        int bitrateKbps,
        string ttsEngine,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException($"Audio output path has no directory: {outputPath}");
        Directory.CreateDirectory(outputDirectory);
        var tempId = Guid.NewGuid().ToString("N");
        var sourceTempPath = Path.Combine(TempDirectory, $"{tempId}.source.mp3");
        var tempPath = Path.Combine(TempDirectory, $"{tempId}.tmp.mp3");
        try
        {
            var voiceIndex = startingVoiceIndex % voices.Count;
            var edgeProducedAudio = false;
            if (AppSettings.UsesEdgeTts(ttsEngine))
            {
                var maxAttempts = ttsEngine == AppSettings.EdgeTtsOnlyEngine ? 3 : 1;
                for (var attempt = 1; attempt <= maxAttempts && !edgeProducedAudio; attempt++)
                {
                    try
                    {
                        voiceIndex = await RunEdgeTtsAsync(
                            edgeTtsPath,
                            voices,
                            startingVoiceIndex,
                            text,
                            sourceTempPath,
                            articleTitle,
                            feedHost,
                            bitrateKbps,
                            progress,
                            cancellationToken);
                        edgeProducedAudio = File.Exists(sourceTempPath) && new FileInfo(sourceTempPath).Length > 0;
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (attempt < maxAttempts)
                        {
                            progress.Report($"Edge TTS failed for {articleTitle} ({ex.Message}); retrying (attempt {attempt + 1} of {maxAttempts}).");
                        }
                        else if (maxAttempts == 1)
                        {
                            progress.Report($"Edge TTS failed for {articleTitle} ({ex.Message}); using Windows speech instead.");
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Edge TTS failed for '{articleTitle}' after {maxAttempts} attempts: {ex.Message}", ex);
                        }
                    }
                }
            }

            if (!edgeProducedAudio && ttsEngine == AppSettings.EdgeTtsOnlyEngine)
            {
                throw new InvalidOperationException($"Edge TTS did not produce audio for '{articleTitle}' and fallback is disabled.");
            }

            if (!edgeProducedAudio)
            {
                var wavPath = Path.Combine(TempDirectory, $"{tempId}.source.wav");
                try
                {
                    progress.Report($"Generating {articleTitle} with Windows speech");
                    await Task.Run(() => SynthesizeWithWindowsSpeech(text, wavPath), cancellationToken);
                    await RunFfmpegAsync(
                        FindFfmpeg(),
                        wavPath,
                        tempPath,
                        bitrateKbps == 0 ? EdgeTtsDefaultBitrateKbps : bitrateKbps,
                        cancellationToken);
                }
                finally
                {
                    if (File.Exists(wavPath))
                    {
                        File.Delete(wavPath);
                    }
                }
            }
            else if (bitrateKbps is 0 or EdgeTtsDefaultBitrateKbps)
            {
                File.Move(sourceTempPath, tempPath, true);
            }
            else
            {
                await RunFfmpegAsync(
                    FindFfmpeg(),
                    sourceTempPath,
                    tempPath,
                    bitrateKbps,
                    cancellationToken);
            }

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new InvalidOperationException($"Audio processing did not create an MP3 for '{articleTitle}'.");
            }

            File.Move(tempPath, outputPath, true);
            return new AudioGenerationResult(
                voiceIndex,
                edgeProducedAudio ? EdgeTtsEngineName : WindowsSpeechEngineName);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            if (File.Exists(sourceTempPath))
            {
                File.Delete(sourceTempPath);
            }
        }
    }

    private readonly record struct AudioGenerationResult(int VoiceIndex, string EngineName);

    private async Task<int> RunEdgeTtsAsync(
        string edgeTtsPath,
        IReadOnlyList<string> voices,
        int startingVoiceIndex,
        string text,
        string outputPath,
        string articleTitle,
        string feedHost,
        int bitrateKbps,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var voiceIndex = startingVoiceIndex % voices.Count;
        var voice = voices[voiceIndex];
        var outputQuality = bitrateKbps == 0 ? "Edge TTS default quality" : $"{bitrateKbps} kbps";
        progress.Report($"Generating {articleTitle} with {voice} at {outputQuality}");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        try
        {
            await RunEdgeTtsAsync(edgeTtsPath, voice, text, outputPath, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"edge-tts failed for article '{articleTitle}' from {feedHost} using voice '{voice}': {ex.Message}",
                ex);
        }

        return voiceIndex;
    }

    private static void SynthesizeWithWindowsSpeech(string text, string wavPath)
    {
        using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
        synthesizer.SetOutputToWaveFile(wavPath);
        synthesizer.Speak(text);
    }

    private async Task RunEdgeTtsAsync(
        string edgeTtsPath,
        string voice,
        string text,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var textPath = Path.Combine(Path.GetTempPath(), $"anchorfeeds-tts-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(textPath, text, new System.Text.UTF8Encoding(false), cancellationToken);
        var startInfo = new ProcessStartInfo
        {
            FileName = edgeTtsPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--voice");
        startInfo.ArgumentList.Add(voice);
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(textPath);
        startInfo.ArgumentList.Add("--write-media");
        startInfo.ArgumentList.Add(outputPath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start edge-tts.");
            }

            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                throw;
            }

            var error = await standardError;
            _ = await standardOutput;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"edge-tts failed (exit code {process.ExitCode}): {error.Trim()}");
            }
        }
        finally
        {
            try
            {
                File.Delete(textPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task RunFfmpegAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        int bitrateKbps,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-codec:a");
        startInfo.ArgumentList.Add("libmp3lame");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add($"{bitrateKbps}k");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start FFmpeg.");
        }

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }

        var error = await standardError;
        _ = await standardOutput;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed to encode MP3 at {bitrateKbps} kbps (exit code {process.ExitCode}): {error.Trim()}");
        }
    }

    private static List<NewsArticle> ParseArticles(string xml, Uri feedUri)
    {
        var result = new List<NewsArticle>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        var sourceLogoUrl = GetSourceLogoUrl(document, feedUri);

        foreach (XmlNode item in document.SelectNodes("//*[local-name()='item' or local-name()='entry']")
                     ?? throw new InvalidDataException("The feed has no RSS items or Atom entries."))
        {
            var title = item.SelectSingleNode("./*[local-name()='title']")?.InnerText ?? string.Empty;
            var description = GetArticleDescription(item);
            result.Add(new NewsArticle(
                CleanNewsText(title),
                CleanNewsText(description),
                description,
                GetArticleUrl(item),
                GetImageUrl(item),
                sourceLogoUrl,
                GetPublishedAt(item),
                FeedCategoryPreferencesService.ExtractCategories(item)));
        }

        return result;
    }

    private static string GetArticleDescription(XmlNode item)
    {
        var contentNode = item.SelectSingleNode("./*[local-name()='encoded']") ??
                          item.SelectSingleNode("./*[local-name()='content']") ??
                          item.SelectSingleNode("./*[local-name()='description']") ??
                          item.SelectSingleNode("./*[local-name()='summary']");
        return contentNode is null
            ? string.Empty
            : contentNode.ChildNodes.Cast<XmlNode>().Any(child => child.NodeType == XmlNodeType.Element)
                ? contentNode.InnerXml
                : contentNode.InnerText;
    }

    private static DateTimeOffset? GetPublishedAt(XmlNode item)
    {
        foreach (var elementName in new[] { "pubDate", "published", "updated", "date" })
        {
            var dateText = item.SelectSingleNode($"./*[local-name()='{elementName}']")?.InnerText.Trim();
            if (DateTimeOffset.TryParse(
                    dateText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var publishedAt))
            {
                return publishedAt;
            }
        }

        return null;
    }

    private static string GetSourceLogoUrl(XmlDocument document, Uri feedUri)
    {
        var logoNode = document.SelectSingleNode(
            "/*[local-name()='rss']/*[local-name()='channel']/*[local-name()='image']/*[local-name()='url']") ??
            document.SelectSingleNode("/*[local-name()='feed']/*[local-name()='logo']") ??
            document.SelectSingleNode("/*[local-name()='feed']/*[local-name()='icon']");
        var logo = logoNode?.InnerText.Trim();
        if (string.IsNullOrWhiteSpace(logo))
        {
            var siteUrl = document.SelectSingleNode(
                "/*[local-name()='rss']/*[local-name()='channel']/*[local-name()='link']")?.InnerText.Trim();
            if (Uri.TryCreate(siteUrl, UriKind.Absolute, out var siteUri) && siteUri.Scheme is "http" or "https")
            {
                logo = new Uri(siteUri, "/favicon.ico").AbsoluteUri;
            }
        }

        if (Uri.TryCreate(feedUri, logo, out var logoUri) && logoUri.Scheme is "http" or "https")
        {
            return logoUri.AbsoluteUri;
        }

        return new Uri(feedUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico").AbsoluteUri;
    }

    private static string GetArticleUrl(XmlNode item)
    {
        var links = item.SelectNodes("./*[local-name()='link']");
        if (links is not null)
        {
            string? fallbackLink = null;
            foreach (XmlNode linkNode in links)
            {
                var href = linkNode.Attributes?["href"]?.Value;
                var link = string.IsNullOrWhiteSpace(href) ? linkNode.InnerText.Trim() : href.Trim();
                var relation = linkNode.Attributes?["rel"]?.Value;
                if (!IsHttpUrl(link))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(relation) ||
                    relation.Equals("alternate", StringComparison.OrdinalIgnoreCase))
                {
                    return link;
                }

                if (!relation.Equals("self", StringComparison.OrdinalIgnoreCase) &&
                    !relation.Equals("enclosure", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackLink ??= link;
                }
            }

            if (fallbackLink is not null)
            {
                return fallbackLink;
            }
        }

        return string.Empty;
    }

    private static string GetImageUrl(XmlNode item)
    {
        var mediaNodes = item.SelectNodes(".//*[local-name()='thumbnail' or local-name()='content' or local-name()='image' or local-name()='enclosure']");
        if (mediaNodes is not null)
        {
            foreach (XmlNode media in mediaNodes)
            {
                var imageUrl = media.Attributes?["url"]?.Value ??
                               media.Attributes?["href"]?.Value ??
                               media.Attributes?["src"]?.Value;
                var mediaType = media.Attributes?["type"]?.Value;
                if ((string.IsNullOrWhiteSpace(mediaType) || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) &&
                    IsHttpUrl(imageUrl))
                {
                    return imageUrl!;
                }
            }
        }

        var description = WebUtility.HtmlDecode(item.SelectSingleNode("./*[local-name()='description']")?.InnerText ?? string.Empty);
        var match = Regex.Match(description, """<img\b[^>]*?\bsrc\s*=\s*["'](?<url>[^"']+)["']""", RegexOptions.IgnoreCase);
        var embeddedUrl = match.Success ? WebUtility.HtmlDecode(match.Groups["url"].Value) : null;
        return IsHttpUrl(embeddedUrl) ? embeddedUrl! : string.Empty;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string CleanNewsText(string value)
    {
        var text = WebUtility.HtmlDecode(value);
        text = Regex.Replace(text, @"<!\[CDATA\[|\]\]>", " ");
        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
        text = Regex.Replace(
            text,
            @"<(script|style|noscript|iframe|svg|figcaption|template)\b.*?</\1\s*>",
            " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/li|/h[1-6])\b[^>]*>", ". ", RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"<a\b[^>]*>(?<text>[\s\S]*?)</a\s*>",
            match =>
            {
                var linkText = Regex.Replace(match.Groups["text"].Value, "<[^>]*>", " ");
                linkText = WebUtility.HtmlDecode(WebUtility.HtmlDecode(linkText));
                return IsAllCapsLinkText(linkText) ? " " : match.Groups["text"].Value;
            },
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]*>", " ");
        // Feeds sometimes double-encode entities, leaving "&amp;nbsp;" after the first decode.
        text = WebUtility.HtmlDecode(text);

        text = Regex.Replace(
            text,
            @"!?\[([^\]]*)\]\([^)]*\)",
            match => IsAllCapsLinkText(match.Groups[1].Value) ? " " : match.Groups[1].Value);
        text = Regex.Replace(text, @"\[(?:\.{2,3}|…|&#8230;)\]", " ");
        text = Regex.Replace(text, @"\b(?:https?://|www\.)\S+", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b", " ");
        text = Regex.Replace(text, @"(?<!\w)[#@](?=\w)", string.Empty);
        text = Regex.Replace(text, @"(\*{1,3}|_{2,}|`+|~{2,})", string.Empty);
        text = Regex.Replace(text, @"(?m)^\s*(?:#{1,6}|>|[-*+•▪●◦]|\d+[.)])\s+", string.Empty);
        text = Regex.Replace(
            text,
            @"(?i)\b(?:the post\b.*?\bappeared first on\b.*?(?:\.|$)|(?:read|continue reading|click here to read|view|see|learn) (?:more|the (?:full|rest of the) (?:article|story|post))[^.!?]*[.!?]?|(?:appeared first|originally published) (?:on|at)\b.*?(?:\.|$)|sign up for\b.*?(?:newsletter|alerts)[^.!?]*[.!?]?)",
            " ");

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            switch (category)
            {
                case System.Globalization.UnicodeCategory.Control:
                case System.Globalization.UnicodeCategory.Format:
                case System.Globalization.UnicodeCategory.PrivateUse:
                case System.Globalization.UnicodeCategory.Surrogate:
                case System.Globalization.UnicodeCategory.OtherNotAssigned:
                case System.Globalization.UnicodeCategory.NonSpacingMark when rune.Value is 0xFE0E or 0xFE0F:
                case System.Globalization.UnicodeCategory.ModifierSymbol:
                    builder.Append(' ');
                    break;
                case System.Globalization.UnicodeCategory.OtherSymbol when rune.Value != 0x00B0:
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(rune.ToString());
                    break;
            }
        }

        var cleaned = Regex.Replace(builder.ToString(), @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"(\s*\.){2,}\s*(?=\S)", ". ");
        cleaned = Regex.Replace(cleaned, @"\s+([.,;:!?])", "$1");
        return cleaned.Trim();
    }

    private static bool IsAllCapsLinkText(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    public static bool IsFfmpegAvailable()
    {
        try
        {
            FindFfmpeg();
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static string FindFfmpeg()
    {
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathCommand = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
        return pathCommand
            ?? throw new FileNotFoundException("FFmpeg was not found. Install FFmpeg and ensure ffmpeg.exe is on PATH.");
    }

    private sealed record NewsArticle(
        string Title,
        string Description,
        string DescriptionHtml,
        string ArticleUrl,
        string ImageUrl,
        string SourceLogoUrl,
        DateTimeOffset? PublishedAt,
        IReadOnlyList<string> Categories);
}
