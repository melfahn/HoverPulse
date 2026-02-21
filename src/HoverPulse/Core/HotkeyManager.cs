using HoverPulse.Interop;

namespace HoverPulse.Core;

/// <summary>
/// Manages global hotkey registration via RegisterHotKey/UnregisterHotKey.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hWnd;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    /// <summary>
    /// Registers a global hotkey and returns its ID.
    /// </summary>
    /// <param name="modifiers">MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN, or 0 for none.</param>
    /// <param name="virtualKey">Virtual key code (e.g., 0x78 for F9).</param>
    /// <param name="handler">Action to invoke when hotkey is pressed.</param>
    /// <returns>The hotkey ID, or -1 if registration failed.</returns>
    public int Register(uint modifiers, uint virtualKey, Action handler)
    {
        var id = _nextId++;

        if (!NativeMethods.RegisterHotKey(_hWnd, id, modifiers, virtualKey))
        {
            return -1;
        }

        _handlers[id] = handler;
        return id;
    }

    /// <summary>
    /// Unregisters a previously registered hotkey.
    /// </summary>
    public void Unregister(int id)
    {
        NativeMethods.UnregisterHotKey(_hWnd, id);
        _handlers.Remove(id);
    }

    /// <summary>
    /// Handles WM_HOTKEY messages. Call this from the window procedure.
    /// </summary>
    public void HandleHotkey(int id)
    {
        if (_handlers.TryGetValue(id, out var handler))
        {
            handler();
        }
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToArray())
        {
            Unregister(id);
        }
    }
}
