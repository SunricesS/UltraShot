using System.Drawing;
using System.Windows.Forms;
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

    public TrayIconManager()
    {
        var contextMenu = new ContextMenuStrip();

        _captureMenuItem = new ToolStripMenuItem("📷 Ekran Görüntüsü Al", null, (s, e) => CaptureTriggered?.Invoke());
        _recentMenuItem = new ToolStripMenuItem("🖼️ Son Ekran Görüntüleri", null, (s, e) => RecentScreenshotsTriggered?.Invoke());
        _openFolderMenuItem = new ToolStripMenuItem("📁 Ekran Görüntüleri Klasörünü Aç", null, (s, e) => OpenFolderTriggered?.Invoke());
        
        _temporaryModeMenuItem = new ToolStripMenuItem("📋 Yalnızca Panoya Kaydet (Geçici)", null, (s, e) =>
        {
            if (s is ToolStripMenuItem mi)
            {
                SettingsManager.Current.SaveOnlyToClipboard = mi.Checked;
                SettingsManager.Save();
                TemporaryModeToggled?.Invoke(mi.Checked);
            }
        });
        _temporaryModeMenuItem.CheckOnClick = true;

        _settingsMenuItem = new ToolStripMenuItem("⚙️ Kısayol ve Ayarlar", null, (s, e) => SettingsTriggered?.Invoke());
        _exitMenuItem = new ToolStripMenuItem("❌ Çıkış", null, (s, e) => ExitTriggered?.Invoke());

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

        UpdateDisplayStrings();
    }

    public void UpdateDisplayStrings()
    {
        var hotkeyStr = SettingsManager.Current.GetHotkeyDisplayString();
        _captureMenuItem.Text = $"📷 Ekran Görüntüsü Al ({hotkeyStr})";
        _notifyIcon.Text = $"UltraShot ({hotkeyStr})";
        _temporaryModeMenuItem.Checked = SettingsManager.Current.SaveOnlyToClipboard;
    }

    public void ShowNotification(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(1500, title, text, ToolTipIcon.Info);
        }
        catch { }
    }

    private static Icon CreateAppIcon()
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

