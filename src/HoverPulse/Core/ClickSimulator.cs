using System.Runtime.InteropServices;
using HoverPulse.Interop;

namespace HoverPulse.Core;

internal enum ClickType { LeftClick, RightClick, DoubleClick, DragStart, DragEnd }

internal sealed class ClickSimulator(Func<uint, bool, bool> emit, Action<int> wait)
{
    private bool _busy;
    private uint _ownedUp;
    private bool _ownedBackend;
    public bool Holding => _ownedUp != 0;

    public static bool NativeEdge(uint flag, bool alternate)
    {
        if (!alternate)
        {
            NativeMethods.mouse_event(flag, 0, 0, 0, 0);
            return true; // Legacy API has no delivery receipt.
        }
        var input = new INPUT { MouseInput = new MOUSEINPUT { Flags = flag } };
        return NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }

    public bool Dispatch(ClickType action, bool alternate)
    {
        if (_busy || (Holding && action != ClickType.DragEnd) || (!Holding && action == ClickType.DragEnd))
        {
            return false;
        }

        _busy = true;
        try
        {
            if (action == ClickType.DragEnd)
            {
                return Release();
            }

            uint down = action == ClickType.RightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
            int pairs = action == ClickType.DoubleClick ? 2 : 1;
            for (int i = 0; i < pairs; i++)
            {
                if (!emit(down, alternate))
                {
                    return false;
                }

                _ownedUp = down << 1;
                _ownedBackend = alternate;
                if (action == ClickType.DragStart)
                {
                    return true;
                }

                wait(alternate ? 100 : 0);
                if (!Release())
                {
                    return false;
                }

                if (i + 1 < pairs)
                {
                    wait(alternate ? 100 : 0);
                }
            }
            return true;
        }
        finally { _busy = false; }
    }

    public bool Release()
    {
        if (!Holding)
        {
            return true;
        }

        if (!emit(_ownedUp, _ownedBackend))
        {
            return false;
        }

        _ownedUp = 0;
        return true;
    }
}
