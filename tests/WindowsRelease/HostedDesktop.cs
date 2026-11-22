using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// The hosted release receiver needs the input desktop for real pointer delivery.
// Keep runner consoles and shell windows out of its hit tests.
public static class HostedDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int size;
        public string reserved, desktop, title;
        public int x, y, width, height, columns, rows, attributes, flags;
        public short showWindow, reservedSize;
        public IntPtr reservedBytes, input, output, error;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process, thread; public int processId, threadId; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserObjectInformationW(IntPtr obj, int index, StringBuilder result, int bytes, out int needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr handle, out int code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static string Name(IntPtr desktop)
    {
        var result = new StringBuilder(256);
        int needed;
        if (!GetUserObjectInformationW(desktop, 2, result, result.Capacity * 2, out needed)) throw new Win32Exception();
        return result.ToString();
    }

    public static int Run(string app, string args, string directory)
    {
        if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Process.GetCurrentProcess().SessionId == 0 || !Environment.UserInteractive)
            throw new InvalidOperationException("A disposable interactive GitHub-hosted runner is required.");
        const uint readAndSwitch = 0x101;
        IntPtr previous = OpenInputDesktop(0, false, readAndSwitch);
        if (previous == IntPtr.Zero) throw new Win32Exception();
        string name = "HoverClickTest" + Guid.NewGuid().ToString("N");
        IntPtr desktop = IntPtr.Zero;
        try
        {
            if (!String.Equals(Name(previous), "Default", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The runner is not on its ordinary desktop.");
            desktop = CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, 0x1ff, IntPtr.Zero);
            if (desktop == IntPtr.Zero || !SwitchDesktop(desktop)) throw new Win32Exception();
            var startup = new StartupInfo { size = Marshal.SizeOf(typeof(StartupInfo)), desktop = @"WinSta0\" + name };
            var command = new StringBuilder("\"" + app + "\" " + args);
            ProcessInfo child;
            if (!CreateProcessW(app, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000,
                IntPtr.Zero, directory, ref startup, out child)) throw new Win32Exception();
            CloseHandle(child.thread);
            try
            {
                if (WaitForSingleObject(child.process, 0xffffffff) != 0) throw new Win32Exception();
                int code;
                if (!GetExitCodeProcess(child.process, out code)) throw new Win32Exception();
                return code;
            }
            finally { CloseHandle(child.process); }
        }
        finally
        {
            try
            {
                if (desktop != IntPtr.Zero)
                {
                    IntPtr current = OpenInputDesktop(0, false, 1);
                    if (current == IntPtr.Zero) throw new Win32Exception();
                    try
                    {
                        if (Name(current) == name && !SwitchDesktop(previous)) throw new Win32Exception();
                    }
                    finally { CloseDesktop(current); }
                }
            }
            finally
            {
                if (desktop != IntPtr.Zero) CloseDesktop(desktop);
                CloseDesktop(previous);
            }
        }
    }
}
