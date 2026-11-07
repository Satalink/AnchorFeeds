using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace AnchorFeeds.Services;

public sealed class TrayIconService : IDisposable
{
    private static readonly Uri ApplicationIconUri = new("pack://application:,,,/Assets/AnchorFeeds.ico");
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly Action _open;
    private readonly Action _exit;
    private Icon? _currentIcon;
    private bool _disposed;

    public TrayIconService(Action open, Action exit)
    {
        _open = open;
        _exit = exit;

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add("Open AnchorFeeds", null, (_, _) => Dispatch(_open));
        _contextMenu.Items.Add("Exit", null, (_, _) => Dispatch(_exit));

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatch(_open);
            }
        };
        _notifyIcon.BalloonTipClicked += (_, _) => Dispatch(() => NotificationClicked?.Invoke());
        SetUnreadCount(0);
    }

    public event Action? NotificationClicked;

    public void ShowNotification(string title, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(8000);
    }

    public void SetUnreadCount(int unreadCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tooltip = unreadCount > 0
            ? $"AnchorFeeds - {unreadCount} unread articles"
            : "AnchorFeeds";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : $"{tooltip[..60]}...";

        var updatedIcon = CreateIcon(unreadCount > 0);
        _notifyIcon.Icon = updatedIcon;
        _currentIcon?.Dispose();
        _currentIcon = updatedIcon;
    }

    private static Icon CreateIcon(bool showUnreadBadge)
    {
        var resource = WpfApplication.GetResourceStream(ApplicationIconUri)
            ?? throw new FileNotFoundException("The AnchorFeeds tray icon resource is unavailable.");
        using var stream = resource.Stream;
        using var sourceIcon = new Icon(stream);
        if (!showUnreadBadge)
        {
            return (Icon)sourceIcon.Clone();
        }

        using var bitmap = sourceIcon.ToBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        using (var unreadBrush = new SolidBrush(Color.FromArgb(255, 57, 255, 20)))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var diameter = Math.Max(6, bitmap.Width / 3);
            var x = bitmap.Width - diameter - 1;
            var y = bitmap.Height - diameter - 1;
            graphics.FillEllipse(Brushes.White, x - 1, y - 1, diameter + 2, diameter + 2);
            graphics.FillEllipse(unreadBrush, x, y, diameter, diameter);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static void Dispatch(Action action)
    {
        WpfApplication.Current?.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
