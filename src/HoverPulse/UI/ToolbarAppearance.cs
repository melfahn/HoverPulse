using System.Drawing;
using HoverPulse.Services;

namespace HoverPulse.UI;

internal sealed record ToolbarPalette(Color Shell, Color ShellBorder, Color Target, Color Border,
    Color Ink, Color Locked, Color Edge, Color StateInk, Color Pause, Color PauseInk, Color Exit)
{
    private static Color Hex(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);
    private static readonly ToolbarPalette Neutral = new(Hex(0x171717), Hex(0x777777), Hex(0x292929),
        Hex(0x767676), Hex(0xf4f4f4), Hex(0x444444), Hex(0xe2e2e2), Color.White,
        Hex(0xdedede), Hex(0x171717), Hex(0xe2e2e2));
    private static readonly ToolbarPalette Light = new(Color.White, Hex(0x9baac0), Hex(0xedf2f9),
        Hex(0xb8c6d7), Hex(0x263a57), Hex(0xdceafe), Hex(0x305dab), Hex(0x133c78),
        Hex(0x2354a2), Color.White, Hex(0x8c303e));
    private static readonly ToolbarPalette Blue = new(Hex(0x182335), Hex(0x687d9b), Hex(0x26364d),
        Hex(0x637997), Hex(0xf0f5ff), Hex(0x284983), Hex(0x93bafa), Hex(0xf3f7ff),
        Hex(0xa8caff), Hex(0x122b52), Hex(0xffc3cb));
    internal static ToolbarPalette For(ToolbarTheme theme) => theme switch
    {
        ToolbarTheme.Light => Light, ToolbarTheme.Blue => Blue, _ => Neutral
    };
}

internal readonly record struct TargetSize(string Name, int Width, int Height, int ToggleWidth, int Gap)
{
    internal static TargetSize For(int size) => size switch
    {
        0 => new("Extra Small", 44, 52, 56, 5),
        1 => new("Small", 48, 62, 64, 6),
        2 => new("Medium", 56, 68, 72, 8),
        3 => new("Large", 64, 76, 80, 10),
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };
}

internal sealed record ToolbarLayout(Rectangle[] Targets, Size Shell)
{
    internal static int Pixels(int dip, int dpi) => (int)Math.Round(dip * dpi / 96d, MidpointRounding.AwayFromZero);

    // Accumulate positions in DIPs, then round each edge. This preserves alignment
    // across fractional scales and uses the same rectangles for painting and input.
    internal static ToolbarLayout Measure(AppSettings settings, int dpi, Size available, int count, bool menu)
    {
        var size = TargetSize.For(settings.AppSize);
        int inset = 1 + settings.ToolbarPadding;
        int usableWidth = Math.Max(1, (int)(available.Width * 96d / dpi) - 2 * inset);
        int usableHeight = Math.Max(1, (int)(available.Height * 96d / dpi) - 2 * inset);
        bool across = menu || settings.ToolbarHorizontal;
        int x = inset, y = inset, lineBreadth = 0, right = inset, bottom = inset;
        var targets = new Rectangle[count];
        for (int i = 0; i < count; i++)
        {
            int width = menu ? Math.Min(174, usableWidth) : i == 0 ? size.ToggleWidth : size.Width;
            int height = menu ? 44 : size.Height;
            bool wrap = across ? x > inset && x + width > inset + usableWidth :
                y > inset && y + height > inset + usableHeight;
            if (menu && i > 0 && i % Math.Max(1, Math.Min(3, (usableWidth + size.Gap) / (width + size.Gap))) == 0)
            {
                wrap = true;
            }
            if (wrap)
            {
                if (across) { x = inset; y += lineBreadth + size.Gap; }
                else { y = inset; x += lineBreadth + size.Gap; }
                lineBreadth = 0;
            }
            targets[i] = Rectangle.FromLTRB(Pixels(x,dpi), Pixels(y,dpi), Pixels(x+width,dpi), Pixels(y+height,dpi));
            right = Math.Max(right, x + width);
            bottom = Math.Max(bottom, y + height);
            lineBreadth = Math.Max(lineBreadth, across ? height : width);
            if (across) { x += width + size.Gap; } else { y += height + size.Gap; }
        }
        return new(targets, new(Pixels(right + inset,dpi), Pixels(bottom + inset,dpi)));
    }
}
