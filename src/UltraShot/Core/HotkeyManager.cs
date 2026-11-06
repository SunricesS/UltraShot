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
    // Maximum time window (ms) allowed from the first key press to the last key press in flexible combo mode (< 1 second)
    private const long FLEXIBLE_COMBO_WINDOW_MS = 650;

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
    private bool _flexibleOrder = true;

    // Physical down tracking with automatic 4s expiration to prevent stuck state after Lock Screen / Admin windows
    private bool _isWinDown;
    private bool _isCtrlDown;
    private bool _isShiftDown;
    private bool _isAltDown;
    private bool _isTargetDown;

    private long _winDownTimestamp;
    private long _ctrlDownTimestamp;
    private long _shiftDownTimestamp;
    private long _altDownTimestamp;
    private long _targetDownTimestamp;

    // Initial key-down timestamps (non-repeating) for rapid order-independent combo matching
    private long _lastWinPressTick;
    private long _lastCtrlPressTick;
    private long _lastShiftPressTick;
    private long _lastAltPressTick;
    private long _lastTargetPressTick;

    private bool _suppressNextKeyUp;
    private uint _suppressedVk;
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

    private void ResetState()
    {
        _isWinDown = false;
        _isCtrlDown = false;
        _isShiftDown = false;
        _isAltDown = false;
        _isTargetDown = false;
        _suppressNextKeyUp = false;
        ResetComboTimestamps();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetComboTimestamps()
    {
        _lastWinPressTick = 0;
        _lastCtrlPressTick = 0;
        _lastShiftPressTick = 0;
        _lastAltPressTick = 0;
        _lastTargetPressTick = 0;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        ResetState();

        if (e.Reason == SessionSwitchReason.SessionUnlock && _hookId != IntPtr.Zero)
        {
            InstallHook();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            ResetState();

            if (_hookId != IntPtr.Zero)
            {
                InstallHook();
            }
        }
    }

    public bool RegisterFromSettings(AppSettings settings)
    {
        return Register(settings.HotkeyModifiers, settings.HotkeyKey, settings.FlexibleHotkeyOrder);
    }

    public bool Register(string modifierStr, string keyStr, bool flexibleOrder = true)
    {
        Unregister();

        _flexibleOrder = flexibleOrder;
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

        bool hasModifiers = _targetWin || _targetCtrl || _targetShift || _targetAlt;

        // Use WH_KEYBOARD_LL directly when Win key is involved or when flexible order is enabled with modifiers
        if (_targetWin || (_flexibleOrder && hasModifiers))
        {
            return InstallHook();
        }

        // For standard strict combinations, try standard Win32 RegisterHotKey first
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

        ResetState();

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

        ResetState();
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

            // Ignore synthetic mask key (0xE8) injected by SendMenuMaskAndReleaseWin
            if (vk == NativeMethods.VK_MASK_KEY)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool isWinKey = vk is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN;
            bool isCtrlKey = vk is NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL;
            bool isShiftKey = vk is NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT;
            bool isAltKey = vk is NativeMethods.VK_MENU or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU;
            bool isTargetKey = vk == _targetVk;

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                long now = Environment.TickCount64;

                bool isPartOfTargetCombo =
                    (isWinKey && _targetWin) ||
                    (isCtrlKey && _targetCtrl) ||
                    (isShiftKey && _targetShift) ||
                    (isAltKey && _targetAlt) ||
                    isTargetKey;

                if (!isPartOfTargetCombo)
                {
                    // Any unrelated key press immediately invalidates the rapid combo sequence
                    ResetComboTimestamps();
                }
                else
                {
                    // Record initial press timestamp (ignore auto-repeat while key is held down)
                    if (isWinKey)
                    {
                        if (!_isWinDown || (now - _winDownTimestamp) > 4000)
                        {
                            _lastWinPressTick = now;
                        }
                        _isWinDown = true;
                        _winDownTimestamp = now;
                    }
                    else if (isCtrlKey)
                    {
                        if (!_isCtrlDown || (now - _ctrlDownTimestamp) > 4000)
                        {
                            _lastCtrlPressTick = now;
                        }
                        _isCtrlDown = true;
                        _ctrlDownTimestamp = now;
                    }
                    else if (isShiftKey)
                    {
                        if (!_isShiftDown || (now - _shiftDownTimestamp) > 4000)
                        {
                            _lastShiftPressTick = now;
                        }
                        _isShiftDown = true;
                        _shiftDownTimestamp = now;
                    }
                    else if (isAltKey)
                    {
                        if (!_isAltDown || (now - _altDownTimestamp) > 4000)
                        {
                            _lastAltPressTick = now;
                        }
                        _isAltDown = true;
                        _altDownTimestamp = now;
                    }
                    else if (isTargetKey)
                    {
                        if (!_isTargetDown || (now - _targetDownTimestamp) > 4000)
                        {
                            _lastTargetPressTick = now;
                        }
                        _isTargetDown = true;
                        _targetDownTimestamp = now;
                    }

                    // Ensure no non-configured modifier is currently held down
                    bool unwantedWin = !_targetWin && (IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN));
                    bool unwantedCtrl = !_targetCtrl && (IsKeyDown(NativeMethods.VK_CONTROL) || IsKeyDown(NativeMethods.VK_LCONTROL) || IsKeyDown(NativeMethods.VK_RCONTROL));
                    bool unwantedShift = !_targetShift && (IsKeyDown(NativeMethods.VK_SHIFT) || IsKeyDown(NativeMethods.VK_LSHIFT) || IsKeyDown(NativeMethods.VK_RSHIFT));
                    bool unwantedAlt = !_targetAlt && (IsKeyDown(NativeMethods.VK_MENU) || IsKeyDown(NativeMethods.VK_LMENU) || IsKeyDown(NativeMethods.VK_RMENU));

                    if (!(unwantedWin || unwantedCtrl || unwantedShift || unwantedAlt))
                    {
                        bool shouldTrigger = false;

                        if (_flexibleOrder)
                        {
                            // Flexible order mode: all required keys in the combo can be pressed in ANY order
                            // (e.g. Win+Shift+S, Shift+Win+S, S+Win+Shift) as long as all of them were pressed
                            // rapidly within FLEXIBLE_COMBO_WINDOW_MS (650ms < 1s).
                            bool winRapid = !_targetWin || (_lastWinPressTick > 0 && (now - _lastWinPressTick) <= FLEXIBLE_COMBO_WINDOW_MS);
                            bool ctrlRapid = !_targetCtrl || (_lastCtrlPressTick > 0 && (now - _lastCtrlPressTick) <= FLEXIBLE_COMBO_WINDOW_MS);
                            bool shiftRapid = !_targetShift || (_lastShiftPressTick > 0 && (now - _lastShiftPressTick) <= FLEXIBLE_COMBO_WINDOW_MS);
                            bool altRapid = !_targetAlt || (_lastAltPressTick > 0 && (now - _lastAltPressTick) <= FLEXIBLE_COMBO_WINDOW_MS);
                            bool targetRapid = _lastTargetPressTick > 0 && (now - _lastTargetPressTick) <= FLEXIBLE_COMBO_WINDOW_MS;

                            if (winRapid && ctrlRapid && shiftRapid && altRapid && targetRapid)
                            {
                                shouldTrigger = true;
                            }
                        }
                        else if (isTargetKey)
                        {
                            // Classic strict mode: modifiers must be held down first, and target key pressed last
                            bool winDown = IsKeyDown(NativeMethods.VK_LWIN) ||
                                           IsKeyDown(NativeMethods.VK_RWIN) ||
                                           (_isWinDown && (now - _winDownTimestamp) < 4000);

                            bool ctrlDown = IsKeyDown(NativeMethods.VK_CONTROL) ||
                                            IsKeyDown(NativeMethods.VK_LCONTROL) ||
                                            IsKeyDown(NativeMethods.VK_RCONTROL) ||
                                            (_isCtrlDown && (now - _ctrlDownTimestamp) < 4000);

                            bool shiftDown = IsKeyDown(NativeMethods.VK_SHIFT) ||
                                             IsKeyDown(NativeMethods.VK_LSHIFT) ||
                                             IsKeyDown(NativeMethods.VK_RSHIFT) ||
                                             (_isShiftDown && (now - _shiftDownTimestamp) < 4000);

                            bool altDown = IsKeyDown(NativeMethods.VK_MENU) ||
                                           IsKeyDown(NativeMethods.VK_LMENU) ||
                                           IsKeyDown(NativeMethods.VK_RMENU) ||
                                           (_isAltDown && (now - _altDownTimestamp) < 4000);

                            if (winDown == _targetWin &&
                                ctrlDown == _targetCtrl &&
                                shiftDown == _targetShift &&
                                altDown == _targetAlt)
                            {
                                shouldTrigger = true;
                            }
                        }

                        if (shouldTrigger)
                        {
                            ResetComboTimestamps();
                            _suppressNextKeyUp = true;
                            _suppressedVk = vk;
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
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                if (isWinKey)
                {
                    _isWinDown = false;
                }
                else if (isCtrlKey)
                {
                    _isCtrlDown = false;
                }
                else if (isShiftKey)
                {
                    _isShiftDown = false;
                }
                else if (isAltKey)
                {
                    _isAltDown = false;
                }
                else if (isTargetKey)
                {
                    _isTargetDown = false;
                }

                // Never swallow WM_KEYUP for Win key so the OS raw input state never thinks Win is stuck down
                if (!isWinKey && _suppressNextKeyUp && vk == _suppressedVk)
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
