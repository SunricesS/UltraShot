using System.IO;
using System.Text.Json;

namespace UltraShot.Models;

public class AppSettings
{
    public string SaveDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "UltraShot");

    public string HotkeyModifiers { get; set; } = "Control, Shift";
    public string HotkeyKey { get; set; } = "S";

    public bool PlaySound { get; set; } = false;
    public bool OpenFlyoutAfterCapture { get; set; } = true;
    public bool CopyImageToClipboard { get; set; } = true;
    public bool AutoSaveScreenshot { get; set; } = true;
    public bool SaveOnlyToClipboard
    {
        get => !AutoSaveScreenshot;
        set => AutoSaveScreenshot = !value;
    }
    public bool StartWithWindows { get; set; } = false;
    public bool ConfirmBeforeDelete { get; set; } = true;

    public string ImageFormat { get; set; } = "PNG";
    public int JpegQuality { get; set; } = 90;

    public string GetFileExtension() => ImageFormat.ToUpperInvariant() switch
    {
        "JPEG" or "JPG" => ".jpg",
        "BMP" => ".bmp",
        _ => ".png"
    };

    // Helper to format hotkey display string (e.g. "Ctrl + Shift + S" or "PrintScreen")
    public string GetHotkeyDisplayString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(HotkeyModifiers) && HotkeyModifiers != "None")
        {
            var mods = HotkeyModifiers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (mods.Any(m => m.Equals("Win", StringComparison.OrdinalIgnoreCase))) parts.Add("Win");
            if (mods.Any(m => m.Equals("Control", StringComparison.OrdinalIgnoreCase))) parts.Add("Ctrl");
            if (mods.Any(m => m.Equals("Shift", StringComparison.OrdinalIgnoreCase))) parts.Add("Shift");
            if (mods.Any(m => m.Equals("Alt", StringComparison.OrdinalIgnoreCase))) parts.Add("Alt");
        }
        parts.Add(HotkeyKey);
        return string.Join(" + ", parts);
    }
}

