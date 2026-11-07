using System.Media;
using System.Windows;

namespace AnchorFeeds.Services;

public static class PageTurnSound
{
    private const string ResourcePath = "/AnchorFeeds;component/Assets/page-flip.wav";
    private static readonly Lazy<byte[]> WaveData = new(LoadWaveData);
    private static readonly Lazy<SoundPlayer> Player = new(CreatePlayer);

    public static void Play() => Player.Value.Play();

    public static void Stop()
    {
        if (Player.IsValueCreated)
        {
            Player.Value.Stop();
        }
    }

    public static SoundPlayer CreatePlayer()
    {
        var stream = new MemoryStream(WaveData.Value, writable: false);
        var player = new SoundPlayer(stream);
        player.Load();
        return player;
    }

    private static byte[] LoadWaveData()
    {
        var resource = Application.GetResourceStream(new Uri(ResourcePath, UriKind.Relative))
            ?? throw new InvalidOperationException($"The page-turn sound resource was not found: {ResourcePath}");
        using (resource.Stream)
        using (var stream = new MemoryStream())
        {
            resource.Stream.CopyTo(stream);
            return stream.ToArray();
        }
    }
}
