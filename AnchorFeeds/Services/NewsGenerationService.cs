using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class NewsGenerationService(AppSettings settings, string applicationDirectory)
{
    private const int EdgeTtsDefaultBitrateKbps = 48;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string _outputDirectory = ArticleFileNaming.ResolveOutputDirectory(
        applicationDirectory,
        settings.FinalOutput);
    private readonly string _tempDirectory = ResolvePath(applicationDirectory, settings.OutputDirectory);

    public async Task<(int GeneratedCount, IReadOnlyList<string> FailedFeeds, IReadOnlyList<string> MediaWarnings)> GenerateAsync(
        IProgress<string> progress,
        IProgress<ArticleAudio> generatedArticles,
        CancellationToken cancellationToken)
    {
        if (settings.Voices.GetVoices().Count == 0)
        {
            throw new InvalidDataException("Select at least one voice in Settings before retrieving feeds.");
        }

        var voices = settings.Voices.GetVoices();
        var edgeTtsPath = EdgeTtsVoiceCatalog.FindExecutable();
        Directory.CreateDirectory(_outputDirectory);
        Directory.CreateDirectory(_tempDirectory);
        var generated = 0;
        var nextVoiceIndex = 0;
        var failedFeeds = new List<string>();
        var mediaWarnings = new List<string>();

        for (var feedIndex = 0; feedIndex < settings.Feeds.Count; feedIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feedUrl = settings.Feeds[feedIndex];
            var feedUri = new Uri(feedUrl);
            var feedSlug = ArticleFileNaming.BuildFeedSlug(feedUrl);
            progress.Report($"Fetching feed {feedIndex + 1}/{settings.Feeds.Count}: {feedUri.Host}");
            string xml;
            try
            {
                xml = await HttpClient.GetStringAsync(feedUri, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                failedFeeds.Add($"{feedUrl}: {ex.Message}");
                progress.Report($"Skipping feed {feedUri.Host}: {ex.Message}");
                continue;
            }

            var articles = ParseArticles(xml, feedUri);
            var generatedForFeed = 0;

            foreach (var article in articles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (generatedForFeed >= settings.MaxArticlesPerFeed)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(article.Title))
                {
                    continue;
                }

                var titleSlug = ArticleFileNaming.BuildTitleSlug(article.Title);
                var configuredOutputPath = ArticleFileNaming.BuildLegacyOutputPath(
                    _outputDirectory,
                    settings.FinalOutput,
                    feedSlug,
                    titleSlug);
                var articleFolder = ArticleFileNaming.BuildArticleFolderPath(
                    _outputDirectory,
                    settings.FinalOutput,
                    feedSlug,
                    titleSlug);
                var bodyPath = ArticleFileNaming.BuildBodyAudioPath(settings.FinalOutput, articleFolder);
                var titlePath = Path.Combine(articleFolder, "title.mp3");
                var legacyBodyExists = File.Exists(configuredOutputPath) &&
                    new FileInfo(configuredOutputPath).Length > 0;
                var selectedBodyPath = File.Exists(bodyPath) ? bodyPath :
                    legacyBodyExists ? configuredOutputPath : bodyPath;
                Directory.CreateDirectory(articleFolder);

                var titleVoiceIndex = nextVoiceIndex;
                if (!File.Exists(titlePath) || new FileInfo(titlePath).Length == 0)
                {
                    progress.Report($"Generating title audio for {article.Title}");
                    titleVoiceIndex = await GenerateAudioFileAsync(
                        edgeTtsPath,
                        voices,
                        nextVoiceIndex,
                        article.Title,
                        titlePath,
                        article.Title,
                        feedUri.Host,
                        settings.Mp3BitrateKbps,
                        progress,
                        cancellationToken);
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

                if (!File.Exists(bodyPath) && legacyBodyExists)
                {
                    try
                    {
                        File.Move(configuredOutputPath, bodyPath);
                        selectedBodyPath = bodyPath;
                    }
                    catch (IOException ex)
                    {
                        mediaWarnings.Add(
                            $"{article.Title}: the existing audio file could not be moved into its article folder: {ex.Message}");
                    }
                }

                var bodyText = string.IsNullOrWhiteSpace(article.Description)
                    ? article.Title
                    : article.Description;
                var articleAudio = new ArticleAudio
                {
                    FilePath = selectedBodyPath,
                    TitleAudioPath = titlePath,
                    CachedImagePath = cachedImagePath ?? string.Empty,
                    ArticleFolderPath = articleFolder,
                    Title = article.Title,
                    Feed = feedUri.Host,
                    ArticleUrl = article.ArticleUrl,
                    ImageUrl = article.ImageUrl,
                    SourceLogoUrl = article.SourceLogoUrl,
                    AudioText = bodyText,
                    PublishedAt = article.PublishedAt,
                    CreatedAt = File.Exists(selectedBodyPath) ? File.GetCreationTime(selectedBodyPath) : DateTime.Now
                };
                if (File.Exists(selectedBodyPath) && new FileInfo(selectedBodyPath).Length > 0)
                {
                    generatedArticles.Report(articleAudio);
                    progress.Report($"Skipping existing: {article.Title}");
                    continue;
                }

                nextVoiceIndex = (titleVoiceIndex + 1) % voices.Count;
                nextVoiceIndex = (await GenerateAudioFileAsync(
                    edgeTtsPath,
                    voices,
                    titleVoiceIndex,
                    bodyText,
                    bodyPath,
                    article.Title,
                    feedUri.Host,
                    settings.Mp3BitrateKbps,
                    progress,
                    cancellationToken) + 1) % voices.Count;

                generated++;
                generatedForFeed++;
                articleAudio.CreatedAt = File.GetCreationTime(bodyPath);
                generatedArticles.Report(articleAudio);
            }
        }

        progress.Report(
            failedFeeds.Count == 0
                ? $"Generation complete. Created {generated} new article(s)."
                : $"Generation complete. Created {generated} new article(s); skipped {failedFeeds.Count} feed(s).");
        return (generated, failedFeeds, mediaWarnings);
    }

    private async Task<int> GenerateAudioFileAsync(
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
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException($"Audio output path has no directory: {outputPath}");
        Directory.CreateDirectory(outputDirectory);
        var tempId = Guid.NewGuid().ToString("N");
        var sourceTempPath = Path.Combine(outputDirectory, $".{tempId}.source.mp3");
        var tempPath = Path.Combine(outputDirectory, $".{tempId}.tmp.mp3");
        try
        {
            var voiceIndex = await RunEdgeTtsWithVoiceFallbackAsync(
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
            if (!File.Exists(sourceTempPath) || new FileInfo(sourceTempPath).Length == 0)
            {
                throw new InvalidOperationException($"Edge TTS did not create audio for '{articleTitle}'.");
            }

            if (bitrateKbps is 0 or EdgeTtsDefaultBitrateKbps)
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
            return voiceIndex;
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

    private async Task<int> RunEdgeTtsWithVoiceFallbackAsync(
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
        Exception? lastFailure = null;
        var triedVoices = new List<string>();
        for (var attempt = 0; attempt < voices.Count; attempt++)
        {
            var voiceIndex = (startingVoiceIndex + attempt) % voices.Count;
            var voice = voices[voiceIndex];
            triedVoices.Add(voice);
            var outputQuality = bitrateKbps == 0 ? "Edge TTS default quality" : $"{bitrateKbps} kbps";
            progress.Report($"Generating {articleTitle} with {voice} at {outputQuality}");
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            try
            {
                await RunEdgeTtsAsync(edgeTtsPath, voice, text, outputPath, cancellationToken);
                return voiceIndex;
            }
            catch (InvalidOperationException ex) when (IsNoAudioReceived(ex))
            {
                lastFailure = ex;
                if (attempt + 1 < voices.Count)
                {
                    progress.Report($"No audio received from {voice}; trying another selected voice for {articleTitle}.");
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"edge-tts failed for article '{articleTitle}' from {feedHost} using voice '{voice}': {ex.Message}",
                    ex);
            }
        }

        throw new InvalidOperationException(
            $"edge-tts received no audio for article '{articleTitle}' from {feedHost} using all selected voices " +
            $"({string.Join(", ", triedVoices)}). Check the internet connection and try again.",
            lastFailure);
    }

    private static bool IsNoAudioReceived(InvalidOperationException exception) =>
        exception.Message.Contains("NoAudioReceived", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("no audio was received", StringComparison.OrdinalIgnoreCase);

    private async Task RunEdgeTtsAsync(
        string edgeTtsPath,
        string voice,
        string text,
        string outputPath,
        CancellationToken cancellationToken)
    {
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
        startInfo.ArgumentList.Add("--text");
        startInfo.ArgumentList.Add(text);
        startInfo.ArgumentList.Add("--write-media");
        startInfo.ArgumentList.Add(outputPath);

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
            var description = item.SelectSingleNode("./*[local-name()='encoded']")?.InnerText ??
                              item.SelectSingleNode("./*[local-name()='content']")?.InnerText ??
                              item.SelectSingleNode("./*[local-name()='description']")?.InnerText ??
                              item.SelectSingleNode("./*[local-name()='summary']")?.InnerText ??
                              string.Empty;
            result.Add(new NewsArticle(
                CleanNewsText(title),
                CleanNewsText(description),
                GetArticleUrl(item),
                GetImageUrl(item),
                sourceLogoUrl,
                GetPublishedAt(item)));
        }

        return result;
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
        var decoded = WebUtility.HtmlDecode(value);
        var plainText = Regex.Replace(decoded, "<[^>]*>", " ");
        return Regex.Replace(plainText, @"\s+", " ").Trim();
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

    private static string ResolvePath(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    private sealed record NewsArticle(
        string Title,
        string Description,
        string ArticleUrl,
        string ImageUrl,
        string SourceLogoUrl,
        DateTimeOffset? PublishedAt);
}
