using System.Runtime.InteropServices;

namespace HoverPulse.Interop;

internal sealed class Companion
{
    private const int Marker = 0x0BADC0DE;
    private IntPtr _window;
    internal void Discover()
    {
        if (_window != 0 && GetWindowLongW(_window, -21) == Marker)
        {
            return;
        }

        _window = 0;
        EnumWindows((window, _) =>
        {
            if (GetWindowLongW(window, -21) != Marker)
            {
                return true;
            }

            _window = window;
            return false;
        }, 0);
        Post(0x8001, 1);
    }
    internal void Acknowledge() => Post(0x8004, 1);
    internal void Quit() => Post(0x8002, 0);
    private void Post(uint msg, int command)
    {
        if (_window != 0 && GetWindowLongW(_window, -21) == Marker)
        {
            NativeMethods.PostMessage(_window, msg, command, 0);
        }
    }
    private delegate bool Enumerate(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(IntPtr hwnd, int index);
}

internal static partial class NativeMethods
{
    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSUnRegisterSessionNotification(IntPtr window);
}
