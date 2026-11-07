using System.Windows.Input;
using YamlDotNet.Serialization;

namespace AnchorFeeds.Models;

public readonly record struct HotkeyGesture(Key Key, ModifierKeys Modifiers);

public sealed class HotkeySettings
{
    [YamlMember(Alias = "play_pause")]
    public string PlayPause { get; set; } = "Space";

    [YamlMember(Alias = "skip_next")]
    public string SkipNext { get; set; } = "Down";

    [YamlMember(Alias = "previous_back")]
    public string PreviousBack { get; set; } = "Up";

    [YamlMember(Alias = "volume_up")]
    public string VolumeUp { get; set; } = "Shift+Up";

    [YamlMember(Alias = "volume_down")]
    public string VolumeDown { get; set; } = "Shift+Down";

    [YamlMember(Alias = "forward")]
    public string Forward { get; set; } = "Right";

    [YamlMember(Alias = "backward")]
    public string Backward { get; set; } = "Left";

    [YamlMember(Alias = "mute")]
    public string Mute { get; set; } = "M";

    [YamlMember(Alias = "full_mode")]
    public string FullMode { get; set; } = "F";

    [YamlMember(Alias = "scan_mode")]
    public string ScanMode { get; set; } = "S";

    [YamlMember(Alias = "refresh_library")]
    public string RefreshLibrary { get; set; } = "Ctrl+F5";

    [YamlMember(Alias = "refresh_article")]
    public string RefreshArticle { get; set; } = "F5";

    [YamlMember(Alias = "mark_all_as_read")]
    public string MarkAllAsRead { get; set; } = "Ctrl+Shift+A";

    [YamlMember(Alias = "settings")]
    public string Settings { get; set; } = "O";

    public IEnumerable<(string Action, string Shortcut)> GetShortcuts()
    {
        yield return (nameof(PlayPause), PlayPause);
        yield return (nameof(SkipNext), SkipNext);
        yield return (nameof(PreviousBack), PreviousBack);
        yield return (nameof(VolumeUp), VolumeUp);
        yield return (nameof(VolumeDown), VolumeDown);
        yield return (nameof(Forward), Forward);
        yield return (nameof(Backward), Backward);
        yield return (nameof(Mute), Mute);
        yield return (nameof(FullMode), FullMode);
        yield return (nameof(ScanMode), ScanMode);
        yield return (nameof(RefreshLibrary), RefreshLibrary);
        yield return (nameof(RefreshArticle), RefreshArticle);
        yield return (nameof(MarkAllAsRead), MarkAllAsRead);
        yield return (nameof(Settings), Settings);
    }

    // Settings saved before Forward/Backward existed still hold the old arrow defaults, which would now conflict.
    public void MigrateLegacyDefaults()
    {
        if (SkipNext == "Right" && PreviousBack == "Left" && VolumeUp == "Up" && VolumeDown == "Down")
        {
            SkipNext = "Down";
            PreviousBack = "Up";
            VolumeUp = "Shift+Up";
            VolumeDown = "Shift+Down";
        }
    }

    public void Validate()
    {
        var assigned = new Dictionary<(Key Key, ModifierKeys Modifiers), string>();
        foreach (var (action, shortcut) in GetShortcuts())
        {
            if (shortcut is null)
            {
                throw new InvalidDataException($"The hotkey for {action} cannot be null. Use an empty value to disable it.");
            }

            if (string.IsNullOrWhiteSpace(shortcut))
            {
                continue;
            }

            if (!TryParse(shortcut, out var gesture))
            {
                throw new InvalidDataException($"The hotkey for {action} is invalid: '{shortcut}'. Use a key with optional Ctrl, Shift, or Alt modifiers.");
            }

            var chord = (gesture.Key, gesture.Modifiers);
            if (assigned.TryGetValue(chord, out var existingAction))
            {
                throw new InvalidDataException($"The hotkey '{shortcut}' is assigned to both {existingAction} and {action}.");
            }

            assigned.Add(chord, action);
        }
    }

    public static bool TryParse(string shortcut, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var token in tokens)
        {
            if (token.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("control", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & ModifierKeys.Control) != 0)
                {
                    return false;
                }

                modifiers |= ModifierKeys.Control;
            }
            else if (token.Equals("shift", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & ModifierKeys.Shift) != 0)
                {
                    return false;
                }

                modifiers |= ModifierKeys.Shift;
            }
            else if (token.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & ModifierKeys.Alt) != 0)
                {
                    return false;
                }

                modifiers |= ModifierKeys.Alt;
            }
            else if (key is null &&
                     Enum.TryParse<Key>(token, ignoreCase: true, out var parsedKey) &&
                     Enum.IsDefined(typeof(Key), parsedKey) &&
                     parsedKey is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed) &&
                     !IsModifierKey(parsedKey))
            {
                key = parsedKey;
            }
            else
            {
                return false;
            }
        }

        if (key is null)
        {
            return false;
        }

        gesture = new HotkeyGesture(key.Value, modifiers);
        return true;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
