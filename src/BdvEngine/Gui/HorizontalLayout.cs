namespace BdvEngine.Gui;

public enum VAlign { Top, Center, Bottom, Stretch }

/// <summary>
/// Container that auto-stacks its children left-to-right each frame.
/// Mirrors <see cref="VerticalLayout"/> on the other axis: children keep their
/// own Width; Height is honored unless ChildAlignment is Stretch.
/// </summary>
public sealed class HorizontalLayout : Panel
{
    public float Spacing = 4f;
    public Padding Padding = new(8f);
    public VAlign ChildAlignment = VAlign.Top;

    public HorizontalLayout(float x, float y, float w, float h) : base(x, y, w, h) { }

    public HorizontalLayout WithSpacing(float spacing) { Spacing = spacing; return this; }
    public HorizontalLayout WithPadding(Padding pad)   { Padding   = pad;     return this; }
    public HorizontalLayout WithPadding(float all)     { Padding   = new(all); return this; }
    public HorizontalLayout WithAlignment(VAlign a)    { ChildAlignment = a;   return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        DoLayout();
        base.Render(ctx);
    }

    private void DoLayout()
    {
        var (_, _, _, h) = AbsoluteRect();
        float innerH = MathF.Max(0f, h - Padding.Vertical);
        float x = Padding.Left;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) continue;
            c.AnchorMin = c.AnchorMax = c.Pivot = System.Numerics.Vector2.Zero;
            switch (ChildAlignment)
            {
                case VAlign.Top:     c.Y = Padding.Top; break;
                case VAlign.Bottom:  c.Y = Padding.Top + innerH - c.Height; break;
                case VAlign.Center:  c.Y = Padding.Top + (innerH - c.Height) * 0.5f; break;
                case VAlign.Stretch: c.Y = Padding.Top; c.Height = innerH; break;
            }
            c.X = x;
            x += c.Width + Spacing;
        }
    }
}
