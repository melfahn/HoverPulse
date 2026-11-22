using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using HoverPulse.Interop;

namespace HoverPulse.Core;

// The process that sends drag-down also sends drag-up. A closed command pipe
// wakes it even when Windows terminates the toolbar without running finally.
internal sealed class DragWorker : IDisposable
{
    internal const string Argument = "--drag-worker";
    private const byte LegacyDown = 1, AlternateDown = 2, Up = 3;
    private Process? _process;

    internal bool Prepare()
    {
        if (_process is { HasExited: false })
        {
            return true;
        }

        Dispose();
        if (Environment.ProcessPath is not string path)
        {
            return false;
        }

        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HoverPulse.dll"));
        }
        start.ArgumentList.Add(Argument);
        try { _process = Process.Start(start); }
        catch (Win32Exception) { return false; }
        if (DragTrace.Enabled && _process != null) { DragTrace.Write($"worker-started pid={_process.Id}"); }
        return _process != null;
    }

    internal bool Send(uint flag, bool alternate)
    {
        bool up = flag == NativeMethods.MOUSEEVENTF_LEFTUP;
        if (!up && flag != NativeMethods.MOUSEEVENTF_LEFTDOWN)
        {
            return false;
        }

        if (up && (_process == null || _process.HasExited))
        {
            // The toolbar only requests up after an acknowledged down.
            DragTrace.Write("release-fallback worker-unavailable");
            return ReleaseIfHeld(alternate);
        }
        if (!up && !Prepare())
        {
            return false;
        }

        try
        {
            if (DragTrace.Enabled) { DragTrace.Write($"command-write up={up} alternate={alternate}"); }
            var commands = _process!.StandardInput.BaseStream;
            commands.WriteByte(up ? Up : alternate ? AlternateDown : LegacyDown);
            commands.Flush();
            DragTrace.Write("reply-wait");
            byte[] ack = new byte[1];
            int read = _process.StandardOutput.BaseStream.ReadAsync(ack.AsMemory()).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (read == 1)
            {
                if (DragTrace.Enabled) { DragTrace.Write($"reply-read accepted={ack[0] == 1}"); }
                return ack[0] == 1;
            }
            DragTrace.Write("reply-eof");
        }
        catch (IOException) { DragTrace.Write("command-io-error"); }
        catch (TimeoutException) { DragTrace.Write("reply-timeout"); }
        Dispose(); // EOF requests release if the worker accepted down before losing its acknowledgment.
        return false;
    }

    private static bool ReleaseIfHeld(bool alternate) =>
        (NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0 ||
        ClickSimulator.NativeEdge(NativeMethods.MOUSEEVENTF_LEFTUP, alternate);

    internal static int Run(Stream commands, Stream replies)
    {
        bool held = false, alternate = false;
        try
        {
            DragTrace.Write("worker-ready");
            int command;
            while ((command = commands.ReadByte()) >= 0)
            {
                if (DragTrace.Enabled) { DragTrace.Write($"command-read code={command} held={held}"); }
                bool accepted;
                if (command == Up)
                {
                    accepted = !held || Emit(NativeMethods.MOUSEEVENTF_LEFTUP, alternate);
                    if (accepted)
                    {
                        held = false;
                    }
                }
                else if (!held && command is LegacyDown or AlternateDown)
                {
                    alternate = command == AlternateDown;
                    held = Emit(NativeMethods.MOUSEEVENTF_LEFTDOWN, alternate);
                    accepted = held;
                }
                else { return 2; }
                replies.WriteByte(accepted ? (byte)1 : (byte)0);
                replies.Flush();
                DragTrace.Write("reply-written");
            }
            DragTrace.Write("command-eof");
        }
        catch (IOException) { DragTrace.Write("worker-io-error"); }
        finally
        {
            if (held)
            {
                DragTrace.Write("worker-cleanup-release");
                Emit(NativeMethods.MOUSEEVENTF_LEFTUP, alternate);
            }
        }
        return 0;
    }

    private static bool Emit(uint flag, bool alternate)
    {
        if (DragTrace.Enabled) { DragTrace.Write($"native-enter flag={flag} alternate={alternate}"); }
        bool accepted = ClickSimulator.NativeEdge(flag, alternate);
        if (DragTrace.Enabled)
        {
            bool leftDown = (NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0;
            DragTrace.Write($"native-return flag={flag} accepted={accepted} leftDown={leftDown}");
        }
        return accepted;
    }

    public void Dispose()
    {
        if (_process == null)
        {
            return;
        }

        try { _process.StandardInput.Close(); }
        catch (IOException) { /* The worker already closed its end of the pipe. */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
