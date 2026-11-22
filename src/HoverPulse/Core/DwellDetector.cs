using HoverPulse.Interop;

namespace HoverPulse.Core;

internal sealed class DwellDetector
{
    private POINT? _previous;
    private int _count;
    private bool _latched;
    public bool Paused { get; private set; }
    public int ClickTime { get; set; } = 2;
    public int MoveLimit { get; set; } = 5;
    public double Progress => _latched ? 0 : Math.Clamp((double)_count / (ClickTime + 1), 0, 1);

    public void Reset()
    {
        _previous = null;
        _count = 0;
        _latched = false;
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        _count = 0;
    }

    // Called once per delivered timer message. Off-button samples during pause
    // leave even the previous coordinates untouched.
    public void CancelPending()
    {
        if (_count > 0) { _count = 0; }
    }

    public bool Sample(POINT point, bool overToggle, bool eligible = true)
    {
        if (Paused && !overToggle)
        {
            return false;
        }

        var previous = _previous;
        _previous = point;
        if (previous is null)
        {
            return false;
        }

        long step = Math.Max(Math.Abs((long)point.X - previous.Value.X),
                             Math.Abs((long)point.Y - previous.Value.Y));
        if (_latched)
        {
            if (step >= (Paused ? 2 : MoveLimit))
            {
                _latched = false;
            }
            else
            {
                _count = 0;
            }
        }
        _count = eligible && step < MoveLimit ? _count + 1 : 0;
        if (_count <= ClickTime)
        {
            return false;
        }

        if (overToggle)
        {
            SetPaused(!Paused);
        }

        _latched = true;
        _count = -ClickTime;
        return true;
    }
}
