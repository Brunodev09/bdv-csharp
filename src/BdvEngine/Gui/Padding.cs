namespace BdvEngine.Gui;

/// <summary>
/// Inset on the four sides of a layout container, in screen pixels.
/// Construct with one value (uniform), two (horizontal/vertical), or four sides.
/// </summary>
public readonly struct Padding
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Right;
    public readonly float Bottom;

    public Padding(float all) : this(all, all, all, all) { }
    public Padding(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public Padding(float left, float top, float right, float bottom)
    { Left = left; Top = top; Right = right; Bottom = bottom; }

    public static readonly Padding Zero = new(0);

    public float Horizontal => Left + Right;
    public float Vertical   => Top  + Bottom;
}
