using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeepShot.Platform;

/// <summary>
/// Own Escape/right-click only for an active screenshot. Consume both halves of each
/// gesture and request cancellation on release, so the background never receives it.
/// </summary>
internal sealed class CaptureInputGuard : IDisposable
{
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _cancel;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _escapeDown;
    private bool _rightDown;

    internal CaptureInputGuard(Func<bool> enabled, Action<string> cancel)
    {
        _enabled = enabled;
        _cancel = cancel;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(13, _keyboardProc, module, 0); // WH_KEYBOARD_LL
        if (_keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        _mouseHook = SetWindowsHookEx(14, _mouseProc, module, 0); // WH_MOUSE_LL
        if (_mouseHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error);
        }
    }

    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x1B)
        {
            var keyMessage = message.ToInt32();
            if (keyMessage is 0x0100 or 0x0104) // KEYDOWN / SYSKEYDOWN
            {
                if (_escapeDown || _enabled())
                {
                    _escapeDown = true;
                    return new IntPtr(1);
                }
            }
            else if ((keyMessage is 0x0101 or 0x0105) && _escapeDown)
            {
                _escapeDown = false;
                _cancel("Esc");
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            if (message.ToInt32() == 0x0204 && (_rightDown || _enabled())) // RBUTTONDOWN
            {
                _rightDown = true;
                return new IntPtr(1);
            }
            if (message.ToInt32() == 0x0205 && _rightDown) // RBUTTONUP
            {
                _rightDown = false;
                _cancel("鼠标右键");
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
}
