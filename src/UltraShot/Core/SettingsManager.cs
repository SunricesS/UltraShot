using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using UltraShot.Models;

namespace UltraShot.Core;

public static class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraShot");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        // Ensure save directory exists
        try
        {
            if (!Directory.Exists(Current.SaveDirectory))
            {
                Directory.CreateDirectory(Current.SaveDirectory);
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);

            UpdateStartupRegistry(Current.StartWithWindows);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static void UpdateStartupRegistry(bool enable)
    {
        try
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string appName = "UltraShot";
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(appName, $"\"{exePath}\"");
                }
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update startup registry: {ex.Message}");
        }
    }
}

