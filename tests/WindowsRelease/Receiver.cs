using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

public sealed class ReleaseReceiver : Form
{
    public readonly List<string> Edges = new List<string>();
    public readonly List<string> Trace = new List<string>();
    public readonly List<int> SessionEvents = new List<int>();
    public readonly List<string> DelayedHolds = new List<string>();
    public readonly List<string> DragTimeline = new List<string>();
    public bool TraceDrag;
    readonly Stopwatch clock = Stopwatch.StartNew();
    public ReleaseReceiver()
    {
        Text = "HoverPulse release receiver";
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(80, 250, 340, 240);
    }
    protected override void WndProc(ref Message msg)
    {
        if (msg.Msg == 0x2b1) SessionEvents.Add(msg.WParam.ToInt32());
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
            if (TraceDrag) { MarkDrag("edge " + edge + " leftDown=" + LeftDown()); }
            Trace.Add(String.Format("{0:F3} {1} {2} {3}", clock.Elapsed.TotalMilliseconds, edge,
                (short)msg.LParam.ToInt64(), (short)(msg.LParam.ToInt64() >> 16)));
        }
        base.WndProc(ref msg);
    }

    public void MarkDrag(string phase)
    {
        DragTimeline.Add(Stopwatch.GetTimestamp() + " " + phase);
    }

    public static bool Responds(IntPtr window)
    {
        UIntPtr result;
        return SendMessageTimeoutW(window, 0, UIntPtr.Zero, null, 2, 50, out result) != IntPtr.Zero;
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
        var actual = WindowFromPoint(new Point(x,y));
        if (actual != handle)
        {
            uint owner;
            GetWindowThreadProcessId(actual, out owner);
            throw new InvalidOperationException(String.Format(
                "Target at {0},{1} is occluded. Expected HWND {2}, found {3} (PID {4}).", x, y, handle, actual, owner));
        }
        Trace.Add(String.Format("{0:F3} sample-target {1} {2} {3}", clock.Elapsed.TotalMilliseconds,handle,x,y));
        double start = clock.Elapsed.TotalMilliseconds;
        if (!SetCursorPos(x,y)) throw new Exception("Failed to position test pointer");
        Point physical;
        if (!GetPhysicalCursorPos(out physical) || physical.X != x || physical.Y != y)
            throw new InvalidOperationException("Test pointer differs from the requested physical screen point.");
        if (TraceDrag) { MarkDrag(String.Format("placed x={0} y={1} hwnd={2}", x, y, handle)); }
        Pump(milliseconds);
        double elapsed = clock.Elapsed.TotalMilliseconds - start;
        if (elapsed - milliseconds >= 250)
        {
            DelayedHolds.Add(String.Format("at {0:F3} ms, target {1},{2}, requested {3} ms, observed {4:F3} ms",
                start,x,y,milliseconds,elapsed));
        }
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

    public static string[] VisibleLabels(IntPtr window)
    {
        var labels = new List<string>();
        EnumChildWindows(window, delegate(IntPtr child, IntPtr state)
        {
            if (IsWindowVisible(child)) labels.Add(ReadText(child));
            return true;
        }, IntPtr.Zero);
        return labels.ToArray();
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

    public static Point CursorForWindow(IntPtr window)
    {
        var previous = SetThreadDpiAwarenessContext(window == IntPtr.Zero ? new IntPtr(-1) : GetWindowDpiAwarenessContext(window));
        if (previous == IntPtr.Zero) throw new InvalidOperationException("Could not match the toolbar DPI context.");
        try
        {
            Point point;
            if (!GetCursorPos(out point)) throw new InvalidOperationException("Cursor readback failed.");
            return point;
        }
        finally { RestoreDpi(previous); }
    }

    public void HoldReferencePoint(IntPtr handle, int x, int y, int milliseconds)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-1));
        if (previous == IntPtr.Zero) throw new InvalidOperationException("Reference coordinate context unavailable.");
        Point logical;
        try
        {
            if (WindowFromPoint(new Point(x,y)) != handle || !SetCursorPos(x,y) ||
                !GetCursorPos(out logical) || logical.X != x || logical.Y != y)
                throw new InvalidOperationException("Reference-space target or cursor readback differs.");
        }
        finally { RestoreDpi(previous); }
        Point physical;
        if (!GetPhysicalCursorPos(out physical)) throw new InvalidOperationException("Physical target readback failed.");
        HoldScreenPoint(handle,physical.X,physical.Y,milliseconds);
    }

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

    public static string IntegritySid(Process process)
    {
        Microsoft.Win32.SafeHandles.SafeFileHandle token;
        if (!OpenProcessToken(process.Handle,8,out token)) throw new System.ComponentModel.Win32Exception();
        using (token) {
            int bytes;
            GetTokenInformation(token,25,IntPtr.Zero,0,out bytes);
            if (bytes <= 0) throw new System.ComponentModel.Win32Exception();
            IntPtr data = Marshal.AllocHGlobal(bytes);
            try {
                if (!GetTokenInformation(token,25,data,bytes,out bytes)) throw new System.ComponentModel.Win32Exception();
                return new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value;
            } finally { Marshal.FreeHGlobal(data); }
        }
    }

    public static void Foreground(IntPtr window)
    {
        if (GetForegroundWindow() != window) SetForegroundWindow(window);
        Pump(200);
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Test receiver is not the foreground window.");
    }

    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out Microsoft.Win32.SafeHandles.SafeFileHandle token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeFileHandle token,int info,IntPtr buffer,int length,out int needed);

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
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
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
    [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Point point);
}
