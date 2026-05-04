namespace BdvEngine.Gui;

/// <summary>
/// Per-event mouse state passed to <see cref="Element"/>'s pointer-handler methods.
/// X/Y are the cursor position in window-logical screen pixels; Delta is the move
/// since last frame (only meaningful for drag events). Multi-button input would
/// extend this with a <c>Button</c> enum — for now the engine only tracks left.
/// </summary>
public readonly struct PointerEvent
{
    public readonly float X;
    public readonly float Y;
    public readonly float DeltaX;
    public readonly float DeltaY;
    public readonly bool LeftDown;

    public PointerEvent(float x, float y, float dx, float dy, bool leftDown)
    {
        X = x; Y = y; DeltaX = dx; DeltaY = dy; LeftDown = leftDown;
    }
}
