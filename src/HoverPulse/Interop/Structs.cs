using System.Runtime.InteropServices;

namespace HoverPulse.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }

    public readonly double DistanceTo(POINT other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int CX;
    public int CY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr HWnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Point;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT Point;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public MOUSEINPUT MouseInput;

    internal const uint INPUT_MOUSE = 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int DX;
    public int DY;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;

    internal const byte AC_SRC_OVER = 0x00;
    internal const byte AC_SRC_ALPHA = 0x01;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public uint CbSize;
    public uint Style;
    public IntPtr LpfnWndProc;
    public int CbClsExtra;
    public int CbWndExtra;
    public IntPtr HInstance;
    public IntPtr HIcon;
    public IntPtr HCursor;
    public IntPtr HbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? LpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string LpszClassName;
    public IntPtr HIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PAINTSTRUCT
{
    public IntPtr Hdc;
    [MarshalAs(UnmanagedType.Bool)]
    public bool FErase;
    public RECT RcPaint;
    [MarshalAs(UnmanagedType.Bool)]
    public bool FRestore;
    [MarshalAs(UnmanagedType.Bool)]
    public bool FIncUpdate;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] RgbReserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public uint CbSize;
    public IntPtr HWnd;
    public uint UID;
    public uint UFlags;
    public uint UCallbackMessage;
    public IntPtr HIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string SzTip;
    public uint DwState;
    public uint DwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string SzInfo;
    public uint UTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string SzInfoTitle;
    public uint DwInfoFlags;
    public Guid GuidItem;
    public IntPtr HBalloonIcon;
}
