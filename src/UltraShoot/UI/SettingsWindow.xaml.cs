using System.Windows;
using System.Windows.Input;
using UltraShoot.Core;

namespace UltraShoot.UI;

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
        OpenFlyoutCheckBox.IsChecked = settings.OpenFlyoutAfterCapture;
        CopyClipboardCheckBox.IsChecked = settings.CopyImageToClipboard;
        PlaySoundCheckBox.IsChecked = settings.PlaySound;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
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
        settings.OpenFlyoutAfterCapture = OpenFlyoutCheckBox.IsChecked ?? true;
        settings.CopyImageToClipboard = CopyClipboardCheckBox.IsChecked ?? true;
        settings.PlaySound = PlaySoundCheckBox.IsChecked ?? false;
        settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;

        SettingsManager.Save();
        SettingsSaved?.Invoke();

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
