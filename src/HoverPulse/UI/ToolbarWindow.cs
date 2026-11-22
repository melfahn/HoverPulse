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
    private readonly Func<POINT?> _cursor;
    private readonly FlowLayoutPanel _panel = new() { AutoSize = true, WrapContents = false };
    private readonly Button _toggle = MakeButton("Pause");
    private readonly Dictionary<Button, Action> _commands = [];
    private OverlayWindow? _overlay;
    private Point? _moveOffset;
    private bool _processing, _confirmQuit, _running, _lockedSession;
    private string? _fault;
    private bool _saveFailed;
    private int _ticks;
    private bool _deferredClose;
    private readonly Companion _companion = new();

    public ToolbarWindow(SettingsService store, AppSettings settings, ClickSimulator? clicks = null, Func<POINT?>? cursor = null)
    {
        _store = store;
        if (clicks == null)
        {
            _dragWorker = new DragWorker();
            clicks = new ClickSimulator(ClickSimulator.NativeEdge, BetweenEdges, _dragWorker.Send);
        }
        _clicks = clicks;
        _cursor = cursor ?? (() => NativeMethods.GetCursorPos(out var point) ? point : null);
        _settings = settings;
        _selection.Restore(settings.DefaultAction);
        Text = "HoverPulse";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(35, 38, 42);
        Location = new Point(settings.ToolbarX, settings.ToolbarY);
        Controls.Add(_panel);
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

    private static Button MakeButton(string label) => new()
    {
        Text = label,
        AccessibleName = label,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        ForeColor = Color.White,
        TabStop = false
    };

    private void Add(string label, Action action, bool selected = false, Button? existing = null)
    {
        var button = existing ?? MakeButton(label);
        button.Text = button.AccessibleName = label;
        button.BackColor = selected ? Color.Teal : Color.FromArgb(59, 64, 70);
        button.MinimumSize = new Size(42 + 12 * _settings.AppSize, 28 + 8 * _settings.AppSize);
        if (existing == null)
        {
            button.Click += (_, _) => ActivateButton(button);
        }

        _commands.Add(button, action);
        _panel.Controls.Add(button);
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
        _panel.SuspendLayout();
        foreach (var control in _panel.Controls.Cast<Control>().ToArray())
        {
            _panel.Controls.Remove(control);
            if (control != _toggle)
            {
                control.Dispose();
            }
        }
        _commands.Clear();
        _panel.FlowDirection = _settings.ToolbarHorizontal ? FlowDirection.LeftToRight : FlowDirection.TopDown;
        if (_draft is not null)
        {
            Options();
        }
        else if (_confirmQuit)
        {
            Add("Quit HoverPulse", Close);
            Add("Cancel", () => { _confirmQuit = false; LayoutButtons(); });
        }
        else
        {
            Add(_fault ?? (_detector.Paused ? "Resume" : "Pause"), () => Toggle(false), existing: _toggle);
            if (!_detector.Paused)
            {
                foreach (var (label, mode) in new[] { ("Double", ClickType.DoubleClick),
                    ("Drag", ClickType.DragStart), ("Right", ClickType.RightClick), ("Left", ClickType.LeftClick) })
                {
                    var caption = label + (_selection.Locked == mode ? " *" : "");
                    Add(caption, () => { _selection.Select(mode); _settings.DefaultAction = _selection.Locked; LayoutButtons(); },
                        _selection.Temporary == mode || (mode == ClickType.DragStart && _selection.Dragging));
                }
                Add(_saveFailed ? "Options · unsaved" : "Options", OpenOptions);
                Add("Quit", () => { _confirmQuit = true; LayoutButtons(); });
                Add(_moveOffset.HasValue ? "Place" : "Move", StartMove);
            }
        }
        _panel.WrapContents = true;
        _panel.MaximumSize = Screen.FromRectangle(Bounds).WorkingArea.Size;
        _panel.ResumeLayout(true);
        ClientSize = _panel.GetPreferredSize(Size.Empty);
        KeepReachable();
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
        Add($"Size {draft.AppSize}", () => Change(() => draft.AppSize = draft.AppSize % 3 + 1));
        string[] floatNames = ["On", "Off", "Always"];
        Add($"Float {floatNames[draft.FloatMode]}", () => Change(() => draft.FloatMode = (draft.FloatMode + 1) % 3));
        Add(draft.AudioEnabled ? "Sound on" : "Sound off", () => Change(() => draft.AudioEnabled = !draft.AudioEnabled));
        Add(draft.AlternateInput ? "SendInput" : "Legacy input", () => Change(() => draft.AlternateInput = !draft.AlternateInput));
        Add("Bedroom profile", () => Change(() => _draft = new AppSettings()));
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
        Add("Cancel", () => { _draft = null; LayoutButtons(); });
    }

    private void ApplySettings()
    {
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
        Rectangle bounds = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Clamp(Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)),
            Math.Clamp(Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height)));
    }

    private void StartMove()
    {
        if (_cursor() is not POINT point)
        {
            return;
        }

        _moveOffset = new Point(point.X - Left, point.Y - Top);
        LayoutButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
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
            if (_cursor() is not POINT p) { _detector.Reset(); _overlay?.Hide(); return; }
            if (_moveOffset is Point offset)
            {
                Location = new Point(p.X - offset.X, p.Y - offset.Y);
                KeepReachable();
                if (_detector.Sample(p, false)) { _moveOffset = null; LayoutButtons(); Save(); }
                return;
            }
            var hwnd = NativeMethods.WindowFromPoint(p);
            if (hwnd == 0) { _detector.Reset(); return; }
            var hit = Control.FromHandle(hwnd);
            var button = hit as Button;
            bool onToggle = button != null && button == _toggle && _commands.ContainsKey(button);
            bool owned = hit?.FindForm() == this;
            if (!_detector.Sample(p, onToggle))
            {
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
                KeepReachable();
            }
        }
        base.WndProc(ref m);
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
