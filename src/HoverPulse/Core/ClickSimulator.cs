using System.Runtime.InteropServices;
using HoverPulse.Interop;

namespace HoverPulse.Core;

/// <summary>
/// The type of mouse click to simulate.
/// </summary>
internal enum ClickType
{
    LeftClick,
    RightClick,
    DoubleClick,
    DragStart,
    DragEnd
}

/// <summary>
/// Simulates mouse clicks at the current cursor position using SendInput.
/// </summary>
internal static class ClickSimulator
{
    /// <summary>
    /// Simulates a mouse click of the specified type at the current cursor position.
    /// </summary>
    public static void SimulateClick(ClickType clickType)
    {
        var inputs = clickType switch
        {
            ClickType.LeftClick => CreateLeftClick(),
            ClickType.RightClick => CreateRightClick(),
            ClickType.DoubleClick => CreateDoubleClick(),
            ClickType.DragStart => CreateMouseDown(isLeft: true),
            ClickType.DragEnd => CreateMouseUp(isLeft: true),
            _ => throw new ArgumentOutOfRangeException(nameof(clickType))
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            // SendInput may fail silently due to UIPI when targeting elevated windows.
            // We don't throw here — just log or ignore for now.
        }
    }

    private static INPUT[] CreateLeftClick()
    {
        return
        [
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTUP)
        ];
    }

    private static INPUT[] CreateRightClick()
    {
        return
        [
            CreateMouseInput(NativeMethods.MOUSEEVENTF_RIGHTDOWN),
            CreateMouseInput(NativeMethods.MOUSEEVENTF_RIGHTUP)
        ];
    }

    private static INPUT[] CreateDoubleClick()
    {
        return
        [
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTUP),
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            CreateMouseInput(NativeMethods.MOUSEEVENTF_LEFTUP)
        ];
    }

    private static INPUT[] CreateMouseDown(bool isLeft)
    {
        var flag = isLeft ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_RIGHTDOWN;
        return [CreateMouseInput(flag)];
    }

    private static INPUT[] CreateMouseUp(bool isLeft)
    {
        var flag = isLeft ? NativeMethods.MOUSEEVENTF_LEFTUP : NativeMethods.MOUSEEVENTF_RIGHTUP;
        return [CreateMouseInput(flag)];
    }

    private static INPUT CreateMouseInput(uint flags)
    {
        return new INPUT
        {
            Type = INPUT.INPUT_MOUSE,
            MouseInput = new MOUSEINPUT
            {
                Flags = flags,
                Time = 0,
                ExtraInfo = 0
            }
        };
    }
}
