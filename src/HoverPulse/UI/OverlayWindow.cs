using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HoverPulse.Interop;

namespace HoverPulse.UI;

/// <summary>
/// Transparent click-through overlay that shows a progress ring around the
/// cursor during a dwell. Uses UpdateLayeredWindow for per-pixel alpha.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "HoverPulse_Overlay";
    private const int WindowSize = 56;
    private const float RingThickness = 3.5f;
    private const float RingRadius = 20f;

    private readonly NativeMethods.WndProcDelegate _wndProc;
    private IntPtr _hWnd;
    private bool _isVisible;
    private Color _ringColor = Color.FromArgb(51, 153, 255); // #3399FF

    // Reusable drawing resources
    private readonly Bitmap _bitmap;
    private readonly Graphics _graphics;

    public OverlayWindow()
    {
        _wndProc = WndProc;
        _bitmap = new Bitmap(WindowSize, WindowSize, PixelFormat.Format32bppPArgb);
        _graphics = Graphics.FromImage(_bitmap);
        _graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    /// <summary>Returns the window handle.</summary>
    public IntPtr Handle => _hWnd;

    /// <summary>
    /// Sets the overlay ring color from a hex string (e.g., "#3399FF").
    /// </summary>
    public void SetColor(string hexColor)
    {
        _ringColor = ColorTranslator.FromHtml(hexColor);
    }

    /// <summary>
    /// Creates the overlay window. Must be called from the message loop thread.
    /// </summary>
    public void Create(IntPtr hInstance)
    {
        var wc = new WNDCLASSEX
        {
            CbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = hInstance,
            LpszClassName = ClassName,
            HCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW)
        };
        NativeMethods.RegisterClassEx(ref wc);

        const uint exStyle = NativeMethods.WS_EX_LAYERED
                           | NativeMethods.WS_EX_TRANSPARENT
                           | NativeMethods.WS_EX_TOPMOST
                           | NativeMethods.WS_EX_TOOLWINDOW
                           | NativeMethods.WS_EX_NOACTIVATE;

        _hWnd = NativeMethods.CreateWindowEx(
            exStyle, ClassName, "",
            NativeMethods.WS_POPUP,
            0, 0, WindowSize, WindowSize,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    /// <summary>
    /// Updates the overlay to show dwell progress at the specified screen point.
    /// </summary>
    /// <param name="center">Screen coordinates of the dwell point.</param>
    /// <param name="progress">Progress from 0.0 to 1.0.</param>
    public void UpdateProgress(POINT center, double progress)
    {
        if (_hWnd == IntPtr.Zero)
        {
            return;
        }

        // Draw the ring
        _graphics.Clear(Color.Transparent);

        if (progress > 0.01)
        {
            var sweepAngle = (float)(progress * 360.0);
            var ringRect = new RectangleF(
                WindowSize / 2f - RingRadius,
                WindowSize / 2f - RingRadius,
                RingRadius * 2,
                RingRadius * 2);

            using var pen = new Pen(_ringColor, RingThickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            _graphics.DrawArc(pen, ringRect, -90f, sweepAngle);
        }

        // Update the layered window
        var screenPos = new POINT(center.X - WindowSize / 2, center.Y - WindowSize / 2);
        var size = new SIZE { CX = WindowSize, CY = WindowSize };
        var sourcePos = new POINT(0, 0);
        var blend = new BLENDFUNCTION
        {
            BlendOp = BLENDFUNCTION.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = BLENDFUNCTION.AC_SRC_ALPHA
        };

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = _bitmap.GetHbitmap(Color.Empty);
        var oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

        NativeMethods.UpdateLayeredWindow(
            _hWnd, screenDc,
            ref screenPos, ref size,
            memDc, ref sourcePos,
            0, ref blend,
            NativeMethods.ULW_ALPHA);

        NativeMethods.SelectObject(memDc, oldBitmap);
        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteDC(memDc);
        _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);

        if (!_isVisible)
        {
            NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_SHOW);
            _isVisible = true;
        }
    }

    /// <summary>
    /// Hides the overlay window.
    /// </summary>
    public void Hide()
    {
        if (_isVisible && _hWnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_HIDE);
            _isVisible = false;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }
}
