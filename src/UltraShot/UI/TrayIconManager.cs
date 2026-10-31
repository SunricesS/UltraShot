using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using UltraShot.Core;

namespace UltraShot.UI;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _captureMenuItem;
    private readonly ToolStripMenuItem _openFolderMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly ToolStripMenuItem _temporaryModeMenuItem;
    private readonly ToolStripMenuItem _recentMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;

    public event Action? CaptureTriggered;
    public event Action? RecentScreenshotsTriggered;
    public event Action? OpenFolderTriggered;
    public event Action? SettingsTriggered;
    public event Action<bool>? TemporaryModeToggled;
    public event Action? ExitTriggered;
    public event Action? NotificationClicked;
    public event Action? NotificationClosed;

    public TrayIconManager()
    {
        var contextMenu = new ContextMenuStrip();

        _captureMenuItem = new ToolStripMenuItem("📷 Take Screenshot", null, (s, e) => CaptureTriggered?.Invoke());
        _recentMenuItem = new ToolStripMenuItem("🖼️ Recent Screenshots", null, (s, e) => RecentScreenshotsTriggered?.Invoke());
        _openFolderMenuItem = new ToolStripMenuItem("📁 Open Screenshots Folder", null, (s, e) => OpenFolderTriggered?.Invoke());
        
        _temporaryModeMenuItem = new ToolStripMenuItem("📋 Save to Clipboard Only (Temporary)", null, (s, e) =>
        {
            if (s is ToolStripMenuItem mi)
            {
                SettingsManager.Current.SaveOnlyToClipboard = mi.Checked;
                SettingsManager.Save();
                TemporaryModeToggled?.Invoke(mi.Checked);
            }
        });
        _temporaryModeMenuItem.CheckOnClick = true;

        _settingsMenuItem = new ToolStripMenuItem("⚙️ Settings & Hotkey", null, (s, e) => SettingsTriggered?.Invoke());
        _exitMenuItem = new ToolStripMenuItem("❌ Exit", null, (s, e) => ExitTriggered?.Invoke());

        contextMenu.Items.Add(_captureMenuItem);
        contextMenu.Items.Add(_recentMenuItem);
        contextMenu.Items.Add(_openFolderMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_temporaryModeMenuItem);
        contextMenu.Items.Add(_settingsMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateAppIcon(),
            Visible = true,
            ContextMenuStrip = contextMenu,
            Text = "UltraShot"
        };

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RecentScreenshotsTriggered?.Invoke();
            }
        };

        _notifyIcon.BalloonTipClicked += (s, e) => NotificationClicked?.Invoke();
        _notifyIcon.BalloonTipClosed += (s, e) => NotificationClosed?.Invoke();

        UpdateDisplayStrings();
    }

    public void UpdateDisplayStrings()
    {
        var hotkeyStr = SettingsManager.Current.GetHotkeyDisplayString();
        _captureMenuItem.Text = $"📷 Take Screenshot ({hotkeyStr})";
        _notifyIcon.Text = $"UltraShot ({hotkeyStr})";
        _temporaryModeMenuItem.Checked = SettingsManager.Current.SaveOnlyToClipboard;
    }

    public void ShowNotification(string title, string text, string? imagePath = null, BitmapSource? bitmap = null)
    {
        try
        {
            string? previewUri = null;

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                previewUri = new Uri(imagePath).AbsoluteUri;
            }
            else if (bitmap != null)
            {
                string tempThumbPath = Path.Combine(Path.GetTempPath(), "UltraShot_NotificationPreview.png");
                using (var stream = new FileStream(tempThumbPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }
                previewUri = new Uri(tempThumbPath).AbsoluteUri;
            }

            if (!string.IsNullOrEmpty(previewUri))
            {
                string safeTitle = SecurityElement.Escape(title) ?? title;
                string safeText = SecurityElement.Escape(text) ?? text;

                string xml = $@"<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{safeTitle}</text>
            <text>{safeText}</text>
            <image placement='appLogoOverride' src='{previewUri}' hint-crop='none'/>
        </binding>
    </visual>
</toast>";

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var toast = new ToastNotification(doc)
                {
                    Tag = "ScreenshotNotification"
                };

                toast.Activated += (s, e) => NotificationClicked?.Invoke();
                toast.Dismissed += (s, e) => NotificationClosed?.Invoke();
                toast.Failed += (s, e) =>
                {
                    _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.None);
                };

                ToastNotificationManager.CreateToastNotifier("UltraShot").Show(toast);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Modern toast notification error: {ex.Message}");
        }

        // Fallback to NotifyIcon balloon tip with no info/exclamation badge
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.None);
        }
        catch { }
    }

    private static Icon CreateAppIcon()
    {
        try
        {
            // 1. Try loading directly from file in base directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localIco = Path.Combine(baseDir, "ico", "UltraShot.ico");
            if (File.Exists(localIco))
            {
                return new Icon(localIco);
            }

            // 2. Try loading from WPF embedded assembly resource
            var uri = new Uri("pack://application:,,,/UltraShot;component/ico/UltraShot.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                return new Icon(streamInfo.Stream);
            }

            // 3. Try extracting from current executable process
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                var exeIcon = Icon.ExtractAssociatedIcon(processPath);
                if (exeIcon != null)
                {
                    return exeIcon;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Icon load error: {ex.Message}");
        }

        return GenerateFallbackIcon();
    }

    private static Icon GenerateFallbackIcon()
    {
        // Generate a crisp modern camera icon programmatically if no .ico file is present
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Background rounded rectangle
        using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 122, 204)))
        {
            g.FillEllipse(brush, 1, 1, 30, 30);
        }

        // Camera body
        using (var brush = new SolidBrush(System.Drawing.Color.White))
        {
            g.FillRectangle(brush, 7, 12, 18, 12);
            g.FillRectangle(brush, 11, 9, 10, 4);
        }

        // Camera lens
        using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 122, 204)))
        {
            g.FillEllipse(brush, 12, 14, 8, 8);
        }

        IntPtr hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

