using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using UltraShot.Models;

namespace UltraShot.Core;

public static class StorageService
{
    public static string GenerateUniqueFilePath()
    {
        var folder = SettingsManager.Current.SaveDirectory;
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var ext = SettingsManager.Current.GetFileExtension();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var fileName = $"UltraShot_{timestamp}{ext}";
        return Path.Combine(folder, fileName);
    }

    public static async Task<string> SaveBitmapAsync(BitmapSource bitmapSource)
    {
        var filePath = GenerateUniqueFilePath();

        // Encode according to configured image format and quality
        using var memoryStream = new MemoryStream();
        BitmapEncoder encoder = SettingsManager.Current.ImageFormat.ToUpperInvariant() switch
        {
            "JPEG" or "JPG" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(SettingsManager.Current.JpegQuality, 10, 100) },
            "BMP" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        encoder.Save(memoryStream);
        var bytes = memoryStream.ToArray();

        await File.WriteAllBytesAsync(filePath, bytes);
        return filePath;
    }

    public static void SaveBitmapDirect(BitmapSource bitmapSource, string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var ext = Path.GetExtension(destinationPath).ToLowerInvariant();
        BitmapEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(SettingsManager.Current.JpegQuality, 10, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        using var stream = File.Open(destinationPath, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
    }

    public static List<ScreenshotItem> GetRecentScreenshots(int maxCount = 20)
    {
        var list = new List<ScreenshotItem>();
        var folder = SettingsManager.Current.SaveDirectory;
        if (!Directory.Exists(folder))
        {
            return list;
        }

        try
        {
            var files = new DirectoryInfo(folder)
                .GetFiles("*.png")
                .Concat(new DirectoryInfo(folder).GetFiles("*.jpg"))
                .Concat(new DirectoryInfo(folder).GetFiles("*.jpeg"))
                .Concat(new DirectoryInfo(folder).GetFiles("*.bmp"))
                .OrderByDescending(f => f.CreationTime)
                .Take(maxCount);

            foreach (var file in files)
            {
                var item = new ScreenshotItem
                {
                    FilePath = file.FullName,
                    CreatedAt = file.CreationTime,
                    FileSizeBytes = file.Length
                };

                // Read image dimensions without decoding full image into memory
                try
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        item.Width = frame.PixelWidth;
                        item.Height = frame.PixelHeight;
                    }
                }
                catch
                {
                    item.Width = 0;
                    item.Height = 0;
                }

                list.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to retrieve recent screenshots: {ex.Message}");
        }

        return list;
    }

    public static void DeleteScreenshot(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete file: {ex.Message}");
        }
    }

    public static void OpenFolder(string? selectFilePath = null)
    {
        try
        {
            var folder = SettingsManager.Current.SaveDirectory;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (!string.IsNullOrEmpty(selectFilePath) && File.Exists(selectFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{selectFilePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open folder: {ex.Message}");
        }
    }
}

