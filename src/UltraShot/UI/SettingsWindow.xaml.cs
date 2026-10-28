using System.Windows;
using System.Windows.Input;
using UltraShot.Core;

namespace UltraShot.UI;

public partial class SettingsWindow : Window
{
    private string _currentModifiers = "Control, Shift";
    private string _currentKey = "S";

    public event Action? SettingsSaved;

    public SettingsWindow()
    {
        InitializeComponent();
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
            foreach (var mod in _currentModifiers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (mod.Equals("Control", StringComparison.OrdinalIgnoreCase)) parts.Add("Ctrl");
                else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase)) parts.Add("Shift");
                else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase)) parts.Add("Alt");
                else if (mod.Equals("Win", StringComparison.OrdinalIgnoreCase)) parts.Add("Win");
            }
        }
        parts.Add(_currentKey);
        HotkeyTextBox.Text = string.Join(" + ", parts);
    }

    private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyTextBox.Text = "Tuş kombinasyonuna basın...";
    }

    private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateHotkeyTextBoxDisplay();
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

        var mods = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) mods.Add("Control");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) mods.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) mods.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) mods.Add("Win");

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
        Close();
    }
}

