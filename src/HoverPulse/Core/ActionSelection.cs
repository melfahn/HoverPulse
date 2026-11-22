namespace HoverPulse.Core;

internal sealed class ActionSelection
{
    public ClickType Locked { get; private set; } = ClickType.LeftClick;
    public ClickType? Temporary { get; private set; }
    public bool Dragging { get; private set; }
    public ClickType Next => Dragging ? ClickType.DragEnd : Temporary ?? Locked;

    public void Restore(ClickType mode)
    {
        Locked = mode;
        Temporary = null;
    }

    public void Select(ClickType mode)
    {
        if (Temporary == mode)
        {
            Restore(mode);
        }
        else
        {
            Temporary = mode;
        }
    }

    public void Completed(ClickType mode)
    {
        Dragging = mode == ClickType.DragStart;
        if (!Dragging)
        {
            Temporary = null;
        }
    }
}
