using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UltraShoot.Core;
using UltraShoot.Models;

namespace UltraShoot.UI;

public partial class RecentFlyoutWindow : Window
{
    public event Action<string>? OpenEditorRequested;
    public event Action? OpenSettingsRequested;

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

    public void RefreshScreenshots()
    {
        var items = StorageService.GetRecentScreenshots(25);
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
        Hide();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        StorageService.OpenFolder();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenSettingsRequested?.Invoke();
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ScreenshotItem item })
        {
            Hide();
            OpenEditorRequested?.Invoke(item.FilePath);
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ScreenshotItem item })
        {
            Hide();
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
