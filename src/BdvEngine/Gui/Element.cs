using System.Numerics;

namespace BdvEngine.Gui;

/// <summary>
/// Base UI node. Holds anchor parameters, local rect (X/Y/Width/Height), child list,
/// and the shared visibility/enabled/pickable flags.
///
/// <b>RectTransform-style anchoring (Phase 1):</b>
/// <see cref="AnchorMin"/> and <see cref="AnchorMax"/> describe a rect inside the
/// parent's rect, in the [0,1] × [0,1] normalized space (where (0,0) is the parent's
/// top-left and (1,1) is the parent's bottom-right). <see cref="Pivot"/> picks the
/// reference point on the element itself.
///
/// • <b>Point anchor</b> (AnchorMin == AnchorMax): X/Y is the offset from the anchor
///   point to the pivot; Width/Height is the explicit size.
///
/// • <b>Stretched anchor</b> (AnchorMin != AnchorMax on an axis): X is the inset
///   from the start anchor line; Width is the inset from the end anchor line. The
///   element grows/shrinks with the parent on that axis. Same for Y / Height.
///
/// Defaults preserve the old "absolute pixel" behavior — AnchorMin = AnchorMax =
/// Pivot = (0,0), so X/Y/Width/Height behave exactly like before.
///
/// Use <see cref="AnchorTo(Anchor)"/> for one-line presets (TopRight, StretchAll, …).
/// </summary>
public abstract class Element
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    /// <summary>Top-left of the element's anchor rect inside the parent (normalized 0..1).</summary>
    public Vector2 AnchorMin = Vector2.Zero;
    /// <summary>Bottom-right of the element's anchor rect inside the parent (normalized 0..1).</summary>
    public Vector2 AnchorMax = Vector2.Zero;
    /// <summary>Reference point on the element itself (0,0 = top-left, 1,1 = bottom-right).</summary>
    public Vector2 Pivot     = Vector2.Zero;

    /// <summary>Visual scale applied around the rect's center at render time. Hit
    /// testing always uses the unscaled rect so a pulsing button stays clickable.</summary>
    public float RenderScale = 1f;
    public bool Visible = true;
    public bool Enabled = true;
    /// <summary>If false, hit-tests skip this element (text labels, decorative panels).</summary>
    public bool Pickable = true;
    /// <summary>Self alpha 0..1 — multiplies all rendered colors. Inherited
    /// down the subtree via <see cref="EffectiveAlpha"/> (CanvasGroup-style).</summary>
    public float Alpha = 1f;
    /// <summary>If false, this element and its subtree don't accept pointer input.
    /// Combined with low Alpha gives the standard "greyed out" look.</summary>
    public bool Interactable = true;

    /// <summary>Self * parent chain. 0 = fully transparent.</summary>
    public float EffectiveAlpha => Alpha * (Parent?.EffectiveAlpha ?? 1f);
    /// <summary>True only if self & every ancestor are Interactable + Visible + Enabled.</summary>
    public bool EffectiveInteractable
        => Interactable && Visible && Enabled && (Parent?.EffectiveInteractable ?? true);
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

    /// <summary>Remove this element from its parent's child list. No-op if no parent.
    /// The element and all its descendants stop rendering and stop receiving input
    /// next frame; re-attach via <see cref="Add{T}"/>.</summary>
    public void RemoveFromParent()
    {
        if (Parent == null) return;
        Parent.Children.Remove(this);
        Parent = null;
    }

    // ── Z-order (Unity SetSiblingIndex equivalent) ─────────────────────────────
    // Sibling index in the parent's child list IS the z-order. Render walks children
    // forward (later = on top); HitTest walks reverse (later = topmost = first hit).
    // Both stay consistent automatically when the list is reordered.

    /// <summary>This element's position in its parent's child list, or -1 if no parent.</summary>
    public int SiblingIndex => Parent?.Children.IndexOf(this) ?? -1;

    /// <summary>Move this element to the end of its parent's children — renders on top
    /// of all siblings. Equivalent to Unity's <c>Transform.SetAsLastSibling</c>.</summary>
    public Element BringToFront()
    {
        if (Parent == null) return this;
        Parent.Children.Remove(this);
        Parent.Children.Add(this);
        return this;
    }

    /// <summary>Move this element to index 0 of its parent's children — renders behind
    /// all siblings. Equivalent to Unity's <c>Transform.SetAsFirstSibling</c>.</summary>
    public Element SendToBack()
    {
        if (Parent == null) return this;
        Parent.Children.Remove(this);
        Parent.Children.Insert(0, this);
        return this;
    }

    /// <summary>Move this element to a specific index in its parent's children list.
    /// Clamped to [0, Children.Count - 1]. Equivalent to Unity's
    /// <c>Transform.SetSiblingIndex(int)</c>.</summary>
    public Element SetSiblingIndex(int index)
    {
        if (Parent == null) return this;
        Parent.Children.Remove(this);
        index = Math.Clamp(index, 0, Parent.Children.Count);
        Parent.Children.Insert(index, this);
        return this;
    }

    /// <summary>Add many children at once and return *this* for parent-side chaining.</summary>
    public Element WithChildren(params Element[] children)
    {
        foreach (var c in children) Add(c);
        return this;
    }

    /// <summary>Set Anchor*/Pivot from a preset. Returns the element for chaining.</summary>
    public Element AnchorTo(Anchor preset)
    {
        var (min, max, pivot) = AnchorPresets.Of(preset);
        AnchorMin = min; AnchorMax = max; Pivot = pivot;
        return this;
    }

    /// <summary>Manually set anchors / pivot. (0,0)..(0,0) = top-left point anchor;
    /// (0,0)..(1,1) = stretch to fill parent. Returns the element for chaining.</summary>
    public Element WithAnchors(Vector2 min, Vector2 max, Vector2 pivot)
    {
        AnchorMin = min; AnchorMax = max; Pivot = pivot;
        return this;
    }

    /// <summary>Computed absolute screen rect based on anchors + parent rect. This is
    /// the source of truth — widgets render against it, hit-test against it, scissor
    /// against it. Recomputes each call (cheap; cache locally if calling multiple times
    /// in one frame).</summary>
    public virtual (float X, float Y, float W, float H) AbsoluteRect()
    {
        if (Parent == null)
        {
            // Root or detached element: X/Y/Width/Height are taken at face value.
            return (X, Y, Width, Height);
        }
        var (px, py, pw, ph) = Parent.AbsoluteRect();

        float aMinX = px + AnchorMin.X * pw;
        float aMaxX = px + AnchorMax.X * pw;
        float aMinY = py + AnchorMin.Y * ph;
        float aMaxY = py + AnchorMax.Y * ph;

        float x, y, w, h;
        if (AnchorMin.X == AnchorMax.X)
        {
            // Point anchor on X — X is offset from anchor to pivot, Width is size.
            x = aMinX + X - Pivot.X * Width;
            w = Width;
        }
        else
        {
            // Stretched on X — X is left inset, Width is right inset.
            x = aMinX + X;
            w = (aMaxX - aMinX) - X - Width;
        }
        if (AnchorMin.Y == AnchorMax.Y)
        {
            y = aMinY + Y - Pivot.Y * Height;
            h = Height;
        }
        else
        {
            y = aMinY + Y;
            h = (aMaxY - aMinY) - Y - Height;
        }
        return (x, y, w, h);
    }

    /// <summary>Convenience: just the top-left of <see cref="AbsoluteRect"/>.</summary>
    public (float X, float Y) AbsolutePosition()
    {
        var (rx, ry, _, _) = AbsoluteRect();
        return (rx, ry);
    }

    public bool ContainsScreenPoint(float sx, float sy)
    {
        var (rx, ry, rw, rh) = AbsoluteRect();
        return sx >= rx && sx < rx + rw && sy >= ry && sy < ry + rh;
    }

    /// <summary>Visually-rendered rect (top-left + size) after applying RenderScale around
    /// the element's center. Widgets call this to draw; layout/hit-test use AbsoluteRect.</summary>
    public (float X, float Y, float W, float H) RenderRect()
    {
        var (rx, ry, rw, rh) = AbsoluteRect();
        if (RenderScale == 1f) return (rx, ry, rw, rh);
        float w = rw * RenderScale;
        float h = rh * RenderScale;
        return (rx + (rw - w) * 0.5f, ry + (rh - h) * 0.5f, w, h);
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

    // -------- Pointer event hooks. Override in widgets; behaviors get the same events
    // -------- forwarded by the Dispatch* helpers below (called by Root).
    public virtual void OnPointerEnter(PointerEvent e) { }
    public virtual void OnPointerExit (PointerEvent e) { }
    public virtual void OnPointerDown (PointerEvent e) { }
    public virtual void OnPointerUp   (PointerEvent e) { }
    public virtual void OnPointerClick(PointerEvent e) { }
    public virtual void OnPointerDrag (PointerEvent e) { }

    internal void DispatchPointerEnter(PointerEvent e) { OnPointerEnter(e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerEnter(this, e); }
    internal void DispatchPointerExit (PointerEvent e) { OnPointerExit (e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerExit (this, e); }
    internal void DispatchPointerDown (PointerEvent e) { OnPointerDown (e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerDown (this, e); }
    internal void DispatchPointerUp   (PointerEvent e) { OnPointerUp   (e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerUp   (this, e); }
    internal void DispatchPointerClick(PointerEvent e) { OnPointerClick(e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerClick(this, e); }
    internal void DispatchPointerDrag (PointerEvent e) { OnPointerDrag (e); for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].OnPointerDrag (this, e); }
}
