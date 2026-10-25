using System.Collections.ObjectModel;

namespace AnchorFeeds.Models;

public sealed class VoiceLocaleGroup
{
    public required string Locale { get; init; }
    public required string DisplayName { get; init; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<VoiceOption> Voices { get; } = [];
}
