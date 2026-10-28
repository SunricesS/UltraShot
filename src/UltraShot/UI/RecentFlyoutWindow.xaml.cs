using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UltraShot.Core;
using UltraShot.Models;

namespace UltraShot.UI;

public partial class RecentFlyoutWindow : Window
{
    public event Action<string>? OpenEditorRequested;
    public event Action? OpenSettingsRequested;
    public event Action? FlyoutHidden;

    public RecentFlyoutWindow()
    {
        InitializeComponent();
    }

    public void ShowAtTray()
    {
        RefreshScreenshots();

        // Place near the tray in bottom right of primary work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;

        Show();
        Activate();
        Focus();
    }

    public void Dismiss()
    {
        Hide();
        ScreenshotsList.ItemsSource = null;
        FlyoutHidden?.Invoke();
    }

    public void RefreshScreenshots()
    {
        var items = StorageService.GetRecentScreenshots(15);
        ScreenshotsList.ItemsSource = items;

        if (items.Count == 0)
        {
            EmptyNotice.Visibility = Visibility.Visible;
            CountText.Text = "Ekran görüntüsü yok";
        }
        else
        {
            EmptyNotice.Visibility = Visibility.Collapsed;
            CountText.Text = $"Son {items.Count} ekran görüntüsü";
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Dismiss();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        StorageService.OpenFolder();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        OpenSettingsRequested?.Invoke();
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ScreenshotItem item })
        {
            Dismiss();
            OpenEditorRequested?.Invoke(item.FilePath);
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ScreenshotItem item })
        {
            Dismiss();
            OpenEditorRequested?.Invoke(item.FilePath);
        }
    }

    private void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ScreenshotItem item } && File.Exists(item.FilePath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(item.FilePath);
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                Clipboard.SetImage(bitmap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kopyalanamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ScreenshotItem item })
        {
            var result = MessageBox.Show(
                $"'{item.FileName}' dosyasını silmek istediğinize emin misiniz?",
                "Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                StorageService.DeleteScreenshot(item.FilePath);
                RefreshScreenshots();
            }
        }
    }
}

