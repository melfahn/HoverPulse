using System.ComponentModel;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using HoverPulse.Core;
using HoverPulse.Interop;
using HoverPulse.Services;

namespace HoverPulse.UI;

internal sealed class ToolbarWindow : Form
{
    private readonly SettingsService _store;
    private AppSettings _settings;
    private AppSettings? _draft;
    private readonly DwellDetector _detector = new();
    private readonly ActionSelection _selection = new();
    private readonly ClickSimulator _clicks;
    private readonly DragWorker? _dragWorker;
    private readonly Func<CursorSample?> _cursor;
    private ToolbarPalette _palette = ToolbarPalette.For(ToolbarTheme.Neutral);
    private ToolbarButton? _feedback;
    private string? _lastToolbarTarget;
    private Rectangle _divider;
    private readonly ToolbarButton _toggle = new();
    private readonly Dictionary<Button, Action> _commands = [];
    private OverlayWindow? _overlay;
    private Point? _moveOffset;
    private bool _processing, _confirmQuit, _running, _lockedSession;
    private string? _fault;
    private bool _saveFailed;
    private int _ticks;
    private bool _deferredClose;
    private readonly Companion _companion = new();

    public ToolbarWindow(SettingsService store, AppSettings settings, ClickSimulator? clicks = null, Func<CursorSample?>? cursor = null)
    {
        _store = store;
        if (clicks == null)
        {
            _dragWorker = new DragWorker();
            clicks = new ClickSimulator(ClickSimulator.NativeEdge, BetweenEdges, _dragWorker.Send);
        }
        _clicks = clicks;
        _cursor = cursor ?? CursorSample.Read;
        _settings = settings;
        _selection.Restore(settings.DefaultAction);
        Text = "Hover Click";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(35, 38, 42);
        Location = new Point(settings.ToolbarX, settings.ToolbarY);
        ApplySettings();
        LayoutButtons();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            return cp;
        }
    }

    private void Add(string label, Action action, Glyph icon = Glyph.None, bool locked = false,
        bool queued = false, bool held = false, ToolbarButton? existing = null)
    {
        var button = existing ?? new ToolbarButton();
        button.Text = button.AccessibleName = label;
        button.AccessibleDescription = held ? "Holding drag" : locked && queued ? "Locked, selected for next click" :
            locked ? "Locked action" : queued ? "Next click" : icon == Glyph.Resume ? _fault : null;
        button.Icon = icon;
        button.ShowLabel = _settings.ShowToolbarLabels;
        button.Locked = locked;
        button.Queued = queued;
        button.Held = held;
        button.Faulted = (icon == Glyph.Resume && _fault != null) || (icon == Glyph.Setup && _saveFailed);
        button.Palette = _palette;
        if (existing == null)
        {
            button.Click += (_, _) => ActivateButton(button);
        }
        _commands.Add(button, action);
        Controls.Add(button);
    }

    private void ActivateButton(Button button)
    {
        if (_processing || !_commands.TryGetValue(button, out var command))
        {
            return;
        }

        _processing = true;
        try { command(); }
        finally
        {
            _processing = false;
            if (_deferredClose && !IsDisposed)
            {
                _deferredClose = false;
                BeginInvoke(Close);
            }
        }
    }

    private void LayoutButtons()
    {
        SuspendLayout();
        SetFeedback(null, 0);
        foreach (var control in Controls.Cast<Control>().ToArray())
        {
            Controls.Remove(control);
            if (control != _toggle)
            {
                control.Dispose();
            }
        }
        _commands.Clear();
        if (_draft is not null)
        {
            Options();
        }
        else if (_confirmQuit)
        {
            Add("Exit Hover Click", Close);
            Add("Cancel", () => { _confirmQuit = false; LayoutButtons(); });
        }
        else
        {
            Add(_detector.Paused ? "Resume" : "Pause", () => Toggle(false),
                _detector.Paused ? Glyph.Resume : Glyph.Pause, existing: _toggle);
            if (!_detector.Paused)
            {
                foreach (var (label, mode, icon) in new[] { ("Left", ClickType.LeftClick, Glyph.Left),
                    ("Double", ClickType.DoubleClick, Glyph.Double), ("Drag", ClickType.DragStart, Glyph.Drag),
                    ("Right", ClickType.RightClick, Glyph.Right) })
                {
                    Add(label, () => { _selection.Select(mode); _settings.DefaultAction = _selection.Locked; LayoutButtons(); },
                        icon, _selection.Locked == mode, _selection.Temporary == mode,
                        mode == ClickType.DragStart && _selection.Dragging);
                }
                Add("Setup", OpenOptions, Glyph.Setup);
                Add("Exit", () => { _confirmQuit = true; LayoutButtons(); }, Glyph.Exit);
                Add(_moveOffset.HasValue ? "Place" : "Move", StartMove, _moveOffset.HasValue ? Glyph.Place : Glyph.Move);
            }
        }
        ArrangeButtons();
        ResumeLayout(false);
        Invalidate(true);
    }

    private void ArrangeButtons()
    {
        var area = Screen.FromPoint(Location).WorkingArea;
        bool menu = _draft != null || _confirmQuit;
        var layout = ToolbarLayout.Measure(_settings, DeviceDpi, area.Size, Controls.Count, menu);
        for (int i = 0; i < Controls.Count; i++)
        {
            Controls[i].Bounds = layout.Targets[i];
        }
        ClientSize = layout.Shell;
        _divider = Rectangle.Empty;
        if (!menu && Controls.Count >= 6)
        {
            var previous = layout.Targets[4];
            var next = layout.Targets[5];
            int thickness = Math.Max(1, ToolbarLayout.Pixels(1,DeviceDpi));
            if (previous.Top == next.Top)
            {
                _divider = new((previous.Right+next.Left)/2, next.Top+next.Height/8, thickness, next.Height*3/4);
            }
            else if (previous.Left == next.Left)
            {
                _divider = new(next.Left+next.Width/8, (previous.Bottom+next.Top)/2, next.Width*3/4, thickness);
            }
        }
        using var path = ToolbarButton.Corners(ClientRectangle, ToolbarLayout.Pixels(5,DeviceDpi));
        var prior = Region;
        Region = new Region(path);
        prior?.Dispose();
        KeepReachable();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float stroke = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var border = new Pen(_palette.ShellBorder,stroke);
        using var path = ToolbarButton.Corners(new(stroke/2,stroke/2,ClientSize.Width-stroke,ClientSize.Height-stroke),5*stroke);
        e.Graphics.DrawPath(border,path);
        using var separator = new SolidBrush(_palette.Border);
        e.Graphics.FillRectangle(separator,_divider);
    }

    private void SetFeedback(ToolbarButton? button, double progress)
    {
        if (_feedback != button && _feedback != null) { _feedback.Progress = 0; }
        _feedback = button;
        if (button != null) { button.Progress = progress; }
    }

    private void OpenOptions()
    {
        _draft = _settings.Copy();
        _draft.ToolbarX = Left;
        _draft.ToolbarY = Top;
        LayoutButtons();
    }

    private void Options()
    {
        var draft = _draft!;
        void Change(Action action) { action(); LayoutButtons(); }
        Add($"Time − {draft.ClickTime / 10.0:0.0}s", () => Change(() => draft.ClickTime = Math.Max(1, draft.ClickTime - 1)));
        Add("Time +", () => Change(() => draft.ClickTime = Math.Min(50, draft.ClickTime + 1)));
        Add($"Motion − {draft.MoveLimit}px", () => Change(() => draft.MoveLimit = Math.Max(2, draft.MoveLimit - 1)));
        Add("Motion +", () => Change(() => draft.MoveLimit = Math.Min(20, draft.MoveLimit + 1)));
        Add($"Size · {TargetSize.For(draft.AppSize).Name}", () => Change(() => draft.AppSize = (draft.AppSize + 1) % 4));
        string theme = draft.Theme switch { ToolbarTheme.Neutral => "Black and gray", ToolbarTheme.Blue => "Blue dark", _ => "Light" };
        Add($"Theme · {theme}", () => Change(() => draft.Theme = (ToolbarTheme)(((int)draft.Theme + 1) % 3)));
        Add(draft.ShowToolbarLabels ? "Labels on" : "Labels off", () => Change(() => draft.ShowToolbarLabels = !draft.ShowToolbarLabels));
        Add($"Padding −  {draft.ToolbarPadding} DIP", () => Change(() => draft.ToolbarPadding = Math.Max(0,draft.ToolbarPadding-1)));
        Add("Padding +", () => Change(() => draft.ToolbarPadding = Math.Min(24,draft.ToolbarPadding+1)));
        string[] floatNames = ["On", "Off", "Always"];
        Add($"Float {floatNames[draft.FloatMode]}", () => Change(() => draft.FloatMode = (draft.FloatMode + 1) % 3));
        Add(draft.AudioEnabled ? "Sound on" : "Sound off", () => Change(() => draft.AudioEnabled = !draft.AudioEnabled));
        Add(draft.AlternateInput ? "SendInput" : "Legacy input", () => Change(() => draft.AlternateInput = !draft.AlternateInput));
        Add("Bedroom profile", () => Change(() => _draft = new AppSettings()));
        if (_saveFailed) { Add("Retry saving", Save); }
        Add("OK", () =>
        {
            _settings = draft;
            _draft = null;
            _selection.Restore(draft.DefaultAction);
            ApplySettings();
            Location = new Point(draft.ToolbarX, draft.ToolbarY);
            LayoutButtons();
            Save();
        });
        Add("Cancel", () =>
        {
            _draft = null;
            Location = new Point(draft.ToolbarX, draft.ToolbarY);
            LayoutButtons();
        });
    }

    private void ApplySettings()
    {
        _palette = ToolbarPalette.For(_settings.Theme);
        BackColor = _palette.Shell;
        if (!_settings.ShowIndicator)
        {
            _overlay?.Hide();
        }

        _detector.ClickTime = _settings.ClickTime;
        _detector.MoveLimit = _settings.MoveLimit;
        TopMost = _settings.FloatMode != 1;
        Opacity = _settings.ToolbarOpacity;
    }

    private void KeepReachable()
    {
        Rectangle bounds = Screen.FromPoint(Location).WorkingArea;
        Location = new Point(Math.Clamp(Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)),
            Math.Clamp(Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height)));
    }

    private void StartMove()
    {
        if (_cursor() is not { } sample)
        {
            return;
        }

        _moveOffset = new Point(sample.Screen.X - Left, sample.Screen.Y - Top);
        LayoutButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ArrangeButtons();
        _dragWorker?.Prepare();
        _toggle.Click += (_, _) => ActivateButton(_toggle);
        if (NativeMethods.SetTimer(Handle, 1, 100, 0) == 0)
        {
            throw new Win32Exception();
        }

        _running = true;
        if (_settings.ShowIndicator)
        {
            _overlay = new OverlayWindow();
            _overlay.SetColor(_settings.IndicatorColor);
            _overlay.Create(NativeMethods.GetModuleHandle(null));
        }
        if (!NativeMethods.WTSRegisterSessionNotification(Handle, 0))
        {
            throw new Win32Exception();
        }
    }

    private void Toggle(bool fromDwell)
    {
        if (_fault != null)
        {
            if (!_clicks.Release()) { _detector.SetPaused(true); return; }
            _fault = null;
            _selection.Completed(ClickType.DragEnd);
        }
        if (!fromDwell)
        {
            _detector.SetPaused(!_detector.Paused);
        }

        LayoutButtons();
    }

    internal void Tick()
    {
        if (_processing || _lockedSession)
        {
            return;
        }

        _processing = true;
        try
        {
            if (++_ticks % 20 == 1)
            {
                _companion.Discover();
                if (_settings.FloatMode == 2)
                {
                    NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 0x13);
                }
            }
            if (_cursor() is not { } sample) { _detector.Reset(); _overlay?.Hide(); return; }
            var p = sample.Screen;
            if (_moveOffset is Point offset)
            {
                Location = new Point(p.X - offset.X, p.Y - offset.Y);
                KeepReachable();
                if (_detector.Sample(sample.Dwell, false, sample.Stable)) { _moveOffset = null; LayoutButtons(); Save(); }
                return;
            }
            var hwnd = NativeMethods.WindowFromPoint(p);
            if (hwnd == 0) { _detector.Reset(); return; }
            var hit = Control.FromHandle(hwnd);
            var button = hit as ToolbarButton;
            bool onToggle = button != null && button == _toggle && _commands.ContainsKey(button);
            bool owned = hit?.FindForm() == this;
            if (!_detector.Paused && _lastToolbarTarget != button?.AccessibleName)
            {
                _detector.CancelPending();
            }
            if (!_detector.Paused) { _lastToolbarTarget = button?.AccessibleName; }
            if (!_detector.Sample(sample.Dwell, onToggle, sample.Stable && (!owned || button != null)))
            {
                SetFeedback(button, _detector.Progress);
                if (_settings.ShowIndicator && !owned && !_detector.Paused && _detector.Progress > 0)
                {
                    _overlay?.UpdateProgress(p, _detector.Progress);
                }
                else
                {
                    _overlay?.Hide();
                }

                return;
            }
            SetFeedback(null, 0);
            _overlay?.Hide();
            if (onToggle)
            {
                Toggle(true);
            }
            else if (button != null && _commands.TryGetValue(button, out var command))
            {
                command();
            }
            else if (!owned && _draft == null && !_confirmQuit)
            {
                bool hadOverride = _selection.Temporary.HasValue;
                var next = _selection.Next;
                if (!_clicks.Dispatch(next, _settings.AlternateInput))
                {
                    _fault = "Input failed · resume";
                    _detector.SetPaused(true);
                }
                else
                {
                    _selection.Completed(next);
                }

                if (_fault != null || next is ClickType.DragStart or ClickType.DragEnd || hadOverride)
                {
                    LayoutButtons();
                }
            }
            if (_settings.AudioEnabled)
            {
                SystemSounds.Asterisk.Play();
            }
        }
        finally
        {
            _processing = false;
            if (_deferredClose && !IsDisposed)
            {
                _deferredClose = false;
                BeginInvoke(Close);
            }
        }
    }

    private static void BetweenEdges(int delay)
    {
        Application.DoEvents();
        if (delay > 0)
        {
            Thread.Sleep(delay);
        }
    }

    private void Save()
    {
        _settings.ToolbarX = Left;
        _settings.ToolbarY = Top;
        try
        {
            _store.Save(_settings);
            _saveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveFailed = true;
            if (_running)
            {
                LayoutButtons();
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (_running && _processing && m.Msg == 0x10)
        {
            _deferredClose = true;
            return;
        }
        if (_running && m.Msg == 0x113 && m.WParam == 1) { Tick(); return; }
        if (_running && m.Msg == 0x8003) { _companion.Acknowledge(); return; }
        if (_running && m.Msg == 0x8005 && !_processing)
        {
            switch ((int)m.WParam)
            {
                case 0: _detector.SetPaused(false); _draft = null; _confirmQuit = true; break;
                case 1: Close(); return;
                case 2: if (_fault == null) { _detector.SetPaused(false); } break;
                case 3: _draft = null; _confirmQuit = false; _moveOffset = null; _detector.SetPaused(true); break;
            }
            LayoutButtons();
            return;
        }
        if (_running && (m.Msg == 0x2b1 || m.Msg == 0x218 || m.Msg == 0x7e))
        {
            _detector.Reset();
            if (m.Msg == 0x2b1)
            {
                if ((int)m.WParam is 7 or 4 or 2)
                {
                    _lockedSession = true;
                }

                if ((int)m.WParam is 8 or 3 or 1)
                {
                    _lockedSession = false;
                }
            }
            if (m.Msg == 0x7e)
            {
                ArrangeButtons();
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Controls.Count > 0) { ArrangeButtons(); Invalidate(true); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            if (!_clicks.Release())
            {
                _fault = "Release failed · retry";
                _draft = null;
                _confirmQuit = false;
                _detector.SetPaused(true);
                LayoutButtons();
                e.Cancel = true;
                return;
            }
            if (_confirmQuit) { _companion.Quit(); }
            _running = false;
            NativeMethods.KillTimer(Handle, 1);
            NativeMethods.WTSUnRegisterSessionNotification(Handle);
            Save();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _clicks.Release(); _dragWorker?.Dispose(); _overlay?.Dispose(); _toggle.Dispose(); }
        base.Dispose(disposing);
    }
}
