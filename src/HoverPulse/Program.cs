using HoverPulse.Core;
using HoverPulse.Interop;
using HoverPulse.Services;
using HoverPulse.UI;

namespace HoverPulse;

/// <summary>
/// Application entry point. Initializes all components and runs the Win32 message loop.
/// </summary>
internal static class Program
{
    private const nuint TimerId = 1;
    private const uint TimerIntervalMs = 50;

    private static DwellDetector? _dwellDetector;
    private static MouseHook? _mouseHook;
    private static OverlayWindow? _overlay;
    private static ToolbarWindow? _toolbar;
    private static AppSettings? _settings;

    [STAThread]
    private static int Main(string[] args)
    {
        // Prevent multiple instances
        using var mutex = new Mutex(true, "Global\\HoverPulse_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return 1;
        }

        // Load settings
        _settings = SettingsService.Load();

        // Create core components
        _dwellDetector = new DwellDetector
        {
            DwellTimeMs = _settings.DwellTimeMs,
            ToleranceRadius = _settings.ToleranceRadius
        };
        _mouseHook = new MouseHook();

        // Create UI
        var hInstance = NativeMethods.GetModuleHandle(null);

        _overlay = new OverlayWindow();
        _overlay.SetColor(_settings.IndicatorColor);
        _overlay.Create(hInstance);

        _toolbar = new ToolbarWindow();
        var opacity = (byte)Math.Clamp(_settings.ToolbarOpacity * 255, 0, 255);
        _toolbar.Create(hInstance, _settings.ToolbarX, _settings.ToolbarY,
            _settings.ToolbarHorizontal, opacity);

        // Wire events
        _mouseHook.MouseMoved += OnMouseMoved;
        _dwellDetector.DwellProgress += OnDwellProgress;
        _dwellDetector.DwellCompleted += OnDwellCompleted;
        _dwellDetector.DwellCancelled += OnDwellCancelled;
        _toolbar.PauseToggled += OnPauseToggled;

        // Install mouse hook
        _mouseHook.Install();

        // Start dwell tick timer on the toolbar window (it has a message pump)
        NativeMethods.SetTimer(_toolbar.Handle, TimerId, TimerIntervalMs, IntPtr.Zero);

        // Run message loop
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            // Handle WM_TIMER for dwell ticking
            if (msg.Message == NativeMethods.WM_TIMER && msg.WParam == (IntPtr)TimerId)
            {
                _dwellDetector.Tick();
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        // Cleanup
        NativeMethods.KillTimer(_toolbar.Handle, TimerId);
        SaveToolbarPosition();
        _mouseHook.Dispose();
        _dwellDetector.Dispose();
        _overlay.Dispose();
        _toolbar.Dispose();

        return 0;
    }

    private static void OnMouseMoved(POINT position)
    {
        _dwellDetector?.OnCursorMoved(position);
    }

    private static void OnDwellProgress(POINT position, double progress)
    {
        if (_settings?.ShowIndicator == true)
        {
            _overlay?.UpdateProgress(position, progress);
        }
    }

    private static void OnDwellCompleted(POINT position)
    {
        _overlay?.Hide();

        if (_toolbar is not null)
        {
            var clickType = _toolbar.ActiveClickType;
            ClickSimulator.SimulateClick(clickType);

            // If drag started, switch to drag-end for next dwell
            if (clickType == ClickType.DragStart)
            {
                _toolbar.SetActiveClickType(ClickType.DragEnd);
            }
            else if (clickType == ClickType.DragEnd)
            {
                _toolbar.SetActiveClickType(ClickType.LeftClick);
            }
        }
    }

    private static void OnDwellCancelled()
    {
        _overlay?.Hide();
    }

    private static void OnPauseToggled(bool isPaused)
    {
        if (_dwellDetector is not null)
        {
            _dwellDetector.IsPaused = isPaused;
        }
    }

    private static void SaveToolbarPosition()
    {
        if (_settings is not null && _toolbar is not null)
        {
            _settings.ToolbarX = _toolbar.X;
            _settings.ToolbarY = _toolbar.Y;
            SettingsService.Save(_settings);
        }
    }
}
