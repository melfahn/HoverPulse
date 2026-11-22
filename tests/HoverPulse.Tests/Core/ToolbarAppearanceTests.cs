using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using HoverPulse.Core;
using HoverPulse.Interop;
using HoverPulse.Services;
using HoverPulse.UI;

namespace HoverPulse.Tests.Core;

public class ToolbarAppearanceTests
{
    private static readonly bool[] LabelModes = [false, true];
    private static readonly string[] Order = ["Pause","Left","Double","Drag","Right","Setup","Exit","Move"];

    [Theory, InlineData(96), InlineData(144)]
    public void TargetEdgesMatchApprovedDimensionsAtEachScale(int dpi)
    {
        var available = new Size(2400,1400);
        var dimensions = new[] { (44,52,56,5), (48,62,64,6), (56,68,72,8), (64,76,80,10) };
        for (int size = 0; size < dimensions.Length; size++)
        {
            var (width,height,toggle,gap) = dimensions[size];
            foreach (int padding in new[] { 0,4,12,24 })
            {
                var settings = new AppSettings { AppSize=size, ToolbarPadding=padding };
                var active = ToolbarLayout.Measure(settings,dpi,available,8,false);
                var paused = ToolbarLayout.Measure(settings,dpi,available,1,false);
                Assert.Equal(active.Targets[0],paused.Targets[0]);
                int Scale(int dip) => (int)Math.Round(dip * dpi / 96d,MidpointRounding.AwayFromZero);
                int x = padding+1;
                for (int i = 0; i < 8; i++)
                {
                    int right = x + (i == 0 ? toggle : width);
                    Assert.Equal(Rectangle.FromLTRB(Scale(x),Scale(padding+1),Scale(right),Scale(padding+1+height)),active.Targets[i]);
                    x = right + gap;
                }
                Assert.Equal(new Size(Scale(x-gap+padding+1),Scale(2*(padding+1)+height)),active.Shell);
            }
        }
    }

    [Fact]
    public void RealControlsMatchPaintAndHitTesting() => ToolbarIntegrationTests.OnPrivateDesktop(() =>
    {
        var folder = Directory.CreateTempSubdirectory("hoverclick-appearance-");
        string? evidence = Environment.GetEnvironmentVariable("HOVERCLICK_VISUAL_EVIDENCE");
        try
        {
            foreach (var theme in Enum.GetValues<ToolbarTheme>())
            {
                for (int size = 0; size < 4; size++)
                {
                    foreach (bool labels in LabelModes)
            {
                var settings = new AppSettings { ToolbarX=10,ToolbarY=10,AppSize=size,Theme=theme,ShowToolbarLabels=labels };
                List<uint> edges = [];
                POINT? cursor = null;
                var clicks = new ClickSimulator((f,_) => { edges.Add(f); return true; }, _ => {});
                using var form = new ToolbarWindow(new SettingsService(Path.Combine(folder.FullName,"settings.json")),
                    settings,clicks,()=>cursor);
                form.Show();
                Application.DoEvents();
                ToolbarButton[] Buttons() => form.Controls.OfType<ToolbarButton>().ToArray();
                void Dwell(Point p)
                {
                    cursor = new POINT(p.X,p.Y);
                    for (int tick = 0; tick < 8; tick++) { form.Tick(); }
                }
                var buttons = Buttons();
                Assert.Equal(Order,
                    buttons.Select(b=>b.AccessibleName).ToArray());
                Assert.All(buttons,b=>Assert.Equal(labels,b.ShowLabel));
                Rectangle[] bounds = buttons.Select(b=>b.Bounds).ToArray();
                foreach (var b in buttons)
                {
                    foreach (var pt in new[] {new Point(0,0),new(b.Width/2,b.Height/2),new(b.Width-1,b.Height-1)})
                    {
                        var screen = b.PointToScreen(pt);
                        Assert.Equal(b.Handle,NativeMethods.WindowFromPoint(new POINT(screen.X,screen.Y)));
                    }
                }
                var gap = form.PointToScreen(new Point(bounds[1].Right+1,bounds[1].Top+bounds[1].Height/2));
                Dwell(gap);
                Assert.Empty(edges);
                Assert.True(buttons[1].Locked);
                Dwell(buttons[4].PointToScreen(new Point(buttons[4].Width/2,buttons[4].Height/2)));
                buttons=Buttons();
                Assert.True(buttons[1].Locked);
                Assert.True(buttons[4].Queued);
                Assert.Equal(bounds, buttons.Select(b=>b.Bounds).ToArray());
                Assert.Empty(edges);
                using var bitmap = new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,form.ClientRectangle);
                var palette = ToolbarPalette.For(theme);
                Point interior = new(bounds[2].Left+5,bounds[2].Top+7);
                Assert.Equal(palette.Target.ToArgb(),bitmap.GetPixel(interior.X,interior.Y).ToArgb());
                if (evidence != null)
                {
                    Directory.CreateDirectory(evidence);
                    bitmap.Save(Path.Combine(evidence,$"{theme}-{size}-{(labels ? "labels" : "icons")}.png"),ImageFormat.Png);
                }
                var anchor = buttons[0].RectangleToScreen(buttons[0].ClientRectangle);
                Dwell(new Point(anchor.Left+anchor.Width/2,anchor.Top+anchor.Height/2));
                var resume = Assert.Single(Buttons());
                Assert.Equal("Resume",resume.AccessibleName);
                Assert.Equal(anchor,resume.RectangleToScreen(resume.ClientRectangle));
                Dwell(new Point(anchor.Left+anchor.Width/2+3,anchor.Top+anchor.Height/2));
                Assert.Equal(bounds,Buttons().Select(b=>b.Bounds).ToArray());
                Assert.Empty(edges);
                form.Close();
            }
                }
            }
        }
        finally { folder.Delete(true); }
    });

    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void ExistingSizeAndNewPreferencesRoundTrip(int previousSize)
    {
        var folder = Directory.CreateTempSubdirectory("hoverclick-profile-");
        try
        {
            string file = Path.Combine(folder.FullName,"settings.json");
            File.WriteAllText(file,$$"""{"SchemaVersion":2,"AppSize":{{previousSize}}}""");
            var store = new SettingsService(file);
            var profile=store.Load();
            Assert.Equal(previousSize,profile.AppSize);
            Assert.Equal(4,profile.ToolbarPadding);
            Assert.Equal(ToolbarTheme.Neutral,profile.Theme);
            Assert.False(profile.ShowToolbarLabels);
            profile.AppSize=0; profile.Theme=ToolbarTheme.Blue; profile.ToolbarPadding=11; profile.ShowToolbarLabels=true;
            store.Save(profile);
            var loaded=store.Load();
            Assert.Equal((0,ToolbarTheme.Blue,11,true),(loaded.AppSize,loaded.Theme,loaded.ToolbarPadding,loaded.ShowToolbarLabels));
        }
        finally { folder.Delete(true); }
    }
}
