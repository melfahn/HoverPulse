namespace HoverPulse.Interop;

internal readonly record struct CursorSample(POINT Screen, POINT Dwell, bool Stable = true)
{
    internal static CursorSample? Read()
    {
        // The reference samples GetCursorPos in a DPI-unaware window. Windows
        // performs its own coordinate rounding, including across monitor origins.
        var previous = NativeMethods.SetThreadDpiAwarenessContext(-1);
        if (previous == 0) { return null; }
        try
        {
            if (!NativeMethods.GetCursorPos(out var before) ||
                !NativeMethods.GetPhysicalCursorPos(out var screen) ||
                !NativeMethods.GetCursorPos(out var after)) { return null; }

            // Keep the physical hit and logical dwell paired when the head moves
            // between API calls. A moving sample cannot accumulate stationary time.
            return new(screen, after, before.X == after.X && before.Y == after.Y);
        }
        finally { NativeMethods.SetThreadDpiAwarenessContext(previous); }
    }
}
