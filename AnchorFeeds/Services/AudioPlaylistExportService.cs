using System.Diagnostics;
using System.Globalization;
using System.Text;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class AudioPlaylistExportService
{
    private const int BellSampleRate = 44100;
    private const double BellDurationSeconds = 0.9;

    public async Task<PlaylistExportResult> ExportAsync(
        IReadOnlyList<ArticleAudio> articles,
        bool includeBodies,
        string outputPath,
        int bitrateKbps,
        IProgress<PlaylistExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("The selected MP3 path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);

        var playlist = new List<PlaylistEntry>();
        var exportedArticles = 0;
        var skippedArticles = 0;
        for (var articleIndex = 0; articleIndex < articles.Count; articleIndex++)
        {
            var article = articles[articleIndex];
            Report(progress, $"Preparing article {articleIndex + 1} of {articles.Count}: {article.Title}",
                articleIndex, articles.Count, GetPercent(articleIndex, articles.Count));
            var titlePath = ExistingPath(article.TitleAudioPath);
            var bodyPath = includeBodies ? ExistingPath(article.FilePath) : null;
            if (titlePath is null && bodyPath is null)
            {
                skippedArticles++;
                continue;
            }

            if (titlePath is not null)
            {
                if (article.IsFavoriteCategory)
                {
                    playlist.Add(new PlaylistEntry(string.Empty, article.Title, "attention bell", IsAttentionBell: true));
                }

                playlist.Add(new PlaylistEntry(titlePath, article.Title, "title", IsAttentionBell: false));
            }

            if (bodyPath is not null)
            {
                playlist.Add(new PlaylistEntry(bodyPath, article.Title, "body", IsAttentionBell: false));
            }

            exportedArticles++;
        }

        Report(progress, $"Prepared {articles.Count} of {articles.Count} articles. Starting audio export...",
            articles.Count, articles.Count, 100);
        if (playlist.Count == 0)
        {
            throw new InvalidOperationException(
                "No matching title or body audio files were found in the current Feeds Library view.");
        }

        if (playlist
            .Where(entry => !entry.IsAttentionBell)
            .Any(entry => Path.GetFullPath(entry.Path).Equals(fullOutputPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The export destination cannot overwrite an audio file in the playlist.");
        }

        var ffmpegPath = FindFfmpeg();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"anchorfeeds-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryOutputPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(fullOutputPath)}-{Guid.NewGuid():N}.tmp.mp3");

        try
        {
            var bellMp3Path = Path.Combine(temporaryDirectory, "attention-bell.mp3");
            if (playlist.Any(entry => entry.IsAttentionBell))
            {
                var bellWavePath = Path.Combine(temporaryDirectory, "attention-bell.wav");
                WriteAttentionBellWav(bellWavePath);
                var encodeBellArguments = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-i", bellWavePath,
                    "-codec:a", "libmp3lame",
                    "-ar", BellSampleRate.ToString(CultureInfo.InvariantCulture),
                    "-ac", "2",
                    "-f", "mp3"
                };
                AddBitrateArgument(encodeBellArguments, bitrateKbps);
                encodeBellArguments.Add(bellMp3Path);
                await RunFfmpegAsync(ffmpegPath, encodeBellArguments, cancellationToken);
            }

            var trackDurations = await TryGetTrackDurationsAsync(
                playlist,
                progress,
                cancellationToken);
            var playlistPath = Path.Combine(temporaryDirectory, "playlist.txt");
            var playlistLines = playlist.Select(entry =>
                $"file '{EscapeConcatPath(entry.IsAttentionBell ? bellMp3Path : entry.Path)}'");
            await File.WriteAllLinesAsync(
                playlistPath,
                playlistLines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var exportArguments = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-f", "concat",
                "-safe", "0",
                "-i", playlistPath,
                "-map", "0:a:0",
                "-vn",
                "-codec:a", "libmp3lame",
                "-ar", BellSampleRate.ToString(CultureInfo.InvariantCulture),
                "-ac", "2",
                "-f", "mp3"
            };
            AddBitrateArgument(exportArguments, bitrateKbps);
            exportArguments.Add("-progress");
            exportArguments.Add("pipe:1");
            exportArguments.Add("-nostats");
            exportArguments.Add(temporaryOutputPath);
            Report(progress, trackDurations is null
                    ? $"Combining {playlist.Count} audio items..."
                    : $"Encoding item 1 of {playlist.Count}: {FormatTrack(playlist[0])}",
                0, articles.Count);
            await RunFfmpegAsync(
                ffmpegPath,
                exportArguments,
                cancellationToken,
                line => ReportEncodingProgress(line, playlist, trackDurations, progress));

            if (!File.Exists(temporaryOutputPath) || new FileInfo(temporaryOutputPath).Length == 0)
            {
                throw new InvalidOperationException("FFmpeg completed without creating a playable MP3 file.");
            }

            File.Move(temporaryOutputPath, fullOutputPath, overwrite: true);
            return new PlaylistExportResult(exportedArticles, skippedArticles);
        }
        finally
        {
            if (File.Exists(temporaryOutputPath))
            {
                File.Delete(temporaryOutputPath);
            }

            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static string? ExistingPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Path.GetFullPath(path) : null;

    private static string EscapeConcatPath(string path) =>
        Path.GetFullPath(path)
            .Replace('\\', '/')
            .Replace("'", "'\\''", StringComparison.Ordinal);

    private static void AddBitrateArgument(ICollection<string> arguments, int bitrateKbps)
    {
        if (bitrateKbps > 0)
        {
            arguments.Add("-b:a");
            arguments.Add($"{bitrateKbps}k");
        }
    }

    private static async Task<double[]?> TryGetTrackDurationsAsync(
        IReadOnlyList<PlaylistEntry> playlist,
        IProgress<PlaylistExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var ffprobePath = FindFfprobe();
        if (ffprobePath is null)
        {
            Report(progress,
                "FFprobe is unavailable; audio encoding will continue with an indeterminate progress bar.",
                0, playlist.Count);
            return null;
        }

        var durations = new double[playlist.Count];
        var durationCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < playlist.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = playlist[index];
            Report(progress, $"Measuring item {index + 1} of {playlist.Count}: {FormatTrack(track)}",
                index, playlist.Count, GetPercent(index, playlist.Count));
            if (track.IsAttentionBell)
            {
                durations[index] = BellDurationSeconds;
                continue;
            }

            if (!durationCache.TryGetValue(track.Path, out var duration))
            {
                duration = await GetAudioDurationAsync(ffprobePath, track.Path, cancellationToken);
                durationCache.Add(track.Path, duration);
            }

            durations[index] = duration;
        }

        if (durations.Any(duration => !double.IsFinite(duration) || duration <= 0))
        {
            Report(progress,
                "Some audio durations are unavailable; audio encoding will continue with an indeterminate progress bar.",
                0, playlist.Count);
            return null;
        }

        return durations;
    }

    private static async Task<double> GetAudioDurationAsync(
        string ffprobePath,
        string audioPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(audioPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start FFprobe.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0 ||
            !double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) ||
            duration <= 0)
        {
            throw new InvalidDataException(
                $"FFprobe could not determine the duration of '{audioPath}': {error.Trim()}");
        }

        return duration;
    }

    private static async Task RunFfmpegAsync(
        string ffmpegPath,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? progressLine = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start FFmpeg.");
        }

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = ReadProgressOutputAsync(process, progressLine);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var error = await standardError;
        await standardOutput;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg failed to export the MP3 (exit code {process.ExitCode}): {error.Trim()}");
        }
    }

    private static async Task ReadProgressOutputAsync(Process process, Action<string>? progressLine)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            progressLine?.Invoke(line);
        }
    }

    private static void ReportEncodingProgress(
        string line,
        IReadOnlyList<PlaylistEntry> playlist,
        IReadOnlyList<double>? durations,
        IProgress<PlaylistExportProgress>? progress)
    {
        if (progress is null || !line.StartsWith("out_time_us=", StringComparison.Ordinal) ||
            !long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var elapsedMicroseconds))
        {
            return;
        }

        if (durations is null)
        {
            Report(progress, "Combining audio items with FFmpeg...", 0, playlist.Count);
            return;
        }

        var elapsedSeconds = Math.Max(0, elapsedMicroseconds / 1_000_000d);
        var totalSeconds = durations.Sum();
        var itemIndex = 0;
        var itemStart = 0d;
        while (itemIndex < durations.Count - 1 &&
               elapsedSeconds >= itemStart + durations[itemIndex])
        {
            itemStart += durations[itemIndex];
            itemIndex++;
        }

        var percent = Math.Clamp(elapsedSeconds / totalSeconds * 100, 0, 100);
        Report(progress,
            $"Encoding item {itemIndex + 1} of {playlist.Count}: {FormatTrack(playlist[itemIndex])} ({percent:0}%)",
            itemIndex + 1, playlist.Count, percent);
    }

    private static string FormatTrack(PlaylistEntry entry) =>
        $"\"{entry.ArticleTitle}\" ({entry.Segment})";

    private static void Report(
        IProgress<PlaylistExportProgress>? progress,
        string status,
        int completedItems,
        int totalItems,
        double? percentComplete = null) =>
        progress?.Report(new PlaylistExportProgress(status, completedItems, totalItems, percentComplete));

    private static double GetPercent(int completedItems, int totalItems) =>
        totalItems == 0 ? 100 : Math.Clamp(completedItems / (double)totalItems * 100, 0, 100);

    private static void WriteAttentionBellWav(string outputPath)
    {
        const short bitsPerSample = 16;
        const short channels = 1;
        var sampleCount = (int)(BellSampleRate * BellDurationSeconds);
        var dataLength = sampleCount * channels * (bitsPerSample / 8);

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(BellSampleRate);
        writer.Write(BellSampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (double)BellSampleRate;
            var envelope = Math.Exp(-4.5 * time);
            var tone =
                0.62 * Math.Sin(2 * Math.PI * 1046.5 * time) +
                0.34 * Math.Sin(2 * Math.PI * 1568 * time) +
                0.18 * Math.Sin(2 * Math.PI * 2093 * time);
            var sample = (short)(Math.Clamp(tone * envelope, -1, 1) * short.MaxValue * 0.75);
            writer.Write(sample);
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

    private static string? FindFfprobe()
    {
        var executable = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var pathCommand = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
        return pathCommand;
    }

    private sealed record PlaylistEntry(string Path, string ArticleTitle, string Segment, bool IsAttentionBell);
}

public sealed record PlaylistExportResult(int ExportedArticles, int SkippedArticles);

public sealed record PlaylistExportProgress(
    string Status,
    int CompletedItems,
    int TotalItems,
    double? PercentComplete);
