using System.Diagnostics;
using System.Text.RegularExpressions;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public static partial class EdgeTtsVoiceCatalog
{
    public static async Task<string> CreateSampleAsync(string voice, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"ancored-newsfeeds-voice-{Guid.NewGuid():N}.mp3");
        var startInfo = new ProcessStartInfo
        {
            FileName = FindExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--voice");
        startInfo.ArgumentList.Add(voice);
        startInfo.ArgumentList.Add("--text");
        startInfo.ArgumentList.Add($"Hello. This is a sample of the {voice} voice.");
        startInfo.ArgumentList.Add("--write-media");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start edge-tts to create a voice sample.");
        }

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var error = await standardError;
            _ = await standardOutput;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"edge-tts could not create a sample for '{voice}' (exit code {process.ExitCode}): {error.Trim()}");
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidOperationException($"edge-tts created no audio sample for '{voice}'.");
            }

            return outputPath;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            throw;
        }
    }

    public static async Task<IReadOnlyList<VoiceCatalogEntry>> GetAvailableVoicesAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--list-voices");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start edge-tts to load its voice catalog.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"edge-tts could not list voices (exit code {process.ExitCode}): {error.Trim()}");
        }

        var voices = VoiceRowRegex().Matches(output)
            .Select(match =>
            {
                var name = match.Groups["name"].Value;
                var localeMatch = VoiceLocaleRegex().Match(name);
                return localeMatch.Success
                    ? new VoiceCatalogEntry(name, localeMatch.Groups["locale"].Value, match.Groups["gender"].Value)
                    : null;
            })
            .Where(voice => voice is not null)
            .Select(voice => voice!)
            .DistinctBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(voice => voice.Locale, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (voices.Count == 0)
        {
            throw new InvalidDataException("edge-tts returned no voices in its voice catalog.");
        }

        return voices;
    }

    public static string FindExecutable()
    {
        var executable = OperatingSystem.IsWindows() ? "edge-tts.exe" : "edge-tts";
        var pathCommand = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
        if (pathCommand is not null)
        {
            return pathCommand;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var pythonRoot = Path.Combine(appData, "Python");
        if (Directory.Exists(pythonRoot))
        {
            var userInstall = Directory.EnumerateDirectories(pythonRoot, "Python*")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "Scripts", "edge-tts.exe"))
                .FirstOrDefault(File.Exists);
            if (userInstall is not null)
            {
                return userInstall;
            }
        }

        throw new FileNotFoundException("edge-tts was not found. Install it and ensure edge-tts.exe is on PATH.");
    }

    [GeneratedRegex(@"(?m)^\s*(?<name>[A-Za-z0-9-]*Neural)\s+(?<gender>Female|Male)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex VoiceRowRegex();

    [GeneratedRegex(@"^(?<locale>[A-Za-z]{2,3}-[A-Za-z]{2,3})-")]
    private static partial Regex VoiceLocaleRegex();
}
