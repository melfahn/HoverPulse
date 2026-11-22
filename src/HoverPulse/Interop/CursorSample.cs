namespace HoverPulse.Interop;

internal readonly record struct CursorSample(POINT Screen, POINT Dwell, bool Stable = true)
{
    internal static CursorSample? Read()
    {
        // The reference samples GetCursorPos in a DPI-unaware window. Windows
        // performs its own coordinate rounding, including across monitor origins.
        if (!NativeMethods.GetCursorPos(out var before)) { return null; }
        var previous = NativeMethods.SetThreadDpiAwarenessContext(-1);
        if (previous == 0) { return null; }
        POINT dwell;
        try
        {
            if (!NativeMethods.GetCursorPos(out dwell)) { return null; }
        }
        finally { NativeMethods.SetThreadDpiAwarenessContext(previous); }
        // Read both hit-test positions on the toolbar's per-monitor-aware thread.
        // A moving sample between these API calls cannot accumulate stationary time.
        if (!NativeMethods.GetCursorPos(out var screen)) { return null; }
        return new(screen, dwell, before.X == screen.X && before.Y == screen.Y);
    }
}
