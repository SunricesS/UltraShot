using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using UltraShot.Models;

namespace UltraShot.Core;

public class HotkeyManager : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private HwndSource? _source;
    private bool _isRegistered;

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;

    private uint _targetVk;
    private bool _targetWin;
    private bool _targetCtrl;
    private bool _targetShift;
    private bool _targetAlt;
    private bool _suppressNextKeyUp;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        // Create a hidden message window to receive WM_HOTKEY when RegisterHotKey is used
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

        _targetWin = false;
        _targetCtrl = false;
        _targetShift = false;
        _targetAlt = false;

        uint modifiers = (uint)NativeMethods.KeyModifiers.NoRepeat;

        if (!string.IsNullOrWhiteSpace(modifierStr))
        {
            var parts = modifierStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    _targetCtrl = true;
                    modifiers |= (uint)NativeMethods.KeyModifiers.Control;
                }
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    _targetShift = true;
                    modifiers |= (uint)NativeMethods.KeyModifiers.Shift;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    _targetAlt = true;
                    modifiers |= (uint)NativeMethods.KeyModifiers.Alt;
                }
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
                {
                    _targetWin = true;
                    modifiers |= (uint)NativeMethods.KeyModifiers.Win;
                }
            }
        }

        _targetVk = ParseVirtualKey(keyStr);
        if (_targetVk == 0) return false;

        // If Win key is part of the shortcut, use WH_KEYBOARD_LL directly because
        // Windows shell reserves most Win key combinations (e.g. Win+Shift+S, Win+S, Win+A)
        // and RegisterHotKey fails with ERROR_HOTKEY_ALREADY_REGISTERED (1409).
        if (_targetWin)
        {
            return InstallHook();
        }

        // For standard combinations, try standard Win32 RegisterHotKey first
        if (_source != null && _source.Handle != IntPtr.Zero)
        {
            _isRegistered = NativeMethods.RegisterHotKey(_source.Handle, HOTKEY_ID, modifiers, _targetVk);
            if (_isRegistered)
            {
                return true;
            }
        }

        // If RegisterHotKey failed (e.g., PrintScreen or shortcut conflict), fall back to hook
        return InstallHook();
    }

    private bool InstallHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        // Prevent delegate from being garbage collected
        _hookProc = HookCallback;
        IntPtr hMod = NativeMethods.GetModuleHandle(IntPtr.Zero);
        _hookId = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _hookProc, hMod, 0);
        return _hookId != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_isRegistered && _source != null && _source.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _isRegistered = false;
        }

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _hookProc = null;
        }
        _suppressNextKeyUp = false;
    }

    private uint ParseVirtualKey(string keyStr)
    {
        if (keyStr.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ||
            keyStr.Equals("PrtScn", StringComparison.OrdinalIgnoreCase) ||
            keyStr.Equals("Snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return 0x2C; // VK_SNAPSHOT
        }

        if (Enum.TryParse<Key>(keyStr, true, out var wpfKey))
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(wpfKey);
            return (uint)virtualKey;
        }

        return 0;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (hookStruct.vkCode == _targetVk)
                {
                    bool winDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0 ||
                                   (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;
                    bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                    bool shiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

                    if (winDown == _targetWin &&
                        ctrlDown == _targetCtrl &&
                        shiftDown == _targetShift &&
                        altDown == _targetAlt)
                    {
                        _suppressNextKeyUp = true;
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            HotkeyPressed?.Invoke();
                        });
                        return (IntPtr)1; // Consume key, suppress Windows default tools (e.g. Snipping Tool)
                    }
                }
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                if (_suppressNextKeyUp)
                {
                    var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    if (hookStruct.vkCode == _targetVk)
                    {
                        _suppressNextKeyUp = false;
                        return (IntPtr)1;
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
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
