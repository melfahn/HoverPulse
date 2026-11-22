using HoverPulse.Services;
using HoverPulse.UI;

namespace HoverPulse;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(true, @"Local\HoverPulse_SingleInstance", out var first);
        if (!first)
        {
            return 1;
        }

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            var store = SettingsService.ForUser();
            using var toolbar = new ToolbarWindow(store, store.Load());
            Application.Run(toolbar);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "HoverPulse could not continue", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }
}
