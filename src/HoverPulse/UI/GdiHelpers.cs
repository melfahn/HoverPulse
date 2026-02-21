using System.Drawing;
using System.Drawing.Drawing2D;

namespace HoverPulse.UI;

/// <summary>
/// Static helpers for GDI+ drawing shared by UI windows.
/// </summary>
internal static class GdiHelpers
{
    /// <summary>
    /// Creates a rounded rectangle <see cref="GraphicsPath"/>.
    /// </summary>
    public static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        if (diameter > bounds.Width)
        {
            diameter = bounds.Width;
        }

        if (diameter > bounds.Height)
        {
            diameter = bounds.Height;
        }

        var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));

        // Top-left
        path.AddArc(arc, 180, 90);

        // Top-right
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom-right
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom-left
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
