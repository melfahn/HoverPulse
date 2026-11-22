using System.Runtime.InteropServices;
using HoverPulse.Interop;

namespace HoverPulse.Core;

internal enum ClickType { LeftClick, RightClick, DoubleClick, DragStart, DragEnd }

internal sealed class ClickSimulator(Func<uint, bool, bool> emit, Action<int> wait,
    Func<uint, bool, ValueTask<bool>>? dragEdge = null)
{
    private bool _busy;
    private uint _ownedUp;
    private bool _ownedBackend;
    private bool _ownedDrag;
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

    public async ValueTask<bool> DispatchAsync(ClickType action, bool alternate)
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
                return await ReleaseAsync();
            }

            uint down = action == ClickType.RightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
            int pairs = action == ClickType.DoubleClick ? 2 : 1;
            for (int i = 0; i < pairs; i++)
            {
                bool isDrag = action == ClickType.DragStart;
                bool sent = isDrag && dragEdge != null ? await dragEdge(down, alternate) : emit(down, alternate);
                if (!sent)
                {
                    return false;
                }

                _ownedUp = down << 1;
                _ownedBackend = alternate;
                _ownedDrag = isDrag;
                if (action == ClickType.DragStart)
                {
                    return true;
                }

                wait(alternate ? 100 : 0);
                if (!ReleaseLocalEdge())
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

    public async ValueTask<bool> ReleaseAsync()
    {
        if (!_ownedDrag || dragEdge == null)
        {
            return ReleaseLocalEdge();
        }

        if (Holding && !await dragEdge(_ownedUp, _ownedBackend))
        {
            return false;
        }

        _ownedUp = 0;
        return true;
    }

    internal bool ReleaseLocalEdge()
    {
        if (Holding && ((_ownedDrag && dragEdge != null) || !emit(_ownedUp, _ownedBackend)))
        {
            return false;
        }

        _ownedUp = 0;
        return true;
    }
}
