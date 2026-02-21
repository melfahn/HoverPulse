using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using HoverPulse.Core;
using HoverPulse.Interop;

namespace HoverPulse.UI;

/// <summary>
/// Floating toolbar window for selecting click type and toggling pause.
/// Uses WM_PAINT with double-buffered GDI+ rendering and whole-window opacity.
/// </summary>
internal sealed class ToolbarWindow : IDisposable
{
    private const string ClassName = "HoverPulse_Toolbar";
    private const int ButtonSize = 40;
    private const int ButtonPadding = 4;
    private const int ToolbarPadding = 6;
    private const int ButtonCount = 5;
    private const float CornerRadius = 10f;

    private static readonly Color BackgroundColor = Color.FromArgb(40, 40, 40);
    private static readonly Color ButtonHoverColor = Color.FromArgb(70, 70, 70);
    private static readonly Color AccentColor = Color.FromArgb(51, 153, 255);
    private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
    private static readonly Color PausedAccentColor = Color.FromArgb(255, 100, 80);

    private static readonly string[] ButtonLabels = ["L", "R", "D", "Dg", "||"];
    private static readonly ClickType[] ButtonClickTypes =
        [ClickType.LeftClick, ClickType.RightClick, ClickType.DoubleClick, ClickType.DragStart];

    private readonly NativeMethods.WndProcDelegate _wndProc;
    private IntPtr _hWnd;
    private bool _isHorizontal;
    private int _windowWidth;
    private int _windowHeight;

    private ClickType _activeClickType = ClickType.LeftClick;
    private bool _isPaused;
    private int _hoverIndex = -1;

    // Dragging state
    private bool _isDragging;
    private POINT _dragStartCursor;
    private POINT _dragStartWindow;

    /// <summary>Raised when the user selects a different click type.</summary>
    public event Action<ClickType>? ClickTypeChanged;

    /// <summary>Raised when the user toggles pause.</summary>
    public event Action<bool>? PauseToggled;

    /// <summary>Current X position of the toolbar.</summary>
    public int X { get; private set; }

    /// <summary>Current Y position of the toolbar.</summary>
    public int Y { get; private set; }

    /// <summary>The currently active click type.</summary>
    public ClickType ActiveClickType => _activeClickType;

    /// <summary>Whether dwell clicking is paused.</summary>
    public bool IsPaused => _isPaused;

    public ToolbarWindow()
    {
        _wndProc = WndProc;
    }

    /// <summary>
    /// Creates and shows the toolbar window.
    /// </summary>
    public void Create(IntPtr hInstance, int x, int y, bool horizontal, byte opacity)
    {
        _isHorizontal = horizontal;
        X = x;
        Y = y;
        CalculateSize();

        var wc = new WNDCLASSEX
        {
            CbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            Style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = hInstance,
            LpszClassName = ClassName,
            HCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW)
        };
        NativeMethods.RegisterClassEx(ref wc);

        const uint exStyle = NativeMethods.WS_EX_TOPMOST
                           | NativeMethods.WS_EX_TOOLWINDOW
                           | NativeMethods.WS_EX_NOACTIVATE
                           | NativeMethods.WS_EX_LAYERED;

        _hWnd = NativeMethods.CreateWindowEx(
            exStyle, ClassName, "",
            NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE,
            X, Y, _windowWidth, _windowHeight,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        NativeMethods.SetLayeredWindowAttributes(_hWnd, 0, opacity, NativeMethods.LWA_ALPHA);
    }

    /// <summary>Sets the active click type and repaints.</summary>
    public void SetActiveClickType(ClickType clickType)
    {
        _activeClickType = clickType;
        Invalidate();
    }

    /// <summary>Sets the paused state and repaints.</summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        Invalidate();
    }

    /// <summary>Returns the window handle.</summary>
    public IntPtr Handle => _hWnd;

    private void CalculateSize()
    {
        var totalButtonExtent = ButtonCount * ButtonSize + (ButtonCount - 1) * ButtonPadding;
        if (_isHorizontal)
        {
            _windowWidth = totalButtonExtent + ToolbarPadding * 2;
            _windowHeight = ButtonSize + ToolbarPadding * 2;
        }
        else
        {
            _windowWidth = ButtonSize + ToolbarPadding * 2;
            _windowHeight = totalButtonExtent + ToolbarPadding * 2;
        }
    }

    private Rectangle GetButtonRect(int index)
    {
        int x, y;
        if (_isHorizontal)
        {
            x = ToolbarPadding + index * (ButtonSize + ButtonPadding);
            y = ToolbarPadding;
        }
        else
        {
            x = ToolbarPadding;
            y = ToolbarPadding + index * (ButtonSize + ButtonPadding);
        }
        return new Rectangle(x, y, ButtonSize, ButtonSize);
    }

    private int HitTestButton(int x, int y)
    {
        for (var i = 0; i < ButtonCount; i++)
        {
            if (GetButtonRect(i).Contains(x, y))
            {
                return i;
            }
        }
        return -1;
    }

    private void PaintWindow(IntPtr hdc)
    {
        using var bitmap = new Bitmap(_windowWidth, _windowHeight, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Background
        using (var bgPath = GdiHelpers.CreateRoundedRect(
            new RectangleF(0, 0, _windowWidth, _windowHeight), CornerRadius))
        using (var bgBrush = new SolidBrush(BackgroundColor))
        {
            g.FillPath(bgBrush, bgPath);
        }

        using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var textBrush = new SolidBrush(TextColor);

        for (var i = 0; i < ButtonCount; i++)
        {
            var rect = GetButtonRect(i);
            var rectF = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
            var isPauseButton = i == ButtonCount - 1;

            // Determine button color
            Color? fillColor = null;
            if (isPauseButton && _isPaused)
            {
                fillColor = PausedAccentColor;
            }
            else if (!isPauseButton && ButtonClickTypes[i] == _activeClickType)
            {
                fillColor = AccentColor;
            }
            else if (i == _hoverIndex)
            {
                fillColor = ButtonHoverColor;
            }

            if (fillColor is not null)
            {
                using var btnPath = GdiHelpers.CreateRoundedRect(rectF, 6f);
                using var btnBrush = new SolidBrush(fillColor.Value);
                g.FillPath(btnBrush, btnPath);
            }

            // Draw label
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(ButtonLabels[i], font, textBrush, rectF, sf);
        }

        // Blit to window DC
        using var srcGraphics = Graphics.FromHdc(hdc);
        srcGraphics.DrawImageUnscaled(bitmap, 0, 0);
    }

    private void Invalidate()
    {
        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.InvalidateRect(_hWnd, IntPtr.Zero, true);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_PAINT:
            {
                var hdc = NativeMethods.BeginPaint(hWnd, out var ps);
                PaintWindow(hdc);
                NativeMethods.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }

            case NativeMethods.WM_LBUTTONDOWN:
            {
                int mouseX = (short)(lParam.ToInt64() & 0xFFFF);
                int mouseY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var buttonIndex = HitTestButton(mouseX, mouseY);

                if (buttonIndex >= 0)
                {
                    if (buttonIndex == ButtonCount - 1)
                    {
                        // Pause button
                        _isPaused = !_isPaused;
                        PauseToggled?.Invoke(_isPaused);
                        Invalidate();
                    }
                    else
                    {
                        // Click type button
                        _activeClickType = ButtonClickTypes[buttonIndex];
                        ClickTypeChanged?.Invoke(_activeClickType);
                        Invalidate();
                    }
                }
                else
                {
                    // Start dragging on empty space
                    _isDragging = true;
                    NativeMethods.GetCursorPos(out _dragStartCursor);
                    _dragStartWindow = new POINT(X, Y);
                    NativeMethods.SetCapture(hWnd);
                }
                return IntPtr.Zero;
            }

            case NativeMethods.WM_MOUSEMOVE:
            {
                int mouseX = (short)(lParam.ToInt64() & 0xFFFF);
                int mouseY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                if (_isDragging)
                {
                    NativeMethods.GetCursorPos(out var currentCursor);
                    X = _dragStartWindow.X + (currentCursor.X - _dragStartCursor.X);
                    Y = _dragStartWindow.Y + (currentCursor.Y - _dragStartCursor.Y);
                    NativeMethods.SetWindowPos(
                        hWnd, IntPtr.Zero,
                        X, Y, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                }
                else
                {
                    var newHover = HitTestButton(mouseX, mouseY);
                    if (newHover != _hoverIndex)
                    {
                        _hoverIndex = newHover;
                        Invalidate();
                    }
                }
                return IntPtr.Zero;
            }

            case NativeMethods.WM_LBUTTONUP:
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    NativeMethods.ReleaseCapture();
                }
                return IntPtr.Zero;
            }

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }
}
