using System.Text.Json;
using HoverPulse.Core;
using HoverPulse.Interop;

namespace HoverPulse.Tests.Core;

public class DwellDetectorTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "naturalpoint-sample-cases.json")));
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [item.GetProperty("name").GetString()!, item.Clone()];
        }
    }

    [Theory, MemberData(nameof(Fixtures))]
    public void Replay(string name, JsonElement trace)
    {
        var detector = new DwellDetector();
        detector.Sample(new POINT(100, 100), false);
        List<string> actual = [];
        int tick = 0;
        foreach (var sample in trace.GetProperty("samples").EnumerateArray())
        {
            tick++;
            bool toggle = sample.GetArrayLength() == 3;
            if (detector.Sample(new POINT(sample[0].GetInt32(), sample[1].GetInt32()), toggle))
            {
                actual.Add($"{tick} {(toggle ? (detector.Paused ? "pause" : "resume") : "left")}");
            }
        }
        Assert.Equal(trace.GetProperty("expected").EnumerateArray().Select(x => $"{x[0]} {x[1]}"), actual);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Theory, InlineData(1), InlineData(2), InlineData(50)]
    public void TimerLimitsAndRearm(int threshold)
    {
        var detector = new DwellDetector { ClickTime = threshold };
        var point = new POINT(int.MinValue, int.MaxValue);
        Assert.False(detector.Sample(point, false));
        for (int i = 0; i < threshold; i++)
        {
            Assert.False(detector.Sample(point, false));
        }

        Assert.True(detector.Sample(point, false));
        for (int i = 0; i < 600; i++)
        {
            Assert.False(detector.Sample(point, false));
        }

        point.X = int.MaxValue;
        Assert.False(detector.Sample(point, false));
        detector.Reset();
        Assert.False(detector.Sample(point, false));
    }
}
