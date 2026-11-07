using System.Text.Json;

namespace AnchorFeeds.Services;

public sealed class WindowPlacementService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AncoredNewsFeeds",
        "window-placement.json");

    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));
            if (placement is null ||
                !double.IsFinite(placement.Left) || !double.IsFinite(placement.Top) ||
                !double.IsFinite(placement.Width) || !double.IsFinite(placement.Height) ||
                placement.Width <= 0 || placement.Height <= 0)
            {
                return null;
            }

            return placement;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing window placement is not worth interrupting shutdown.
        }
    }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized);
