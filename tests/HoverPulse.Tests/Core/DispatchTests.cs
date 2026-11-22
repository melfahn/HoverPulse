using HoverPulse.Core;

namespace HoverPulse.Tests.Core;

public class DispatchTests
{
    [Theory, InlineData(false), InlineData(true)]
    public void BothBackendsPreserveEdgeOrderAndSpacing(bool alternate)
    {
        List<(uint, int)> events = [];
        int time = 0;
        var sender = new ClickSimulator((edge, backend) =>
        {
            Assert.Equal(alternate, backend);
            events.Add((edge, time));
            return true;
        }, delta => time += delta);
        Assert.True(sender.Dispatch(ClickType.DoubleClick, alternate));
        int step = alternate ? 100 : 0;
        Assert.Equal(new (uint, int)[] { (2,0),(4,step),(2,2*step),(4,3*step) }, events);
        Assert.True(sender.Dispatch(ClickType.RightClick, alternate));
        Assert.Equal(new uint[] {8,16}, events.TakeLast(2).Select(e => e.Item1));
    }

    [Fact]
    public void ShortWriteKeepsOnlyOutstandingReleaseAndSuppressesReentry()
    {
        List<uint> edges = [];
        bool failUp = true;
        ClickSimulator? sender = null;
        sender = new ClickSimulator((edge, _) =>
        {
            edges.Add(edge);
            return edge != 4 || !failUp;
        }, _ => Assert.False(sender!.Dispatch(ClickType.RightClick, true)));
        Assert.False(sender.Dispatch(ClickType.DoubleClick, true));
        Assert.True(sender.Holding);
        Assert.False(sender.Dispatch(ClickType.LeftClick, true));
        failUp = false;
        Assert.True(sender.Release());
        Assert.True(sender.Release());
        Assert.Equal(new uint[] {2,4,4}, edges);
        Assert.False(new ClickSimulator((_,_) => false, _ => {}).Dispatch(ClickType.LeftClick, true));
    }

    [Fact]
    public void LockedAndOneUseDragSurvivePause()
    {
        var state = new ActionSelection();
        var detector = new DwellDetector();
        List<uint> edges = [];
        var sender = new ClickSimulator((edge, _) => { edges.Add(edge); return true; }, _ => {});
        void Fire()
        {
            var action = state.Next;
            Assert.True(sender.Dispatch(action, false));
            state.Completed(action);
        }

        state.Select(ClickType.RightClick);
        state.Select(ClickType.RightClick);
        state.Select(ClickType.DragStart);
        Fire();
        detector.SetPaused(true);
        Assert.False(detector.Sample(default, false));
        Assert.True(sender.Holding);
        detector.SetPaused(false);
        Fire();
        Assert.Equal(ClickType.RightClick, state.Next);
        state.Select(ClickType.DragStart);
        state.Select(ClickType.DragStart);
        Fire();
        Fire();
        Fire();
        Fire();
        Assert.Equal(ClickType.DragStart, state.Next);
        Assert.Equal(new uint[] {2,4,2,4,2,4}, edges);
    }

    [Fact]
    public void EightHoursOfDeliveredSamples()
    {
        var detector = new DwellDetector();
        detector.Sample(default, false);
        int clicks = 0;
        for (int tick = 0; tick < 288000; tick++)
        {
            if (detector.Sample(new HoverPulse.Interop.POINT((tick / 50 % 2) * 5, 0), false))
            {
                clicks++;
            }
        }
        Assert.Equal(5760, clicks);
    }
}
