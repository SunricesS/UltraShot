using System.Windows.Input;
using System.Windows.Interop;
using UltraShot.Models;

namespace UltraShot.Core;

public class HotkeyManager : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private HwndSource? _source;
    private bool _isRegistered;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        // Create a hidden message window to receive WM_HOTKEY
        var parameters = new HwndSourceParameters("UltraShot_HotkeyReceiver")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(HwndHook);
    }

    public bool RegisterFromSettings(AppSettings settings)
    {
        return Register(settings.HotkeyModifiers, settings.HotkeyKey);
    }

    public bool Register(string modifierStr, string keyStr)
    {
        Unregister();

        if (_source == null || _source.Handle == IntPtr.Zero)
            return false;

        uint modifiers = (uint)NativeMethods.KeyModifiers.NoRepeat;

        if (!string.IsNullOrWhiteSpace(modifierStr))
        {
            var parts = modifierStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= (uint)NativeMethods.KeyModifiers.Control;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= (uint)NativeMethods.KeyModifiers.Shift;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= (uint)NativeMethods.KeyModifiers.Alt;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
                    modifiers |= (uint)NativeMethods.KeyModifiers.Win;
            }
        }

        uint vk = ParseVirtualKey(keyStr);
        if (vk == 0) return false;

        _isRegistered = NativeMethods.RegisterHotKey(_source.Handle, HOTKEY_ID, modifiers, vk);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered && _source != null && _source.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _isRegistered = false;
        }
    }

    private uint ParseVirtualKey(string keyStr)
    {
        if (Enum.TryParse<Key>(keyStr, true, out var wpfKey))
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(wpfKey);
            return (uint)virtualKey;
        }

        if (keyStr.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ||
            keyStr.Equals("PrtScn", StringComparison.OrdinalIgnoreCase) ||
            keyStr.Equals("Snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return 0x2C; // VK_SNAPSHOT
        }

        return 0;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source.Dispose();
            _source = null;
        }
    }
}

