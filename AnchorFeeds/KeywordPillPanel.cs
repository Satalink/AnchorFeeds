using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AnchorFeeds.Services;

namespace AnchorFeeds;

public enum KeywordPillKind
{
    FavoriteKeyword,
    StarKeyword,
    FavoriteCategory
}

public sealed record KeywordPill(string Phrase, KeywordPillKind Kind);

public sealed class KeywordPillEventArgs(KeywordPill pill) : EventArgs
{
    public KeywordPill Pill { get; } = pill;
}

public sealed class KeywordPillPanel : Panel
{
    public static readonly DependencyProperty PillsProperty = DependencyProperty.Register(
        nameof(Pills), typeof(IReadOnlyList<KeywordPill>), typeof(KeywordPillPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, PillsChanged));

    private readonly Button _overflow;

    public KeywordPillPanel()
    {
        ClipToBounds = true;
        _overflow = CreateButton("...", KeywordPillKind.StarKeyword);
        _overflow.SetResourceReference(BackgroundProperty, "ElevatedBackgroundBrush");
        _overflow.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        _overflow.ToolTip = LocalizationService.Get("Edit matching keywords");
        _overflow.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        _overflow.ContextMenu = CreateMenu();
    }

    public IReadOnlyList<KeywordPill>? Pills
    {
        get => (IReadOnlyList<KeywordPill>?)GetValue(PillsProperty);
        set => SetValue(PillsProperty, value);
    }

    public event EventHandler? HideRequested;
    public event EventHandler? EditRequested;
    public event EventHandler<KeywordPillEventArgs>? DeleteRequested;

    private static void PillsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((KeywordPillPanel)sender).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (Pills is not { Count: > 0 } pills)
        {
            return;
        }

        foreach (var pill in pills)
        {
            var button = CreateButton(pill.Phrase, pill.Kind);
            button.ToolTip = pill.Phrase;
            button.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
            var menu = CreateMenu();
            var delete = new MenuItem { Header = LocalizationService.Get("Delete") };
            delete.Click += (_, _) => DeleteRequested?.Invoke(this, new KeywordPillEventArgs(pill));
            menu.Items.Add(delete);
            button.ContextMenu = menu;
            Children.Add(button);
        }

        Children.Add(_overflow);
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        var hide = new MenuItem { Header = LocalizationService.Get("Hide") };
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        var edit = new MenuItem { Header = LocalizationService.Get("Edit") };
        edit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(hide);
        menu.Items.Add(edit);
        return menu;
    }

    private static Button CreateButton(string label, KeywordPillKind kind)
    {
        var button = new Button
        {
            Content = label,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kind switch
            {
                KeywordPillKind.FavoriteKeyword => "#006400",
                KeywordPillKind.StarKeyword => "#B8860B",
                KeywordPillKind.FavoriteCategory => "#003B80",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            })),
            Foreground = kind == KeywordPillKind.StarKeyword ? Brushes.Black : Brushes.White,
            Margin = new Thickness(3, 0, 0, 0),
            ToolTip = label
        };
        button.SetResourceReference(StyleProperty, "KeywordPillButtonStyle");
        return button;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var count = InternalChildren.Count - 1;
        var totalWidth = InternalChildren.Cast<UIElement>().Take(count).Sum(child => child.DesiredSize.Width);
        var overflow = totalWidth > finalSize.Width;
        var remaining = Math.Max(0, finalSize.Width - (overflow ? _overflow.DesiredSize.Width : 0));
        var visibleCount = 0;
        double visibleWidth = 0;
        while (visibleCount < count && visibleWidth + InternalChildren[visibleCount].DesiredSize.Width <= remaining)
        {
            visibleWidth += InternalChildren[visibleCount].DesiredSize.Width;
            visibleCount++;
        }

        var x = Math.Max(0, finalSize.Width - visibleWidth - (overflow ? _overflow.DesiredSize.Width : 0));
        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            child.Visibility = i < visibleCount ? Visibility.Visible : Visibility.Hidden;
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            if (i < visibleCount)
            {
                x += width;
            }
        }

        _overflow.Visibility = overflow ? Visibility.Visible : Visibility.Hidden;
        _overflow.Arrange(new Rect(x, 0, Math.Min(_overflow.DesiredSize.Width, finalSize.Width), finalSize.Height));
        return finalSize;
    }
}
