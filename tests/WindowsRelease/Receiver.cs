using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public sealed class ReleaseReceiver : Form
{
    public readonly List<string> Edges = new List<string>();
    public readonly List<string> Trace = new List<string>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    public ReleaseReceiver()
    {
        Text = "HoverPulse release receiver";
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(80, 250, 340, 240);
    }
    protected override void WndProc(ref Message msg)
    {
        string edge = null;
        switch (msg.Msg)
        {
            case 0x201: case 0x203: edge = "LD"; break;
            case 0x202: edge = "LU"; break;
            case 0x204: case 0x206: edge = "RD"; break;
            case 0x205: edge = "RU"; break;
        }
        if (edge != null)
        {
            Edges.Add(edge);
            Trace.Add(String.Format("{0:F3} {1} {2} {3}", clock.Elapsed.TotalMilliseconds, edge,
                (short)msg.LParam.ToInt64(), (short)(msg.LParam.ToInt64() >> 16)));
        }
        base.WndProc(ref msg);
    }

    public void Hold(IntPtr handle, int offsetX, int milliseconds)
    {
        HoldAt(handle, offsetX, 0, milliseconds);
    }

    public void HoldAt(IntPtr handle, int offsetX, int offsetY, int milliseconds)
    {
        RECT rect;
        if (!IsWindowVisible(handle) || !GetWindowRect(handle, out rect)) throw new Exception("Missing target");
        int x = (rect.l + rect.r) / 2 + offsetX, y = (rect.t + rect.b) / 2 + offsetY;
        HoldScreenPoint(handle, x, y, milliseconds);
    }

    public void HoldScreenPoint(IntPtr handle, int x, int y, int milliseconds)
    {
        if (WindowFromPoint(new Point(x,y)) != handle) throw new Exception("Target is occluded");
        Trace.Add(String.Format("{0:F3} sample-target {1} {2} {3}", clock.Elapsed.TotalMilliseconds,handle,x,y));
        if (!SetCursorPos(x,y)) throw new Exception("Failed to position test pointer");
        Pump(milliseconds);
    }

    public static void Pump(int ms)
    {
        var clock = Stopwatch.StartNew();
        do { Application.DoEvents(); System.Threading.Thread.Sleep(5); } while(clock.ElapsedMilliseconds < ms);
    }

    public static IntPtr Button(IntPtr window,string prefix,int id,bool reference)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(window, delegate(IntPtr child, IntPtr state)
        {
            if (!IsWindowVisible(child)) return true;
            bool matches = GetDlgCtrlID(child) == id;
            if (!reference)
            {
                // The toolbar belongs to another process. Ask the child control for its text.
                var label = new StringBuilder(160);
                UIntPtr copied;
                matches = SendMessageTimeoutW(child, 0x000D, (UIntPtr)label.Capacity,
                    label, 2, 250, out copied) != IntPtr.Zero && copied != UIntPtr.Zero &&
                    label.ToString().StartsWith(prefix, StringComparison.Ordinal);
            }
            if (matches)
            {
                found = child;
                return false;
            }
            return true;
        },IntPtr.Zero);
        if (found == IntPtr.Zero) throw new Exception("Missing toolbar control " + prefix);
        return found;
    }

    public static string ReadText(IntPtr window)
    {
        var text = new StringBuilder(200);
        UIntPtr copied;
        if (SendMessageTimeoutW(window, 0x000D, (UIntPtr)text.Capacity, text, 2, 250, out copied) == IntPtr.Zero)
            throw new InvalidOperationException("Control text unavailable");
        return text.ToString();
    }

    public static IntPtr CandidateToolbar(int pid)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(64);
            GetWindowTextW(window, title, title.Capacity);
            string caption = title.ToString();
            if (caption != "Hover Click" && caption != "HoverPulse") return true;
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static void CloseToolbar(IntPtr window, int pid)
    {
        uint owner;
        GetWindowThreadProcessId(window, out owner);
        if (owner == pid) PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }

    public static IntPtr EnterPerMonitorDpi()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) throw new InvalidOperationException("Per-monitor DPI context unavailable.");
        return previous;
    }

    public static void RestoreDpi(IntPtr previous) { SetThreadDpiAwarenessContext(previous); }

    public static Rectangle ScreenBounds(IntPtr window)
    {
        RECT r;
        if (!IsWindowVisible(window) || !GetWindowRect(window, out r)) throw new InvalidOperationException("Missing window bounds.");
        return Rectangle.FromLTRB(r.l,r.t,r.r,r.b);
    }

    public static void CheckHit(IntPtr window, int x, int y)
    {
        if (WindowFromPoint(new Point(x,y)) != window) throw new InvalidOperationException("Unexpected window at " + x + "," + y);
    }

    public static Bitmap CaptureWindow(IntPtr window)
    {
        var bounds = ScreenBounds(window);
        var bitmap = new Bitmap(bounds.Width,bounds.Height);
        using (var g=Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
        }
        return bitmap;
    }

    public static bool LeftDown() { return (GetAsyncKeyState(1) & 0x8000) != 0; }

    public void ReleaseTestDrag()
    {
        if (WindowFromPoint(Cursor.Position) != Handle) throw new InvalidOperationException("Recovery target is occluded.");
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Pump(200);
        if (LeftDown()) throw new InvalidOperationException("The test button is still held.");
    }

    public static void KeepAwake(bool enable)
    {
        uint flags = enable ? 0x80000003u : 0x80000000u;
        if (SetThreadExecutionState(flags) == 0) throw new InvalidOperationException("Could not set the test power request.");
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l,t,r,b; }
    delegate bool Enumerate(IntPtr hwnd,IntPtr state);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr w, StringBuilder text, int capacity);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr w, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr window,Enumerate callback,IntPtr state);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(
        IntPtr window, uint message, UIntPtr capacity, [Out] StringBuilder text,
        uint flags, uint timeout, out UIntPtr copied);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
}
