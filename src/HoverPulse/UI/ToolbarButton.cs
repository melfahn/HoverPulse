using System.Drawing;
using System.Drawing.Drawing2D;

namespace HoverPulse.UI;

internal enum Glyph { None, Pause, Resume, Left, Double, Drag, Right, Setup, Exit, Move, Place }

internal sealed class ToolbarButton : Button
{
    internal Glyph Icon { get; set; }
    internal ToolbarPalette Palette { get; set; } = ToolbarPalette.For(Services.ToolbarTheme.Neutral);
    internal bool Locked { get; set; }
    internal bool Queued { get; set; }
    internal bool Held { get; set; }
    internal bool ShowLabel { get; set; }
    internal bool Faulted { get; set; }
    private double _progress;
    internal double Progress
    {
        get => _progress;
        set { if (_progress != value) { _progress = value; Invalidate(); } }
    }

    internal ToolbarButton()
    {
        TabStop = false;
        FlatStyle = FlatStyle.Flat;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal static GraphicsPath Corners(RectangleF box, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        var path = new GraphicsPath();
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right-d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right-d, box.Bottom-d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom-d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Palette;
        float scale = DeviceDpi / 96f;
        bool toggle = Icon is Glyph.Resume or Glyph.Pause;
        Color fill = Held ? p.Ink : toggle ? p.Pause : Locked ? p.Locked : p.Target;
        Color ink = Held ? p.Shell : toggle ? p.PauseInk : Locked || Queued ? p.StateInk : Icon == Glyph.Exit ? p.Exit : p.Ink;
        g.Clear(p.Shell);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float stroke = (Locked || Queued || Held ? 2 : 1) * scale;
        using var face = Corners(new(stroke/2, stroke/2, Width-stroke, Height-stroke), 3*scale);
        using var brush = new SolidBrush(fill);
        using var outline = new Pen(Locked || Queued || Held ? p.Edge : toggle ? p.Pause : p.Border, stroke);
        g.FillPath(brush, face);
        if (Queued && !Held && !Locked) { outline.DashStyle = DashStyle.Dash; }
        g.DrawPath(outline, face);
        Rectangle labelBounds = ClientRectangle;
        if (Icon != Glyph.None)
        {
            float labelHeight = ShowLabel ? Font.Height + 3*scale : 0;
            float symbol = Math.Min(Width * (toggle ? .40f : .52f), (Height-labelHeight)*.63f);
            var state = g.Save();
            g.TranslateTransform((Width-symbol)/2, (Height-labelHeight-symbol)/2);
            g.ScaleTransform(symbol/32f,symbol/32f);
            DrawIcon(g, Icon, ink, fill);
            g.Restore(state);
            labelBounds = new(0, (int)(Height-labelHeight-2*scale), Width, (int)Math.Ceiling(labelHeight));
        }
        if (Icon == Glyph.None || ShowLabel)
        {
            TextRenderer.DrawText(g, Text, Font, labelBounds, ink, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        if (Queued && (Held || Locked))
        {
            float inset = 3*scale;
            using var inner = Corners(new(inset, inset, Width-2*inset, Height-2*inset), 2*scale);
            using var dashed = new Pen(ink,scale) { DashStyle = DashStyle.Dash };
            g.DrawPath(dashed,inner);
        }
        int slot = 0;
        if (Held) { DrawBadge(g, scale, slot++, "held"); }
        if (Locked) { DrawBadge(g, scale, slot++, "lock"); }
        if (Queued) { DrawBadge(g, scale, slot++, "next"); }
        if (Faulted) { DrawBadge(g, scale, slot, "fault"); }
        if (_progress > 0)
        {
            using var feedback = new Pen(ink, 2*scale);
            float end = 4*scale + (Width-8*scale)*(float)_progress;
            g.DrawLine(feedback,4*scale,4*scale,end,4*scale);
        }
    }

    private void DrawBadge(Graphics g, float scale, int slot, string kind)
    {
        float box = 12*scale, x = Width-box-3*scale-slot*13*scale, y = ShowLabel ? 3*scale : Height-box-3*scale;
        var state = g.Save();
        g.TranslateTransform(x,y);
        g.ScaleTransform(box/14f,box/14f);
        using var badge = new SolidBrush(Held ? Palette.Shell : Palette.StateInk);
        using var mark = new Pen(Held ? Palette.Ink : Palette.Shell,1.3f) { LineJoin = LineJoin.Round };
        g.FillRectangle(badge,0,0,14,14);
        switch (kind)
        {
            case "held": g.DrawEllipse(mark,2,5,6,4); g.DrawEllipse(mark,6,5,6,4); break;
            case "lock": g.DrawArc(mark,4,2,6,7,180,180); g.DrawRectangle(mark,3,6,8,6); break;
            case "next": g.DrawLine(mark,3,7,11,7); g.DrawLines(mark, [new Point(7,3),new(11,7),new(7,11)]); break;
            case "fault": g.DrawLine(mark,7,2,7,8); g.DrawLine(mark,7,10,7,12); break;
        }
        g.Restore(state);
    }

    private static void DrawIcon(Graphics g, Glyph icon, Color ink, Color fill)
    {
        using var pen = new Pen(ink, 2.25f) { LineJoin=LineJoin.Round, StartCap=LineCap.Round, EndCap=LineCap.Round };
        Point[] pointer = [new(4,3),new(28,13),new(18,17),new(14,28)];
        switch (icon)
        {
            case Glyph.Pause: g.DrawRectangle(pen,6,3,7,26); g.DrawRectangle(pen,19,3,7,26); break;
            case Glyph.Resume: g.DrawPolygon(pen,[new Point(8,3),new(27,16),new(8,29)]); break;
            case Glyph.Left:
            case Glyph.Right:
            case Glyph.Double:
                if (icon == Glyph.Right) { g.TranslateTransform(32,0); g.ScaleTransform(-1,1); }
                if (icon == Glyph.Double)
                {
                    using var backdrop = new SolidBrush(fill);
                    var state = g.Save();
                    g.TranslateTransform(7,-2);
                    g.ScaleTransform(.78f,.78f);
                    g.DrawPolygon(pen,pointer);
                    g.Restore(state);
                    g.TranslateTransform(0,4); g.ScaleTransform(.89f,.89f);
                    g.FillPolygon(backdrop,pointer);
                }
                g.DrawPolygon(pen,pointer);
                break;
            case Glyph.Drag:
                using (var hand = new GraphicsPath())
                {
                    hand.AddLines([new Point(5,18),new(3,19),new(4,22),new(10,28)]);
                    hand.AddBezier(10,28,17,35,30,29,30,20);
                    hand.AddLine(30,20,30,12); hand.AddBezier(30,12,30,8,25,8,25,12);
                    hand.AddLine(25,12,25,16); hand.AddLine(25,16,25,7);
                    hand.AddBezier(25,7,25,3,20,3,20,7); hand.AddLine(20,7,20,14);
                    hand.AddLine(20,14,20,5); hand.AddBezier(20,5,20,1,14,1,14,5);
                    hand.AddLine(14,5,14,14); hand.AddLine(14,14,14,7);
                    hand.AddBezier(14,7,14,3,9,3,9,7); hand.AddLine(9,7,9,21);
                    hand.CloseFigure(); g.DrawPath(pen,hand);
                }
                break;
            case Glyph.Setup:
                foreach (var (y, slider) in new[] {(6,19),(16,10),(26,21)})
                {
                    g.DrawLine(pen,3,y,slider-4,y); g.DrawLine(pen,slider,y,29,y);
                    g.DrawLine(pen,slider,y-3,slider,y+3);
                }
                break;
            case Glyph.Exit:
                g.DrawLines(pen,[new Point(14,3),new(6,3),new(6,29),new(14,29)]);
                g.DrawLine(pen,14,16,31,16); g.DrawLines(pen,[new Point(24,9),new(31,16),new(24,23)]);
                break;
            case Glyph.Move:
                g.DrawLine(pen,2,16,30,16); g.DrawLine(pen,16,2,16,30);
                g.DrawLines(pen,[new Point(7,11),new(2,16),new(7,21)]);
                g.DrawLines(pen,[new Point(25,11),new(30,16),new(25,21)]);
                g.DrawLines(pen,[new Point(11,7),new(16,2),new(21,7)]);
                g.DrawLines(pen,[new Point(11,25),new(16,30),new(21,25)]);
                break;
            case Glyph.Place:
                g.DrawLines(pen,[new Point(3,16),new(12,25),new(29,6)]);
                break;
        }
    }
}
