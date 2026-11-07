namespace AnchorFeeds.Services;

public static class AudioMuteService
{
    public static bool IsMuted { get; private set; }

    public static event Action<bool>? MuteChanged;

    public static void SetMuted(bool isMuted)
    {
        if (IsMuted == isMuted)
        {
            return;
        }

        IsMuted = isMuted;
        MuteChanged?.Invoke(isMuted);
    }
}
