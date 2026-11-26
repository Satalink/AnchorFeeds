using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AnchorFeeds.Services;

public sealed record AppUpdateInfo(Version Version, string Tag, Uri MsiUri);

public static class AppUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Satalink/AnchorFeeds/releases/latest";
    private const string InstallerAssetName = "AnchorFeeds-Setup.msi";

    private static readonly HttpClient HttpClient = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ParseVersion(text) ?? new Version(0, 0, 0);
        }
    }

    public static async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;

        var tag = root.GetProperty("tag_name").GetString();
        var latest = ParseVersion(tag);
        if (latest is null || latest <= CurrentVersion)
        {
            return null;
        }

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), InstallerAssetName, StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri))
            {
                return new AppUpdateInfo(latest, tag!, uri);
            }
        }

        return null;
    }

    public static async Task<string> DownloadInstallerAsync(AppUpdateInfo update, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AnchorFeeds-update");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"AnchorFeeds-Setup-{update.Version}.msi");

        using var response = await HttpClient.GetAsync(update.MsiUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(path);
        await source.CopyToAsync(target, cancellationToken);
        return path;
    }

    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().TrimStart('v', 'V');
        var end = trimmed.IndexOfAny(['+', '-']);
        if (end >= 0)
        {
            trimmed = trimmed[..end];
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AnchorFeeds", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
