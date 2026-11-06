using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using UltraShot.Models;

namespace UltraShot.Core;

public class HotkeyManager : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private HwndSource? _source;
    private bool _isRegistered;

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private GCHandle _hookProcHandle;

    private uint _targetVk;
    private bool _targetWin;
    private bool _targetCtrl;
    private bool _targetShift;
    private bool _targetAlt;

    // Track physical Win key press with automatic expiration to prevent stuck state after Lock Screen / Admin windows
    private bool _isWinDown;
    private long _winDownTimestamp;

    private bool _suppressNextKeyUp;
    private readonly DispatcherTimer _keepAliveTimer;

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

        // Pre-JIT the hook callback so it never stalls on first key press
        try
        {
            var method = typeof(HotkeyManager).GetMethod(
                nameof(HookCallback),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method != null)
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
            }
        }
        catch { }

        // Listen for session lock/unlock and sleep/resume to ensure hooks stay alive
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Lightweight keep-alive timer (every 45s) to prevent Windows from paging out
        // the hotkey hook and UI dispatcher pages when idle for long periods
        _keepAliveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(45)
        };
        _keepAliveTimer.Tick += OnKeepAliveTick;
        _keepAliveTimer.Start();
    }

    private void OnKeepAliveTick(object? sender, EventArgs e)
    {
        // Touch critical state so OS keeps hook pages in physical working set
        _ = _targetVk;
        _ = _hookId;
        _ = NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Reset any modifier state on lock/unlock and refresh hook on unlock
        _isWinDown = false;
        _suppressNextKeyUp = false;

        if (e.Reason == SessionSwitchReason.SessionUnlock && _hookId != IntPtr.Zero)
        {
            InstallHook();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _isWinDown = false;
            _suppressNextKeyUp = false;

            if (_hookId != IntPtr.Zero)
            {
                InstallHook();
            }
        }
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

        if (_hookProcHandle.IsAllocated)
        {
            _hookProcHandle.Free();
        }

        _isWinDown = false;
        _suppressNextKeyUp = false;

        // Pin delegate with GCHandle so GC can never relocate or collect it
        _hookProc = HookCallback;
        _hookProcHandle = GCHandle.Alloc(_hookProc);
        _hookId = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);
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

        if (_hookProcHandle.IsAllocated)
        {
            _hookProcHandle.Free();
        }

        _isWinDown = false;
        _suppressNextKeyUp = false;
    }

    private static uint ParseVirtualKey(string keyStr)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            // Read vkCode directly from first 4 bytes of KBDLLHOOKSTRUCT without full struct marshaling
            uint vk = (uint)Marshal.ReadInt32(lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                if (vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                {
                    _isWinDown = true;
                    _winDownTimestamp = Environment.TickCount64;
                }

                if (vk == _targetVk)
                {
                    bool winDown = IsKeyDown(NativeMethods.VK_LWIN) ||
                                   IsKeyDown(NativeMethods.VK_RWIN) ||
                                   (_isWinDown && (Environment.TickCount64 - _winDownTimestamp) < 4000);

                    bool ctrlDown = IsKeyDown(NativeMethods.VK_CONTROL) ||
                                    IsKeyDown(NativeMethods.VK_LCONTROL) ||
                                    IsKeyDown(NativeMethods.VK_RCONTROL);

                    bool shiftDown = IsKeyDown(NativeMethods.VK_SHIFT) ||
                                     IsKeyDown(NativeMethods.VK_LSHIFT) ||
                                     IsKeyDown(NativeMethods.VK_RSHIFT);

                    bool altDown = IsKeyDown(NativeMethods.VK_MENU) ||
                                   IsKeyDown(NativeMethods.VK_LMENU) ||
                                   IsKeyDown(NativeMethods.VK_RMENU);

                    if (winDown == _targetWin &&
                        ctrlDown == _targetCtrl &&
                        shiftDown == _targetShift &&
                        altDown == _targetAlt)
                    {
                        _suppressNextKeyUp = true;
                        bool releaseWin = _targetWin;

                        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
                        {
                            if (releaseWin)
                            {
                                NativeMethods.SendMenuMaskAndReleaseWin();
                            }
                            HotkeyPressed?.Invoke();
                        });

                        return (IntPtr)1; // Consume key immediately so Windows hook never times out
                    }
                }
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                if (vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                {
                    _isWinDown = false;
                }

                if (vk == _targetVk && _suppressNextKeyUp)
                {
                    _suppressNextKeyUp = false;
                    return (IntPtr)1;
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
        _keepAliveTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        Unregister();
        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source.Dispose();
            _source = null;
        }
    }
}
