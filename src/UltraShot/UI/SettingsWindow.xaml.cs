using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using UltraShot.Core;

namespace UltraShot.UI;

public partial class SettingsWindow : Window
{
    private string _currentModifiers = "Control, Shift";
    private string _currentKey = "S";

    private IntPtr _recordingHookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _recordingHookProc;

    public event Action? SettingsSaved;

    public SettingsWindow()
    {
        InitializeComponent();
        Closed += (s, e) => StopRecordingHook();
        Deactivated += (s, e) => StopRecordingHook();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = SettingsManager.Current;

        _currentModifiers = settings.HotkeyModifiers;
        _currentKey = settings.HotkeyKey;
        UpdateHotkeyTextBoxDisplay();

        SaveFolderTextBox.Text = settings.SaveDirectory;

        // Image Format
        FormatComboBox.SelectedIndex = settings.ImageFormat.ToUpperInvariant() switch
        {
            "JPEG" or "JPG" => 1,
            "BMP" => 2,
            _ => 0
        };

        // JPEG Quality
        int quality = Math.Clamp(settings.JpegQuality, 10, 100);
        QualitySlider.Value = quality;
        if (QualityPercentText != null)
        {
            QualityPercentText.Text = $"%{quality}";
        }

        UpdateQualityControlState();

        SaveOnlyToClipboardCheckBox.IsChecked = settings.SaveOnlyToClipboard;
        UpdateTemporaryModeHintState();

        OpenFlyoutCheckBox.IsChecked = settings.OpenFlyoutAfterCapture;
        CopyClipboardCheckBox.IsChecked = settings.CopyImageToClipboard;
        PlaySoundCheckBox.IsChecked = settings.PlaySound;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
    }

    private void SaveOnlyToClipboard_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTemporaryModeHintState();
    }

    private void UpdateTemporaryModeHintState()
    {
        if (TemporaryModeHintText != null && SaveOnlyToClipboardCheckBox != null)
        {
            TemporaryModeHintText.Visibility = (SaveOnlyToClipboardCheckBox.IsChecked == true) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }

    private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateQualityControlState();
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityPercentText != null)
        {
            QualityPercentText.Text = $"%{(int)QualitySlider.Value}";
        }
    }

    private void UpdateQualityControlState()
    {
        if (FormatComboBox == null || QualitySlider == null || QualityHintText == null) return;

        bool isJpeg = FormatComboBox.SelectedIndex == 1; // JPEG
        QualitySlider.IsEnabled = isJpeg;
        QualitySlider.Opacity = isJpeg ? 1.0 : 0.45;

        if (isJpeg)
        {
            QualityHintText.Text = "* %90 kalite gözle görülür kayıp olmadan dosya boyutunu ciddi oranda düşürür.";
            QualityHintText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#888888")!;
        }
        else
        {
            QualityHintText.Text = "* Bu ayar yalnızca JPEG formatı seçildiğinde etkilidir. PNG ve BMP kayıpsız formatlardır.";
            QualityHintText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#666666")!;
        }
    }

    private void UpdateHotkeyTextBoxDisplay()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_currentModifiers) && _currentModifiers != "None")
        {
            var mods = _currentModifiers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (mods.Any(m => m.Equals("Win", StringComparison.OrdinalIgnoreCase))) parts.Add("Win");
            if (mods.Any(m => m.Equals("Control", StringComparison.OrdinalIgnoreCase))) parts.Add("Ctrl");
            if (mods.Any(m => m.Equals("Shift", StringComparison.OrdinalIgnoreCase))) parts.Add("Shift");
            if (mods.Any(m => m.Equals("Alt", StringComparison.OrdinalIgnoreCase))) parts.Add("Alt");
        }
        parts.Add(_currentKey);
        HotkeyTextBox.Text = string.Join(" + ", parts);
    }

    private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyTextBox.Text = "Tuş kombinasyonuna basın...";
        StartRecordingHook();
    }

    private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        StopRecordingHook();
        UpdateHotkeyTextBoxDisplay();
    }

    private void StartRecordingHook()
    {
        if (_recordingHookId != IntPtr.Zero) return;

        _recordingHookProc = RecordingHookCallback;
        IntPtr hMod = NativeMethods.GetModuleHandle(IntPtr.Zero);
        _recordingHookId = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _recordingHookProc, hMod, 0);
    }

    private void StopRecordingHook()
    {
        if (_recordingHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_recordingHookId);
            _recordingHookId = IntPtr.Zero;
            _recordingHookProc = null;
        }
    }

    private IntPtr RecordingHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                uint vkCode = hookStruct.vkCode;

                // Check for lone modifier keys
                if (vkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN or
                    NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT or
                    NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or
                    NativeMethods.VK_MENU or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU)
                {
                    // Suppress lone Win key so Start Menu does not pop up while recording
                    if (vkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                    {
                        return (IntPtr)1;
                    }
                    return NativeMethods.CallNextHookEx(_recordingHookId, nCode, wParam, lParam);
                }

                // A key was pressed. Detect modifier states via GetAsyncKeyState
                bool win = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                           (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
                bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                bool alt = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

                var mods = new List<string>();
                if (win) mods.Add("Win");
                if (ctrl) mods.Add("Control");
                if (shift) mods.Add("Shift");
                if (alt) mods.Add("Alt");

                string keyName;
                if (vkCode == 0x2C) // VK_SNAPSHOT
                {
                    keyName = "PrintScreen";
                }
                else
                {
                    var wpfKey = KeyInterop.KeyFromVirtualKey((int)vkCode);
                    keyName = wpfKey.ToString();
                }

                _currentModifiers = mods.Count > 0 ? string.Join(", ", mods) : "None";
                _currentKey = keyName;

                Dispatcher.BeginInvoke(UpdateHotkeyTextBoxDisplay);

                // Consume the key so Windows shell shortcuts (e.g. Win+Shift+S, Win+S, Win+A)
                // don't launch Snipping Tool or Search while configuring in UltraShot
                return (IntPtr)1;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (hookStruct.vkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                {
                    // Suppress Start Menu trigger on Win release
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_recordingHookId, nCode, wParam, lParam);
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Skip lone modifier keys
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        bool isWinDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                         (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0 ||
                         (Keyboard.Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows;
        bool isCtrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0 ||
                          (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isShiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0 ||
                           (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool isAltDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0 ||
                         (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        var mods = new List<string>();
        if (isWinDown) mods.Add("Win");
        if (isCtrlDown) mods.Add("Control");
        if (isShiftDown) mods.Add("Shift");
        if (isAltDown) mods.Add("Alt");

        _currentModifiers = mods.Count > 0 ? string.Join(", ", mods) : "None";
        _currentKey = key switch
        {
            Key.Snapshot => "PrintScreen",
            _ => key.ToString()
        };

        UpdateHotkeyTextBoxDisplay();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        _currentModifiers = "Control, Shift";
        _currentKey = "S";
        UpdateHotkeyTextBoxDisplay();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Ekran görüntülerinin kaydedileceği klasörü seçin",
            SelectedPath = SaveFolderTextBox.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SaveFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        StopRecordingHook();

        var settings = SettingsManager.Current;

        settings.HotkeyModifiers = _currentModifiers;
        settings.HotkeyKey = _currentKey;
        settings.SaveDirectory = SaveFolderTextBox.Text;
        settings.SaveOnlyToClipboard = SaveOnlyToClipboardCheckBox.IsChecked ?? false;
        settings.OpenFlyoutAfterCapture = OpenFlyoutCheckBox.IsChecked ?? true;
        settings.CopyImageToClipboard = CopyClipboardCheckBox.IsChecked ?? true;
        settings.PlaySound = PlaySoundCheckBox.IsChecked ?? false;
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;

        if (FormatComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            settings.ImageFormat = tag;
        }
        else
        {
            settings.ImageFormat = "PNG";
        }

        settings.JpegQuality = (int)QualitySlider.Value;

        SettingsManager.Save();
        SettingsSaved?.Invoke();

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StopRecordingHook();
        Close();
    }
}
