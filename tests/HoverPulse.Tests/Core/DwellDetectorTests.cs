using HoverPulse.Core;
using HoverPulse.Interop;

namespace HoverPulse.Tests.Core;

public class DwellDetectorTests
{
    [Fact]
    public void OnCursorMoved_WithinTolerance_RaisesDwellProgress()
    {
        using var detector = new DwellDetector { DwellTimeMs = 1000, ToleranceRadius = 10 };
        var progressRaised = false;

        detector.DwellProgress += (_, _) => progressRaised = true;

        // First call starts the dwell
        detector.OnCursorMoved(new POINT(100, 100));
        // Second call within tolerance should raise progress
        detector.OnCursorMoved(new POINT(102, 102));

        progressRaised.Should().BeTrue();
    }

    [Fact]
    public void OnCursorMoved_OutsideTolerance_RaisesDwellCancelled()
    {
        using var detector = new DwellDetector { DwellTimeMs = 1000, ToleranceRadius = 10 };
        var cancelled = false;

        detector.DwellCancelled += () => cancelled = true;

        detector.OnCursorMoved(new POINT(100, 100));
        detector.OnCursorMoved(new POINT(100, 100)); // Start dwelling
        detector.OnCursorMoved(new POINT(200, 200)); // Move far away

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void OnCursorMoved_WhenPaused_DoesNotRaiseEvents()
    {
        using var detector = new DwellDetector { DwellTimeMs = 1, ToleranceRadius = 10, IsPaused = true };
        var anyEvent = false;

        detector.DwellProgress += (_, _) => anyEvent = true;
        detector.DwellCompleted += _ => anyEvent = true;

        detector.OnCursorMoved(new POINT(100, 100));
        detector.OnCursorMoved(new POINT(100, 100));
        Thread.Sleep(10);
        detector.Tick();

        anyEvent.Should().BeFalse();
    }

    [Fact]
    public void OnCursorMoved_WhenDisabled_DoesNotRaiseEvents()
    {
        using var detector = new DwellDetector { DwellTimeMs = 1, ToleranceRadius = 10, IsEnabled = false };
        var anyEvent = false;

        detector.DwellProgress += (_, _) => anyEvent = true;
        detector.DwellCompleted += _ => anyEvent = true;

        detector.OnCursorMoved(new POINT(100, 100));
        detector.OnCursorMoved(new POINT(100, 100));
        Thread.Sleep(10);
        detector.Tick();

        anyEvent.Should().BeFalse();
    }

    [Fact]
    public void Tick_AfterDwellTimeElapsed_RaisesDwellCompleted()
    {
        using var detector = new DwellDetector { DwellTimeMs = 10, ToleranceRadius = 10 };
        var completed = false;
        POINT completedAt = default;

        detector.DwellCompleted += p => { completed = true; completedAt = p; };

        detector.OnCursorMoved(new POINT(100, 100));
        Thread.Sleep(50); // Wait longer than dwell time
        detector.Tick();

        completed.Should().BeTrue();
        completedAt.X.Should().Be(100);
        completedAt.Y.Should().Be(100);
    }

    [Fact]
    public void Tick_DoesNotFireTwice_ForSameDwell()
    {
        using var detector = new DwellDetector { DwellTimeMs = 10, ToleranceRadius = 10 };
        var completedCount = 0;

        detector.DwellCompleted += _ => completedCount++;

        detector.OnCursorMoved(new POINT(100, 100));
        Thread.Sleep(50);
        detector.Tick();
        detector.Tick();
        detector.Tick();

        completedCount.Should().Be(1);
    }

    [Fact]
    public void Reset_CancelsDwell()
    {
        using var detector = new DwellDetector { DwellTimeMs = 10, ToleranceRadius = 10 };
        var completed = false;

        detector.DwellCompleted += _ => completed = true;

        detector.OnCursorMoved(new POINT(100, 100));
        detector.Reset();
        Thread.Sleep(50);
        detector.Tick();

        completed.Should().BeFalse();
    }
}
