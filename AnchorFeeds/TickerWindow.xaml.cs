using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnchorFeeds.Models;

namespace AnchorFeeds;

public partial class TickerWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double ItemGap = 48;
    private const string CheckIconData = "M 3,12 L 9,18 L 21,6";
    private const string TrashIconData =
        "M 4,6 H 20 M 9,6 V 3 H 15 V 6 M 6,6 L 7,21 H 17 L 18,6 M 10,10 V 17 M 14,10 V 17";
    private const string PlayIconData = "M 7,4 L 19,12 L 7,20 Z";
    private const string PauseIconData = "M 8,5 V 19 M 16,5 V 19";
    private const string CloseIconData = "M 5,5 L 19,19 M 19,5 L 5,19";
    private const string UnmutedIconData =
        "M 3,9 H 7 L 12,5 V 19 L 7,15 H 3 Z M 15,9 Q 17,12 15,15 M 18,6 Q 22,12 18,18";
    private const string MutedIconData = "M 3,9 H 7 L 12,5 V 19 L 7,15 H 3 Z M 16,9 L 22,15 M 22,9 L 16,15";
    private const int VisibleArticles = 4;
    private const double HeightStep = 8;
    private const double DragThreshold = 5;

    private bool _dragPending;
    private bool _movePending;
    private bool _moving;
    private double _moveStartY;
    private double _moveStartTop;
    private bool _dragging;
    private bool _dragResumed;
    private double _dragStartX;
    private double _dragStartOffset;
    private TimeSpan _resumeAt;

    private static readonly double[] SpeedPresets = [25, 40, 60, 100, 160];

    private readonly Stopwatch _clock = new();
    private readonly MediaPlayer _titlePlayer = new();
    private readonly MediaPlayer _bodyPlayer = new();
    private readonly Queue<ArticleAudio> _titleQueue = new();
    private readonly HashSet<ArticleAudio> _announced = [];
    private readonly Dictionary<ArticleAudio, Border> _itemPlayButtons = [];
    private readonly Dictionary<ArticleAudio, Border> _itemHighlights = [];
    private readonly Dictionary<ArticleAudio, Border> _itemDimmers = [];
    private IReadOnlyList<ArticleAudio>? _pendingArticles;
    private bool _markReadOnTitle;
    private string _clickAction = TickerSettings.ClickPlayBodyAudio;
    private string _rightClickAction = TickerSettings.ClickPlayBodyAudio;
    private ArticleAudio? _titleArticle;
    private ArticleAudio? _bodyArticle;
    private Border? _playPauseButton;
    private Border? _muteButton;
    private bool _paused;
    private bool _muted = true;
    private bool _restoreMuted;
    private bool _titlePlaying;
    private int? _speedLevelOverride;
    private int _speedLevel;
    private double _itemActionSize = 24;
    private TimeSpan _lastTick;
    private double _offset;
    private double _speed = 60;
    private bool _hovering;
    private bool _rendering;
    private double _thumbnailSize = 36;
    private double _itemWidth = 400;
    private bool _atTop;
    private bool _resizing;
    private double _resizeStartY;
    private double _resizeStartHeight;
    private double _resizeEdge;
    private IReadOnlyList<ArticleAudio> _articles = [];

    public TickerWindow()
    {
        InitializeComponent();
        ResizeGrip.MouseLeftButtonDown += ResizeGrip_MouseLeftButtonDown;
        ResizeGrip.MouseMove += ResizeGrip_MouseMove;
        ResizeGrip.MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
        ResizeGrip.LostMouseCapture += (_, _) => _resizing = false;
        SourceInitialized += (_, _) => ApplyToolWindowStyle();
        IsVisibleChanged += (_, _) =>
        {
            SetRendering(IsVisible);
            if (!IsVisible)
            {
                StopAllAudio();
            }
        };
        _titlePlayer.MediaEnded += (_, _) => OnTitleFinished(completed: true);
        _titlePlayer.MediaFailed += (_, _) => OnTitleFinished(completed: false);
        _bodyPlayer.MediaEnded += (_, _) => StopBody();
        _bodyPlayer.MediaFailed += (_, _) => StopBody();
        TickerCanvas.MouseEnter += (_, _) => _hovering = true;
        TickerCanvas.MouseLeave += (_, _) =>
        {
            _hovering = false;
            _dragResumed = false;
        };
        TickerCanvas.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
        ControlsBox.PreviewMouseLeftButtonDown += ControlsBox_PreviewMouseLeftButtonDown;
        ControlsBox.PreviewMouseMove += ControlsBox_PreviewMouseMove;
        ControlsBox.PreviewMouseLeftButtonUp += ControlsBox_PreviewMouseLeftButtonUp;
        ControlsBox.LostMouseCapture += (_, _) => _moving = false;
        TickerCanvas.PreviewMouseMove += Canvas_PreviewMouseMove;
        TickerCanvas.PreviewMouseLeftButtonUp += Canvas_PreviewMouseLeftButtonUp;
        TickerCanvas.LostMouseCapture += (_, _) => _dragging = false;
    }

    public event Action? CloseRequested;

    public event Action<ArticleAudio, string>? ArticleClicked;

    public event Action<ArticleAudio, bool>? ArticleDismissed;

    public event Action<ArticleAudio>? TitleRead;

    public event Action<ArticleAudio>? AudioErrorClicked;

    public event Action<string>? PositionChanged;

    public event Action<double>? SpeedChanged;

    public event Action<double>? HeightAdjusted;

    private double ScreenY(MouseEventArgs e) =>
        ResizeGrip.PointToScreen(e.GetPosition(ResizeGrip)).Y /
        (VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _resizeStartY = ScreenY(e);
        _resizeStartHeight = Height;
        _resizeEdge = _atTop ? Top : Top + Height;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        // Dragging away from the docked edge grows the ticker; snap to fixed steps.
        var delta = ScreenY(e) - _resizeStartY;
        var grow = _atTop ? delta : -delta;
        var target = _resizeStartHeight + Math.Round(grow / HeightStep) * HeightStep;
        target = Math.Clamp(target, TickerSettings.MinHeight, TickerSettings.MaxHeight);
        if (Math.Abs(target - Height) < 0.5)
        {
            return;
        }

        Height = target;
        Top = _atTop ? _resizeEdge : _resizeEdge - target;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        ResizeGrip.ReleaseMouseCapture();
        e.Handled = true;
        UpdateSizing();
        BuildControls();
        RebuildItems(_articles);
        HeightAdjusted?.Invoke(Height);
    }

    private void UpdateSizing()
    {
        _thumbnailSize = Math.Round(Height);
        _itemActionSize = Math.Clamp(Math.Floor(Height / 3), 16, 24);
        ControlsBox.Width = Height;
    }

    private void ApplyDockVisuals()
    {
        ResizeGrip.VerticalAlignment = _atTop ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        ResizeGrip.BorderThickness = _atTop ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
    }

    private double ControlsScreenY(MouseEventArgs e) =>
        ControlsBox.PointToScreen(e.GetPosition(ControlsBox)).Y /
        VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private void ControlsBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _movePending = true;
        _moveStartY = ControlsScreenY(e);
        _moveStartTop = Top;
    }

    private void ControlsBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_movePending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var dy = ControlsScreenY(e) - _moveStartY;
        if (!_moving)
        {
            if (Math.Abs(dy) < DragThreshold)
            {
                return;
            }

            _moving = true;
            ControlsBox.CaptureMouse();
        }

        var area = SystemParameters.WorkArea;
        Top = Math.Clamp(_moveStartTop + dy, area.Top, area.Bottom - Height);
    }

    private void ControlsBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _movePending = false;
        if (!_moving)
        {
            return;
        }

        _moving = false;
        ControlsBox.ReleaseMouseCapture();
        e.Handled = true;

        // Snap to whichever desktop edge is closer.
        var area = SystemParameters.WorkArea;
        var atTop = Top + Height / 2 < area.Top + area.Height / 2;
        Top = atTop ? area.Top : area.Bottom - Height;
        if (atTop != _atTop)
        {
            _atTop = atTop;
            ApplyDockVisuals();
            PositionChanged?.Invoke(atTop ? TickerSettings.TopPosition : TickerSettings.BottomPosition);
        }
    }

    public void Configure(TickerSettings settings)
    {
        _atTop = settings.Position == TickerSettings.TopPosition;
        ApplyDockVisuals();
        _speed = settings.ScrollSpeed;
        _markReadOnTitle = settings.MarkReadWhenTitleRead;
        _clickAction = settings.ClickAction;
        _rightClickAction = settings.RightClickAction;
        _speedLevel = 0;
        for (var i = 1; i < SpeedPresets.Length; i++)
        {
            if (Math.Abs(SpeedPresets[i] - _speed) < Math.Abs(SpeedPresets[_speedLevel] - _speed))
            {
                _speedLevel = i;
            }
        }

        if (_speedLevelOverride is { } level)
        {
            _speedLevel = level;
            _speed = SpeedPresets[level];
        }

        FontFamily = new FontFamily(settings.FontFamily);
        FontSize = settings.FontSize;
        Height = Math.Ceiling(settings.Height);
        UpdateSizing();

        var area = SystemParameters.WorkArea;
        Left = area.Left;
        Width = area.Width;
        Top = settings.Position == TickerSettings.TopPosition ? area.Top : area.Bottom - Height;
        _itemWidth = Math.Max(240, area.Width / VisibleArticles - ItemGap);
        BuildControls();
    }

    private double ViewWidth => TickerCanvas.ActualWidth > 0 ? TickerCanvas.ActualWidth : Math.Max(0, Width - Height);

    public void SetArticles(IReadOnlyList<ArticleAudio> articles)
    {
        // A running rotation is left untouched; changes apply when it wraps around.
        if (_articles.Count == 0 || ItemsPanel.Children.Count == 0)
        {
            RebuildItems(articles);
            return;
        }

        _pendingArticles = articles;
    }

    private void RebuildItems(IReadOnlyList<ArticleAudio> articles)
    {
        _pendingArticles = null;
        if (_bodyArticle is not null && !articles.Contains(_bodyArticle))
        {
            StopBody();
        }

        foreach (var article in _articles)
        {
            article.PropertyChanged -= Article_PropertyChanged;
        }

        _articles = articles;
        ItemsPanel.Children.Clear();
        _itemPlayButtons.Clear();
        _itemHighlights.Clear();
        _itemDimmers.Clear();
        foreach (var article in articles)
        {
            article.PropertyChanged += Article_PropertyChanged;
            ItemsPanel.Children.Add(CreateItem(article));
        }

        ItemsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (_offset == 0)
        {
            _offset = ViewWidth;
        }
    }

    private void Article_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ArticleAudio article ||
            (e.PropertyName is not null && e.PropertyName != nameof(ArticleAudio.IsPlayed)))
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (_itemDimmers.TryGetValue(article, out var dimmer))
            {
                dimmer.Visibility = article.IsPlayed ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private void BuildControls()
    {
        var size = Math.Max(16, Math.Floor((Height - 10) / 2));
        _playPauseButton = CreateActionButton(_paused ? PlayIconData : PauseIconData, TogglePaused, size);
        _muteButton = CreateActionButton(_muted ? MutedIconData : UnmutedIconData, ToggleMuted, size);
        var close = CreateActionButton(CloseIconData, () => CloseRequested?.Invoke(), size);

        ControlsPanel.Children.Clear();
        ControlsPanel.Children.Add(_playPauseButton);
        ControlsPanel.Children.Add(close);
        ControlsPanel.Children.Add(CreateSpeedButton(size));
        ControlsPanel.Children.Add(_muteButton);
        ControlsPanel.ContextMenuOpening -= SuppressContextMenu;
        ControlsPanel.ContextMenuOpening += SuppressContextMenu;
    }

    private static void SuppressContextMenu(object sender, ContextMenuEventArgs e) => e.Handled = true;

    // Single button showing the speed level (1-5); each click advances and wraps.
    private Border CreateSpeedButton(double size)
    {
        var label = new TextBlock
        {
            Text = (_speedLevel + 1).ToString(),
            FontSize = Math.Max(10, size * 0.5),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Silver,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = label
        };
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            label.Foreground = Brushes.White;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            label.Foreground = Brushes.Silver;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var level = (_speedLevel + 1) % SpeedPresets.Length;
            _speedLevelOverride = level;
            _speedLevel = level;
            _speed = SpeedPresets[level];
            label.Text = (level + 1).ToString();
            SpeedChanged?.Invoke(_speed);
        };
        button.MouseRightButtonUp += (_, e) => e.Handled = true;
        return button;
    }

    private void TogglePaused()
    {
        _paused = !_paused;
        SetIcon(_playPauseButton, _paused ? PlayIconData : PauseIconData);
    }

    private void ToggleMuted()
    {
        _muted = !_muted;
        SetIcon(_muteButton, _muted ? MutedIconData : UnmutedIconData);
        if (_muted)
        {
            StopAllAudio();
        }
        else
        {
            ScanVisibleItems(announce: false);
        }
    }

    private static void SetIcon(Border? button, string iconData)
    {
        if (button?.Child is System.Windows.Shapes.Path path)
        {
            path.Data = Geometry.Parse(iconData);
        }
    }

    private void ToggleBody(ArticleAudio article)
    {
        if (_bodyArticle == article)
        {
            StopBody();
            return;
        }

        if (article.HasAudioError)
        {
            AudioErrorClicked?.Invoke(article);
            return;
        }

        if (!article.HasUsableBodyAudio)
        {
            return;
        }

        StopTitleAudio();
        try
        {
            _bodyPlayer.Open(new Uri(article.FilePath, UriKind.Absolute));
            _bodyPlayer.Play();
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            return;
        }

        if (_muted)
        {
            _restoreMuted = true;
            _muted = false;
            SetIcon(_muteButton, UnmutedIconData);
        }

        _bodyArticle = article;
        UpdateItemPlayButtons();
    }

    private void StopBody()
    {
        if (_bodyArticle is null)
        {
            return;
        }

        _bodyPlayer.Stop();
        _bodyPlayer.Close();
        _bodyArticle = null;
        if (_restoreMuted)
        {
            _restoreMuted = false;
            _muted = true;
            SetIcon(_muteButton, MutedIconData);
        }

        UpdateItemPlayButtons();
        PlayNextTitle();
    }

    private void StopTitleAudio()
    {
        _titleQueue.Clear();
        _titlePlayer.Stop();
        _titlePlayer.Close();
        _titlePlaying = false;
        SetTitleArticle(null);
    }

    private void StopAllAudio()
    {
        StopBody();
        StopTitleAudio();
    }

    private void UpdateItemPlayButtons()
    {
        foreach (var (article, button) in _itemPlayButtons)
        {
            if (article.HasAudioError)
            {
                continue;
            }

            SetIcon(button, article == _bodyArticle ? PauseIconData : PlayIconData);
        }
    }

    private void OnTitleFinished(bool completed)
    {
        var finished = _titleArticle;
        _titlePlayer.Close();
        _titlePlaying = false;
        if (completed && _markReadOnTitle && finished is { IsPlayed: false })
        {
            TitleRead?.Invoke(finished);
        }

        PlayNextTitle();
    }

    private void PlayNextTitle()
    {
        if (_muted || _titlePlaying || _bodyArticle is not null)
        {
            return;
        }

        while (_titleQueue.TryDequeue(out var article))
        {
            if (string.IsNullOrEmpty(article.TitleAudioPath) || !File.Exists(article.TitleAudioPath))
            {
                continue;
            }

            try
            {
                _titlePlayer.Open(new Uri(article.TitleAudioPath, UriKind.Absolute));
                _titlePlayer.Play();
                _titlePlaying = true;
                SetTitleArticle(article);
                return;
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
            {
            }
        }
    }

    // An item counts as fully visible once it has entered completely from the right edge of the view.
    private void ScanVisibleItems(bool announce)
    {
        var viewWidth = ViewWidth;
        var newlyVisible = false;
        for (var i = 0; i < _articles.Count; i++)
        {
            var left = _offset + i * (_itemWidth + ItemGap);
            if (left + _itemWidth > viewWidth)
            {
                break;
            }

            if (left >= 0 && _announced.Add(_articles[i]))
            {
                newlyVisible = true;
                if (announce)
                {
                    _titleQueue.Enqueue(_articles[i]);
                }
            }
        }

        if (announce)
        {
            PlayNextTitle();
        }

        // The highlight stays on the last-read item until a newer item is fully shown.
        if (newlyVisible && !_titlePlaying)
        {
            SetTitleArticle(null);
        }
    }

    private FrameworkElement CreateItem(ArticleAudio article)
    {
        var label = new TextBlock
        {
            Text = article.IsFavoriteCategory ? $"★ {article.Title}" : article.Title,
            Foreground = article.IsFavoriteCategory ? Brushes.Gold : Brushes.WhiteSmoke,
            FontWeight = article.IsFavoriteCategory ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = Height,
            VerticalAlignment = VerticalAlignment.Center
        };
        var item = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Width = _itemWidth,
            Background = Brushes.Transparent
        };
        if (CreateThumbnail(article) is { } thumbnail)
        {
            item.Children.Add(thumbnail);
            label.Width = Math.Max(60, _itemWidth - _thumbnailSize - 10 - _itemActionSize);
        }
        else
        {
            label.Width = _itemWidth - _itemActionSize;
        }

        item.Children.Add(label);
        var actions = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var play = CreateActionButton(
            article.HasAudioError ? CloseIconData : _bodyArticle == article ? PauseIconData : PlayIconData,
            () => ToggleBody(article),
            _itemActionSize);
        if (article.HasAudioError && play.Child is System.Windows.Shapes.Path errorIcon)
        {
            errorIcon.Stroke = Brushes.IndianRed;
            play.MouseEnter += (_, _) => errorIcon.Stroke = Brushes.IndianRed;
            play.MouseLeave += (_, _) => errorIcon.Stroke = Brushes.IndianRed;
        }

        _itemPlayButtons[article] = play;
        actions.Children.Add(play);
        actions.Children.Add(CreateActionButton(CheckIconData, () => Dismiss(article, false), _itemActionSize));
        actions.Children.Add(CreateActionButton(TrashIconData, () => Dismiss(article, true), _itemActionSize));
        actions.ContextMenuOpening += (_, e) => e.Handled = true;

        var container = new Grid
        {
            Width = _itemWidth,
            Height = Height,
            Margin = new Thickness(0, 0, ItemGap, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        container.Children.Add(CreateHighlight(article));
        container.Children.Add(item);
        var dimmer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(74, 0, 0, 0)),
            IsHitTestVisible = false,
            Visibility = article.IsPlayed ? Visibility.Visible : Visibility.Collapsed
        };
        _itemDimmers[article] = dimmer;
        container.Children.Add(dimmer);
        container.Children.Add(actions);
        container.MouseEnter += (_, _) => label.TextDecorations = TextDecorations.Underline;
        container.MouseLeave += (_, _) => label.TextDecorations = null;
        container.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            HandleArticleClick(article, _clickAction);
        };
        container.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            HandleArticleClick(article, _rightClickAction);
        };
        return container;
    }

    private void HandleArticleClick(ArticleAudio article, string action)
    {
        if (action == TickerSettings.ClickPlayBodyAudioInTicker)
        {
            ToggleBody(article);
        }
        else
        {
            StopAllAudio();
            ArticleClicked?.Invoke(article, action);
        }
    }

    private Border CreateHighlight(ArticleAudio article)
    {
        var highlight = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false,
            Visibility = _titleArticle == article ? Visibility.Visible : Visibility.Collapsed,
            Background = new LinearGradientBrush(
                Color.FromArgb(70, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(230, 255, 255, 255), Color.FromArgb(230, 0, 0, 0), 45),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 2,
                BlurRadius = 6,
                Opacity = 0.7
            }
        };
        _itemHighlights[article] = highlight;
        return highlight;
    }

    private void SetTitleArticle(ArticleAudio? article)
    {
        _titleArticle = article;
        foreach (var (candidate, highlight) in _itemHighlights)
        {
            highlight.Visibility = candidate == article ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Canvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragPending = true;
        _dragStartX = e.GetPosition(TickerCanvas).X;
        _dragStartOffset = _offset;
    }

    private void Canvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var dx = e.GetPosition(TickerCanvas).X - _dragStartX;
        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            TickerCanvas.CaptureMouse();
        }

        var panelWidth = ItemsPanel.DesiredSize.Width;
        _offset = Math.Clamp(_dragStartOffset + dx, -Math.Max(0, panelWidth - ItemWidthMargin()), ViewWidth);
        Canvas.SetLeft(ItemsPanel, _offset);
        ScanVisibleItems(announce: !_muted);
    }

    private double ItemWidthMargin() => _itemWidth;

    private void Canvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPending = false;
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _dragResumed = true;
        _resumeAt = _clock.Elapsed + TimeSpan.FromSeconds(2);
        TickerCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Dismiss(ArticleAudio article, bool delete)
    {
        if (_bodyArticle == article)
        {
            StopBody();
        }

        ArticleDismissed?.Invoke(article, delete);
    }

    private static Border CreateActionButton(string iconData, Action onClick, double size)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(iconData),
            Stroke = Brushes.Silver,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = Math.Round(size * 0.58),
            Height = Math.Round(size * 0.58),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            Child = icon
        };
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            icon.Stroke = Brushes.White;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            icon.Stroke = Brushes.Silver;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        button.MouseRightButtonUp += (_, e) => e.Handled = true;
        return button;
    }

    private Border? CreateThumbnail(ArticleAudio article)
    {
        if (article.ThumbnailUri is not { } uri)
        {
            return null;
        }

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Width = _thumbnailSize,
            Height = _thumbnailSize
        };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.DecodePixelHeight = (int)Math.Ceiling(_thumbnailSize * 2);
            bitmap.CacheOption = uri.IsFile ? BitmapCacheOption.OnLoad : BitmapCacheOption.Default;
            bitmap.EndInit();
            image.Source = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }

        return new Border
        {
            Width = _thumbnailSize,
            Height = _thumbnailSize,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = image
        };
    }

    private void SetRendering(bool enabled)
    {
        if (enabled == _rendering)
        {
            return;
        }

        _rendering = enabled;
        if (enabled)
        {
            _clock.Restart();
            _lastTick = TimeSpan.Zero;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }
        else
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
        }
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        var panelWidth = ItemsPanel.DesiredSize.Width;
        if (_dragging || now < _resumeAt || (_hovering && !_dragResumed) || _paused || _bodyArticle is not null || panelWidth <= 0)
        {
            return;
        }

        _offset -= _speed * Math.Min(delta, 0.1);
        if (_offset + panelWidth < 0)
        {
            _offset = ViewWidth;
            _announced.Clear();
            if (_pendingArticles is { } next)
            {
                RebuildItems(next);
            }
        }

        Canvas.SetLeft(ItemsPanel, _offset);
        Canvas.SetTop(ItemsPanel, 0);
        ScanVisibleItems(announce: !_muted);
    }

    private void ApplyToolWindowStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        SetRendering(false);
        StopAllAudio();
        base.OnClosed(e);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
