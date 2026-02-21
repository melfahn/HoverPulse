using HoverPulse.Interop;

namespace HoverPulse.Core;

/// <summary>
/// Monitors cursor position and detects dwell events (cursor stationary within
/// a tolerance radius for a configurable duration).
/// </summary>
internal sealed class DwellDetector : IDisposable
{
    private readonly object _lock = new();

    private POINT _dwellStartPoint;
    private DateTime _dwellStartTime;
    private bool _isDwelling;
    private bool _isEnabled = true;
    private bool _isPaused;
    private bool _clickFiredForCurrentDwell;

    /// <summary>Dwell time in milliseconds. Default: 1000ms.</summary>
    public int DwellTimeMs { get; set; } = 1000;

    /// <summary>Tolerance radius in pixels. Default: 10px.</summary>
    public int ToleranceRadius { get; set; } = 10;

    /// <summary>Whether dwell detection is enabled.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; Reset(); }
    }

    /// <summary>Whether dwell detection is temporarily paused.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set { _isPaused = value; Reset(); }
    }

    /// <summary>Raised when dwell progress updates (0.0 to 1.0).</summary>
    public event Action<POINT, double>? DwellProgress;

    /// <summary>Raised when a dwell completes and a click should be triggered.</summary>
    public event Action<POINT>? DwellCompleted;

    /// <summary>Raised when a dwell is cancelled (cursor moved out of tolerance).</summary>
    public event Action? DwellCancelled;

    /// <summary>
    /// Called on every cursor movement. Evaluates whether the cursor is dwelling.
    /// </summary>
    public void OnCursorMoved(POINT currentPosition)
    {
        if (!_isEnabled || _isPaused)
        {
            return;
        }

        lock (_lock)
        {
            if (!_isDwelling)
            {
                // Start a new dwell
                _dwellStartPoint = currentPosition;
                _dwellStartTime = DateTime.UtcNow;
                _isDwelling = true;
                _clickFiredForCurrentDwell = false;
                return;
            }

            var distance = currentPosition.DistanceTo(_dwellStartPoint);

            if (distance > ToleranceRadius)
            {
                // Cursor moved outside tolerance — reset
                _isDwelling = false;
                _clickFiredForCurrentDwell = false;
                DwellCancelled?.Invoke();

                // Start a new dwell from current position
                _dwellStartPoint = currentPosition;
                _dwellStartTime = DateTime.UtcNow;
                _isDwelling = true;
                return;
            }

            // Cursor still within tolerance — check elapsed time
            var elapsed = (DateTime.UtcNow - _dwellStartTime).TotalMilliseconds;
            var progress = Math.Clamp(elapsed / DwellTimeMs, 0.0, 1.0);

            if (elapsed >= DwellTimeMs && !_clickFiredForCurrentDwell)
            {
                _clickFiredForCurrentDwell = true;
                DwellCompleted?.Invoke(_dwellStartPoint);
            }
            else if (!_clickFiredForCurrentDwell)
            {
                DwellProgress?.Invoke(_dwellStartPoint, progress);
            }
        }
    }

    /// <summary>
    /// Called periodically (e.g., on WM_TIMER) to check dwell state even when
    /// the cursor is completely still (no mouse messages arriving).
    /// </summary>
    public void Tick()
    {
        if (!_isEnabled || _isPaused || !_isDwelling || _clickFiredForCurrentDwell)
        {
            return;
        }

        lock (_lock)
        {
            var elapsed = (DateTime.UtcNow - _dwellStartTime).TotalMilliseconds;
            var progress = Math.Clamp(elapsed / DwellTimeMs, 0.0, 1.0);

            if (elapsed >= DwellTimeMs)
            {
                _clickFiredForCurrentDwell = true;
                DwellCompleted?.Invoke(_dwellStartPoint);
            }
            else
            {
                DwellProgress?.Invoke(_dwellStartPoint, progress);
            }
        }
    }

    /// <summary>Resets the current dwell state.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _isDwelling = false;
            _clickFiredForCurrentDwell = false;
            DwellCancelled?.Invoke();
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
