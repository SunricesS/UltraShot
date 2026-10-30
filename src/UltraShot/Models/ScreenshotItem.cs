using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace UltraShot.Models;

public class ScreenshotItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isConfirmOpen;
    public bool IsConfirmOpen
    {
        get => _isConfirmOpen;
        set
        {
            if (_isConfirmOpen != value)
            {
                _isConfirmOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConfirmOpen)));
            }
        }
    }

    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public DateTime CreatedAt { get; set; }
    public string FormattedDate => CreatedAt.ToString("HH:mm:ss - dd.MM.yyyy");
    public long FileSizeBytes { get; set; }
    public string FileSizeFormatted
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
    public int Width { get; set; }
    public int Height { get; set; }
    public string DimensionsText => $"{Width} × {Height}";

    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && File.Exists(FilePath))
            {
                _thumbnail = LoadThumbnail(FilePath, 120);
            }
            return _thumbnail;
        }
    }

    private static BitmapSource? LoadThumbnail(string path, int decodePixelWidth)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
