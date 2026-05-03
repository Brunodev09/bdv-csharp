namespace BdvEngine.Gui;

/// <summary>
/// Base UI node. Holds local-to-parent x/y/w/h in screen pixels, a child list, and
/// the shared visibility/enabled/pickable flags. Subclasses override Render and may
/// override Update for input handling. Add() returns the *child* so you can chain
/// into deeper nodes; WithChildren(...) returns the parent for left-to-right builds.
/// </summary>
public abstract class Element
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    /// <summary>Visual scale applied around the rect's center at render time. Hit
    /// testing always uses the unscaled rect so a pulsing button stays clickable.</summary>
    public float RenderScale = 1f;
    public bool Visible = true;
    public bool Enabled = true;
    /// <summary>If false, hit-tests skip this element (text labels, decorative panels).</summary>
    public bool Pickable = true;
    public Element? Parent;
    public List<Element> Children = new();
    public List<IElementBehavior> Behaviors = new();

    /// <summary>Attach a behavior; returns the owning element so it chains with WithX
    /// methods. Capture the behavior reference before adding if you need to mutate it
    /// later: `var pulse = new PulseOnHover(); button.AddBehavior(pulse);`.</summary>
    public Element AddBehavior(IElementBehavior b) { Behaviors.Add(b); return this; }

    /// <summary>Add a child and return it, so the caller can keep configuring the child.</summary>
    public T Add<T>(T child) where T : Element
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    /// <summary>Add many children at once and return *this* for parent-side chaining.</summary>
    public Element WithChildren(params Element[] children)
    {
        foreach (var c in children) Add(c);
        return this;
    }

    /// <summary>Local position summed up the parent chain.</summary>
    public (float X, float Y) AbsolutePosition()
    {
        float ax = X, ay = Y;
        var p = Parent;
        while (p != null) { ax += p.X; ay += p.Y; p = p.Parent; }
        return (ax, ay);
    }

    public bool ContainsScreenPoint(float sx, float sy)
    {
        var (ax, ay) = AbsolutePosition();
        return sx >= ax && sx < ax + Width && sy >= ay && sy < ay + Height;
    }

    /// <summary>Visually-rendered rect (top-left + size) after applying RenderScale around
    /// the element's center. Widgets call this to draw; layout/hit-test use raw X/Y/Width/Height.</summary>
    public (float X, float Y, float W, float H) RenderRect()
    {
        var (ax, ay) = AbsolutePosition();
        if (RenderScale == 1f) return (ax, ay, Width, Height);
        float w = Width * RenderScale;
        float h = Height * RenderScale;
        return (ax + (Width - w) * 0.5f, ay + (Height - h) * 0.5f, w, h);
    }

    public virtual void Update(Context ctx)
    {
        if (!Visible || !Enabled) return;
        for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].Update(ctx, this);
        for (int i = 0; i < Children.Count;  i++) Children[i].Update(ctx);
    }

    public virtual void Render(Context ctx)
    {
        if (!Visible) return;
        for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].Render(ctx, this);
        for (int i = 0; i < Children.Count;  i++) Children[i].Render(ctx);
    }
}
