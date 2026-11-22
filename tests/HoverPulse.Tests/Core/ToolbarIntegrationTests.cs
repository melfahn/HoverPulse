using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HoverPulse.Core;
using HoverPulse.Interop;
using HoverPulse.Services;
using HoverPulse.UI;

namespace HoverPulse.Tests.Core;

public class ToolbarIntegrationTests
{
    // CreateDesktop does not display or switch desktops. This private desktop uses a fake
    // cursor and a recording sink for its entire lifetime, including cleanup.
    [Fact]
    public void PointerOnlySessionOnPrivateDesktop() => OnPrivateDesktop(RunSession);

    [Theory, InlineData(false), InlineData(true)]
    public void DropAfterResumeKeepsTheToolbarResponsiveWhileWorkerReplies(bool alternate) => OnPrivateDesktop(() =>
    {
        using var receiver = new Form { StartPosition = FormStartPosition.Manual, Bounds = new(35,260,240,160) };
        receiver.Show();
        var folder = Directory.CreateTempSubdirectory("hoverclick-drop-");
        try
        {
            var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<uint> drag = [];
            List<uint> ordinary = [];
            var clicks = new ClickSimulator((flag, _) => { ordinary.Add(flag); return true; }, _ => {}, (flag, backend) =>
            {
                Assert.Equal(alternate, backend);
                drag.Add(flag);
                return flag == NativeMethods.MOUSEEVENTF_LEFTDOWN ? ValueTask.FromResult(true) : new(reply.Task);
            });
            CursorSample? cursor = null;
            using var form = new ToolbarWindow(new SettingsService(Path.Combine(folder.FullName,"settings.json")),
                new AppSettings { ToolbarX=0, ToolbarY=0, AlternateInput=alternate }, clicks, () => cursor);

            void Dwell(Point point)
            {
                POINT p = new(point.X, point.Y);
                cursor = new(p,p);
                for (int tick = 0; tick < 8; tick++) { form.Tick(); }
            }

            void Select(string label, int offset = 0)
            {
                var button = form.Controls.OfType<Button>().Single(b => b.Text == label);
                Dwell(button.PointToScreen(new Point(button.Width / 2 + offset, button.Height / 2)));
            }

            Exception? failure = null;
            using var deadline = new System.Windows.Forms.Timer { Interval = 2000 };
            deadline.Tick += (_, _) =>
            {
                failure = new TimeoutException("The toolbar did not finish the acknowledged drop and close.");
                form.Dispose();
            };
            form.Shown += (_, _) => form.BeginInvoke(() =>
            {
                try
                {
                    var target = receiver.PointToScreen(new Point(40,50));
                    Select("Drag");
                    Dwell(target);
                    Select("Pause");
                    Dwell(target);
                    Assert.Equal(new uint[] { 2 }, drag);
                    Select("Resume", 4);
                    Dwell(target);
                    Assert.Equal(new uint[] { 2, 4 }, drag);
                    Assert.True(clicks.Holding);

                    bool queriedWhileHeld = false;
                    form.BeginInvoke(() => queriedWhileHeld = clicks.Holding && form.Controls.OfType<Button>().Any(b => b.Text == "Pause"));
                    Application.DoEvents();
                    Assert.True(queriedWhileHeld);
                    form.Close();
                    Assert.True(form.Visible);
                    reply.SetResult(true);
                    deadline.Start();
                }
                catch (Exception error)
                {
                    failure = error;
                    form.Dispose();
                }
            });
            Application.Run(form);
            if (failure != null) { ExceptionDispatchInfo.Throw(failure); }
            Assert.False(form.Visible);
            Assert.False(clicks.Holding);
            Assert.Empty(ordinary);
            Assert.Equal(new uint[] { 2, 4 }, drag);
        }
        finally { folder.Delete(true); }
    });

    [Fact]
    public void ScreenPositionFindsTargetAndReferencePositionRearmsDwell() => OnPrivateDesktop(() =>
    {
        using var receiver = new Form { StartPosition = FormStartPosition.Manual, Bounds = new(35,260,240,160) };
        receiver.Show();
        var dir = Directory.CreateTempSubdirectory("hoverclick-coordinates-");
        try
        {
            CursorSample? cursor = null;
            List<uint> edges = [];
            var clicks = new ClickSimulator((edge,_) => { edges.Add(edge); return true; }, _ => {});
            using var form = new ToolbarWindow(new SettingsService(Path.Combine(dir.FullName,"settings.json")),
                new AppSettings { ToolbarX=0,ToolbarY=0 }, clicks, () => cursor);
            form.Show();
            var physical = receiver.PointToScreen(new Point(40,50));
            var toggle = form.Controls.OfType<Button>().Single(b => b.Text == "Pause");
            var logical = toggle.PointToScreen(new Point(12,12));
            void Hold(int physicalDelta, int logicalDelta, bool stable = true)
            {
                cursor = new(new(physical.X+physicalDelta,physical.Y),new(logical.X+logicalDelta,logical.Y),stable);
                for (int tick = 0; tick < 6; tick++) { form.Tick(); }
            }
            Hold(0,0);
            Hold(6,4);
            Assert.Equal(new uint[] {2,4}, edges);
            Hold(14,9,false);
            Assert.Equal(new uint[] {2,4}, edges);
            Hold(14,9);
            Assert.Equal(new uint[] {2,4,2,4}, edges);
        }
        finally { dir.Delete(true); }
    });

    internal static void OnPrivateDesktop(Action run)
    {
        string desktopName = "HoverPulseTests" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktopW(desktopName, null, 0, 0, 0x1ff, 0);
        Assert.NotEqual(IntPtr.Zero, desktop);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (!SetThreadDesktop(desktop)) { throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
                run();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        CloseDesktop(desktop);
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RunSession()
    {
        using var receiver = new CompanionReceiver { StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(30, 250, 200, 100), Text = "HoverPulse isolated receiver" };
        receiver.Show();
        _ = SetWindowLongW(receiver.Handle, -21, 0x0badc0de);
        var dir = Directory.CreateTempSubdirectory("hoverpulse-ui-");
        try
        {
            var store = new SettingsService(Path.Combine(dir.FullName, "settings.json"));
            var settings = new AppSettings { ToolbarX = 0, ToolbarY = 0 };
            List<uint> edges = [];
            POINT? cursor = default;
            var sender = new ClickSimulator((edge, _) => { edges.Add(edge); return true; }, _ => {});
            using var form = new ToolbarWindow(store, settings, sender, () => cursor is POINT p ? new CursorSample(p,p) : null);
            form.Show();
            Application.DoEvents();

            Button Find(string prefix) => form.Controls
                .OfType<Button>().Single(b => b.Text.StartsWith(prefix, StringComparison.Ordinal));
            void Dwell(Point point)
            {
                cursor = new POINT(point.X, point.Y);
                for (int i = 0; i < 7; i++)
                {
                    form.Tick();
                }
            }
            void Select(string prefix)
            {
                var button = Find(prefix);
                Dwell(button.PointToScreen(new Point(button.Width / 2,button.Height / 2)));
            }
            var target = receiver.PointToScreen(new Point(60, 30));
            Select("Right");
            Assert.Empty(edges);
            Assert.True(((ToolbarButton)Find("Right")).Queued);
            Dwell(target);
            Assert.Equal(new uint[] {8,16}, edges);
            Assert.False(((ToolbarButton)Find("Right")).Queued);
            Assert.True(((ToolbarButton)Find("Left")).Locked);
            Select("Drag");
            Dwell(target);
            Assert.True(sender.Holding);
            Assert.True(((ToolbarButton)Find("Drag")).Held);
            Select("Pause");
            Assert.Single(form.Controls);
            Dwell(target);
            Assert.Equal(new uint[] {8,16,2}, edges);
            var resume = Find("Resume");
            Dwell(resume.PointToScreen(new Point(resume.Width / 2 + 3, resume.Height / 2)));
            Assert.NotNull(Find("Setup"));
            Dwell(target);
            Assert.False(sender.Holding);
            Assert.False(((ToolbarButton)Find("Drag")).Held);
            Select("Setup");
            Select("Time +");
            Select("Theme");
            Select("Padding +");
            Select("Labels");
            Select("Size");
            Select("Cancel");
            Assert.Equal(2,settings.ClickTime);
            Assert.Equal((ToolbarTheme.Neutral,4,false,1),(settings.Theme,settings.ToolbarPadding,settings.ShowToolbarLabels,settings.AppSize));
            var setupOrigin = form.Location;
            Select("Setup");
            Select("Bedroom profile");
            Select("Cancel");
            Assert.Equal(setupOrigin, form.Location);
            Select("Setup");
            Select("Time +");
            Select("Theme");
            Select("Padding +");
            Select("Labels");
            Select("Size");
            Select("OK");
            Assert.Equal(3,store.Load().ClickTime);
            var saved = store.Load();
            Assert.Equal((ToolbarTheme.Light,5,true,2),(saved.Theme,saved.ToolbarPadding,saved.ShowToolbarLabels,saved.AppSize));
            cursor = null;
            form.Tick();
            Application.DoEvents();
            Assert.Contains(0x8001, receiver.Messages);
            SendMessageW(form.Handle, 0x8003, 0, 0);
            Application.DoEvents();
            Assert.Contains(0x8004, receiver.Messages);
            SendMessageW(form.Handle, 0x8005, 3, 0);
            Assert.NotNull(Find("Resume"));
            SendMessageW(form.Handle, 0x8005, 2, 0);
            Assert.NotNull(Find("Setup"));
            SendMessageW(form.Handle, 0x2b1, 7, 0);
            Dwell(target);
            Assert.Equal(4, edges.Count);
            SendMessageW(form.Handle, 0x2b1, 8, 0);
            Dwell(target);
            Assert.Equal(6, edges.Count);
            SendMessageW(form.Handle, 0x218, 7, 0);
            SendMessageW(form.Handle, 0x7e, 0, 0);
            Select("Move");
            var before = form.Location;
            Dwell(new Point(before.X + 300, before.Y + 130));
            Assert.NotEqual(before, form.Location);
            Select("Exit");
            Assert.Empty(edges.Skip(6));
            Select("Exit Hover Click");
            Application.DoEvents();
            Assert.False(form.Visible);
            Assert.Contains(0x8002, receiver.Messages);
        }
        finally { dir.Delete(true); }
    }

    private sealed class CompanionReceiver : Form
    {
        public HashSet<int> Messages { get; } = [];
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x8001 or 0x8002 or 0x8004)
            {
                Messages.Add(message.Msg);
            }

            base.WndProc(ref message);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowLongW(IntPtr window, int index, int value);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, string? device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);
}
