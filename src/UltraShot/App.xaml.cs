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
    private string? _lastCapturedFilePath;
    private BitmapSource? _lastCapturedBitmap;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appGuid = "UltraShot-App-SingleInstance-Guid-8f92b7";
        _mutex = new Mutex(true, appGuid, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show("UltraShot is already running in the background! You can access it via the system tray icon.", 
                "UltraShot", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // Global Exception Handling to prevent silent crashes
        DispatcherUnhandledException += (sender, args) =>
        {
            args.Handled = true;
            MessageBox.Show($"An error occurred during an operation:\n{args.Exception.Message}", 
                "UltraShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "UltraShot", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _trayManager.NotificationClicked += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(_lastCapturedFilePath) && System.IO.File.Exists(_lastCapturedFilePath))
                {
                    OpenEditor(_lastCapturedFilePath);
                }
                else if (_lastCapturedBitmap != null)
                {
                    OpenEditor(_lastCapturedBitmap);
                }
            });
        };
        _trayManager.NotificationClosed += () =>
        {
            _lastCapturedBitmap = null;
            TrimMemory();
        };

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
            Debug.WriteLine($"Screen capture error: {ex.Message}");
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

            string? savedFilePath = null;
            if (!SettingsManager.Current.SaveOnlyToClipboard && SettingsManager.Current.AutoSaveScreenshot)
            {
                savedFilePath = await StorageService.SaveBitmapAsync(bitmap);
            }

            _lastCapturedFilePath = savedFilePath;
            _lastCapturedBitmap = bitmap;

            switch (SettingsManager.Current.AfterCaptureBehavior)
            {
                case UltraShot.Models.AfterCaptureAction.ShowRecent:
                    Dispatcher.Invoke(() =>
                    {
                        ShowRecentFlyout();
                    });
                    break;

                case UltraShot.Models.AfterCaptureAction.ShowNotification:
                    string notificationText = SettingsManager.Current.SaveOnlyToClipboard
                        ? "Screenshot copied to clipboard. Click to edit."
                        : "Screenshot saved. Click to edit.";
                    _trayManager?.ShowNotification("UltraShot", notificationText, savedFilePath, bitmap);
                    break;

                case UltraShot.Models.AfterCaptureAction.None:
                    // Quiet mode: do nothing
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Image processing error: {ex.Message}");
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
                MessageBox.Show("The selected image file was not found.", "UltraShot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new EditorWindow(filePath);
            editor.Closed += (s, e) => TrimMemory();
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenEditor(BitmapSource bitmap)
    {
        try
        {
            var editor = new EditorWindow(bitmap);
            editor.Closed += (s, e) => TrimMemory();
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

