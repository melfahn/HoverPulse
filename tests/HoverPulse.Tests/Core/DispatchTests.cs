using HoverPulse.Core;

namespace HoverPulse.Tests.Core;

public class DispatchTests
{
    [Theory, InlineData(false), InlineData(true)]
    public void WorkerAcknowledgesReleaseOnlyAfterTheButtonClears(bool clears)
    {
        using var commands = new MemoryStream([1, 3]);
        using var replies = new MemoryStream();
        List<uint> edges = [];
        Assert.Equal(0, DragWorker.Run(commands, replies, (flag, _) => { edges.Add(flag); return true; },
            () => !clears));
        Assert.Equal(new byte[] { 1, clears ? (byte)1 : (byte)0 }, replies.ToArray());
        Assert.Equal(clears ? new uint[] { 2, 4 } : [2, 4, 4], edges);
    }

    [Fact]
    public async Task DragReleaseUsesItsOriginalOwnerAndBackend()
    {
        List<(uint Flag, bool Alternate)> ordinary = [], drag = [];
        bool allowUp = false;
        var clicks = new ClickSimulator((flag, alt) => { ordinary.Add((flag, alt)); return true; },
            _ => { }, (flag, alt) => { drag.Add((flag, alt)); return ValueTask.FromResult(flag != 4 || allowUp); });

        Assert.True(await clicks.DispatchAsync(ClickType.LeftClick, false));
        Assert.True(await clicks.DispatchAsync(ClickType.DragStart, true));
        Assert.False(await clicks.DispatchAsync(ClickType.DragEnd, false));
        Assert.True(clicks.Holding);
        allowUp = true;
        Assert.True(await clicks.ReleaseAsync());
        Assert.False(clicks.Holding);
        Assert.Equal(new (uint, bool)[] { (2, false), (4, false) }, ordinary);
        Assert.Equal(new (uint, bool)[] { (2, true), (4, true), (4, true) }, drag);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task BothBackendsPreserveEdgeOrderAndSpacing(bool alternate)
    {
        List<(uint, int)> events = [];
        int time = 0;
        var sender = new ClickSimulator((edge, backend) =>
        {
            Assert.Equal(alternate, backend);
            events.Add((edge, time));
            return true;
        }, delta => time += delta);
        Assert.True(await sender.DispatchAsync(ClickType.DoubleClick, alternate));
        int step = alternate ? 100 : 0;
        Assert.Equal(new (uint, int)[] { (2,0),(4,step),(2,2*step),(4,3*step) }, events);
        Assert.True(await sender.DispatchAsync(ClickType.RightClick, alternate));
        Assert.Equal(new uint[] {8,16}, events.TakeLast(2).Select(e => e.Item1));
    }

    [Fact]
    public async Task ShortWriteKeepsOnlyOutstandingReleaseAndSuppressesReentry()
    {
        List<uint> edges = [];
        bool failUp = true;
        ValueTask<bool> reentry = default;
        ClickSimulator? sender = null;
        sender = new ClickSimulator((edge, _) =>
        {
            edges.Add(edge);
            return edge != 4 || !failUp;
        }, _ => reentry = sender!.DispatchAsync(ClickType.RightClick, true));
        Assert.False(await sender.DispatchAsync(ClickType.DoubleClick, true));
        Assert.True(reentry.IsCompletedSuccessfully);
        Assert.False(await reentry);
        Assert.True(sender.Holding);
        Assert.False(await sender.DispatchAsync(ClickType.LeftClick, true));
        failUp = false;
        Assert.True(await sender.ReleaseAsync());
        Assert.True(await sender.ReleaseAsync());
        Assert.Equal(new uint[] {2,4,4}, edges);
        Assert.False(await new ClickSimulator((_,_) => false, _ => {}).DispatchAsync(ClickType.LeftClick, true));
    }

    [Fact]
    public async Task LockedAndOneUseDragSurvivePause()
    {
        var state = new ActionSelection();
        var detector = new DwellDetector();
        List<uint> edges = [];
        var sender = new ClickSimulator((edge, _) => { edges.Add(edge); return true; }, _ => {});
        async Task Fire()
        {
            var action = state.Next;
            Assert.True(await sender.DispatchAsync(action, false));
            state.Completed(action);
        }

        state.Select(ClickType.RightClick);
        state.Select(ClickType.RightClick);
        state.Select(ClickType.DragStart);
        await Fire();
        detector.SetPaused(true);
        Assert.False(detector.Sample(default, false));
        Assert.True(sender.Holding);
        detector.SetPaused(false);
        await Fire();
        Assert.Equal(ClickType.RightClick, state.Next);
        state.Select(ClickType.DragStart);
        state.Select(ClickType.DragStart);
        await Fire();
        await Fire();
        await Fire();
        await Fire();
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
