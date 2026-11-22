using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace HoverPulse.Core;

internal static class DragTrace
{
    internal const string DirectoryVariable = "HOVERPULSE_DRAG_TRACE_DIRECTORY";
    private static readonly string? Log = Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } directory
        ? Path.Combine(directory, $"drag-{Environment.ProcessId}.log") : null;

    internal static bool Enabled => Log != null;

    internal static void Write(string phase)
    {
        if (Log == null) { return; }
        try
        {
            File.AppendAllText(Log, string.Create(CultureInfo.InvariantCulture,
                $"{Stopwatch.GetTimestamp()} {Environment.CurrentManagedThreadId} {phase}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Optional diagnostics must never interrupt a held drag.
        }
    }
}
