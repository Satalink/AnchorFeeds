using System.Collections.ObjectModel;

namespace AnchorFeeds;

/// <summary>Edits a list of words or phrases as removable pills. Enter, comma or | commits the typed text.</summary>
public partial class PhrasePillEditor : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<string> _phrases = [];

    public PhrasePillEditor()
    {
        InitializeComponent();
        PillsItems.ItemsSource = _phrases;
    }

    public IReadOnlyList<string> Phrases
    {
        get
        {
            CommitInput();
            return _phrases.ToList();
        }
        set
        {
            _phrases.Clear();
            foreach (var phrase in (value ?? [])
                         .Select(item => item?.Trim() ?? string.Empty)
                         .Where(item => item.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _phrases.Add(phrase);
            }
        }
    }

    private void CommitInput()
    {
        foreach (var part in InputBox.Text.Split('|', ',', '\n', '\r'))
        {
            var phrase = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (phrase.Length > 0 && !_phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase))
            {
                _phrases.Add(phrase);
            }
        }

        InputBox.Clear();
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.OemComma)
        {
            CommitInput();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Back && InputBox.Text.Length == 0 && _phrases.Count > 0)
        {
            _phrases.RemoveAt(_phrases.Count - 1);
            e.Handled = true;
        }
    }

    private void InputBox_LostFocus(object sender, System.Windows.RoutedEventArgs e) => CommitInput();

    private void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string phrase })
        {
            _phrases.Remove(phrase);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        InputBox.Focus();
}
