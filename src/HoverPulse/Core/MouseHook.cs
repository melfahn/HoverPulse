using System.Runtime.InteropServices;
using HoverPulse.Interop;

namespace HoverPulse.Core;

/// <summary>
/// Installs a global low-level mouse hook (WH_MOUSE_LL) to monitor cursor
/// position system-wide without interfering with mouse input.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private IntPtr _hookHandle;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;

    /// <summary>Raised on every mouse move with the cursor position.</summary>
    public event Action<POINT>? MouseMoved;

    public MouseHook()
    {
        // Store delegate as field to prevent garbage collection
        _hookProc = HookCallback;
    }

    /// <summary>Installs the global mouse hook.</summary>
    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install mouse hook. Error: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>Removes the global mouse hook.</summary>
    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION && wParam == (IntPtr)NativeMethods.WM_MOUSEMOVE)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseMoved?.Invoke(hookStruct.Point);
        }

        // Always pass the event along — never block mouse input
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
    }
}
