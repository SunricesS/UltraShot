using System.Diagnostics;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using UltraShot.Core;
using UltraShot.UI;

namespace UltraShot;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TrayIconManager? _trayManager;
    private HotkeyManager? _hotkeyManager;
    private RecentFlyoutWindow? _recentFlyout;
    private SettingsWindow? _settingsWindow;
    private bool _isCapturing;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appGuid = "UltraShot-App-SingleInstance-Guid-8f92b7";
        _mutex = new Mutex(true, appGuid, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show("UltraShot zaten arka planda çalışıyor! Sistem tepsisindeki küçük simgeden erişebilirsiniz.", 
                "UltraShot", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // Global Exception Handling to prevent silent crashes
        DispatcherUnhandledException += (sender, args) =>
        {
            args.Handled = true;
            MessageBox.Show($"Bir işlem sırasında hata oluştu:\n{args.Exception.Message}", 
                "UltraShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Beklenmeyen hata:\n{ex.Message}", "UltraShot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        // Load configuration
        SettingsManager.Load();

        // Initialize tray and hotkey
        _trayManager = new TrayIconManager();
        _trayManager.CaptureTriggered += TriggerCapture;
        _trayManager.RecentScreenshotsTriggered += ShowRecentFlyout;
        _trayManager.OpenFolderTriggered += () => StorageService.OpenFolder();
        _trayManager.SettingsTriggered += ShowSettings;
        _trayManager.ExitTriggered += ShutdownApp;

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += TriggerCapture;
        _hotkeyManager.RegisterFromSettings(SettingsManager.Current);

        TrimMemory();
    }

    public void TriggerCapture()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            // Capture virtual desktop instantly
            var capture = ScreenCaptureService.CaptureVirtualScreen();

            var overlay = new OverlayWindow(capture);
            overlay.ScreenshotCaptured += async (croppedBitmap) =>
            {
                await HandleCapturedScreenshotAsync(croppedBitmap);
            };

            overlay.Closed += (s, e) =>
            {
                _isCapturing = false;
                TrimMemory();
            };

            overlay.Show();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            Debug.WriteLine($"Ekran yakalama hatası: {ex.Message}");
        }
    }

    private async Task HandleCapturedScreenshotAsync(BitmapSource bitmap)
    {
        try
        {
            if (SettingsManager.Current.PlaySound)
            {
                SystemSounds.Asterisk.Play();
            }

            if (!SettingsManager.Current.SaveOnlyToClipboard && SettingsManager.Current.AutoSaveScreenshot)
            {
                await StorageService.SaveBitmapAsync(bitmap);
            }
            else if (!SettingsManager.Current.OpenFlyoutAfterCapture)
            {
                _trayManager?.ShowNotification("UltraShot", "Ekran görüntüsü panoya kopyalandı (Geçici mod).");
            }

            if (SettingsManager.Current.OpenFlyoutAfterCapture)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowRecentFlyout();
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Görsel işleme hatası: {ex.Message}");
        }
    }

    public void ShowRecentFlyout()
    {
        if (_recentFlyout == null)
        {
            _recentFlyout = new RecentFlyoutWindow();
            _recentFlyout.OpenEditorRequested += OpenEditor;
            _recentFlyout.OpenSettingsRequested += ShowSettings;
            _recentFlyout.FlyoutHidden += TrimMemory;
            _recentFlyout.Closed += (s, e) => _recentFlyout = null;
        }

        _recentFlyout.ShowAtTray();
    }

    public void OpenEditor(string filePath)
    {
        try
        {
            if (!System.IO.File.Exists(filePath))
            {
                MessageBox.Show("Düzenlenmek istenen görsel bulunamadı.", "UltraShot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new EditorWindow(filePath);
            editor.Closed += (s, e) => TrimMemory();
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Düzenleyici açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.SettingsSaved += () =>
        {
            _hotkeyManager?.RegisterFromSettings(SettingsManager.Current);
            _trayManager?.UpdateDisplayStrings();
        };
        _settingsWindow.Closed += (s, e) =>
        {
            _settingsWindow = null;
            TrimMemory();
        };
        _settingsWindow.ShowDialog();
    }

    private void CleanupResources()
    {
        _hotkeyManager?.Dispose();
        _hotkeyManager = null;

        _trayManager?.Dispose();
        _trayManager = null;

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private void ShutdownApp()
    {
        CleanupResources();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupResources();
        base.OnExit(e);
    }

    private static void TrimMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }
}

