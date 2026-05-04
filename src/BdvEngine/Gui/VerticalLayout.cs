namespace BdvEngine.Gui;

public enum HAlign { Left, Center, Right, Stretch }

/// <summary>
/// Container that auto-stacks its children top-to-bottom each frame, with
/// configurable padding, inter-child spacing, and per-row horizontal alignment.
/// Inherits from <see cref="Panel"/> so it can also paint a background/border
/// and clip overflow — set background to <c>null</c> for a pure logical group.
///
/// Children keep their own Height; Width is honored unless ChildAlignment is
/// Stretch (then they're sized to fill the inner width).
/// </summary>
public sealed class VerticalLayout : Panel
{
    public float Spacing = 4f;
    public Padding Padding = new(8f);
    public HAlign ChildAlignment = HAlign.Left;

    public VerticalLayout(float x, float y, float w, float h) : base(x, y, w, h) { }

    public VerticalLayout WithSpacing(float spacing) { Spacing = spacing; return this; }
    public VerticalLayout WithPadding(Padding pad)   { Padding   = pad;     return this; }
    public VerticalLayout WithPadding(float all)     { Padding   = new(all); return this; }
    public VerticalLayout WithAlignment(HAlign a)    { ChildAlignment = a;   return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        DoLayout();
        base.Render(ctx);
    }

    private void DoLayout()
    {
        var (_, _, w, _) = AbsoluteRect();
        float innerW = MathF.Max(0f, w - Padding.Horizontal);
        float y = Padding.Top;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) continue;
            // Reset anchor to point so X/Y/Width/Height behave classically.
            c.AnchorMin = c.AnchorMax = c.Pivot = System.Numerics.Vector2.Zero;
            switch (ChildAlignment)
            {
                case HAlign.Left:    c.X = Padding.Left; break;
                case HAlign.Right:   c.X = Padding.Left + innerW - c.Width; break;
                case HAlign.Center:  c.X = Padding.Left + (innerW - c.Width) * 0.5f; break;
                case HAlign.Stretch: c.X = Padding.Left; c.Width = innerW; break;
            }
            c.Y = y;
            y += c.Height + Spacing;
        }
    }
}
