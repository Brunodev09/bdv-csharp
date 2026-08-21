namespace BdvEngine.Gui;

/// <summary>
/// Vertically-scrolling viewport. Children are added to an internal Content panel
/// that gets shifted by ScrollY each frame; the outer Panel's scissor (already in
/// place) clips the overflow. Mouse wheel scrolls when the cursor is over the view
/// (or any of its children). A simple track + thumb is drawn on the right edge
/// when the content is taller than the viewport.
///
/// Set <see cref="ContentHeight"/> to the total height of your content. (For dynamic
/// content, recompute it after layout — e.g. sum of children's heights + spacing.)
/// </summary>
public sealed class ScrollView : Panel
{
    public float ScrollY;
    public float ContentHeight;
    public float ScrollSpeed = 60f;
    public Color ScrollBarTrack = new(35, 40, 55, 200);
    public Color ScrollBarThumb = new(95, 115, 160, 255);
    public float ScrollBarWidth = 10f;

    /// <summary>True while the user is dragging the scrollbar thumb. Used to map
    /// raw cursor Y → ScrollY each frame independently of click hit-testing.</summary>
    private bool _draggingThumb;
    /// <summary>Pixel offset *within* the thumb where the user grabbed it, so the
    /// drag feels anchored under the cursor instead of jumping to thumb-top.</summary>
    private float _dragGrabOffset;

    /// <summary>Add children here, not on the ScrollView itself — they'll be shifted
    /// by ScrollY automatically. Forwards through <see cref="Element.Add{T}"/>.</summary>
    public Panel Content { get; }

    public ScrollView(float x, float y, float w, float h) : base(x, y, w, h)
    {
        // A scroll view MUST clip — its content is shifted by ScrollY and the
        // overflow has to be hidden. (Panel.ClipChildren is opt-in/false now.)
        ClipChildren = true;
        Content = new Panel(0, 0, w, 0);
        Content.NoClip();
        Content.Pickable = false;
        // Bypass our forwarding Add and attach directly.
        base.Add(Content);
    }

    /// <summary>Forward Add to the content panel. Children declare positions relative
    /// to the (un-scrolled) content area.</summary>
    public new T Add<T>(T child) where T : Element => Content.Add(child);

    public override void Update(Context ctx)
    {
        // Consume wheel input only when cursor is over us or our subtree.
        if (HoverIsInsideMe(ctx))
        {
            float wheel = InputManager.ConsumeWheelDelta();
            if (wheel != 0f)
            {
                ScrollY -= wheel * ScrollSpeed;
                ScrollY = Math.Clamp(ScrollY, 0f, MathF.Max(0f, ContentHeight - Height));
            }
        }

        // Scrollbar interaction: click on track jumps a page; click on thumb starts
        // a drag that maps cursor Y → ScrollY. Tracks LMB state ourselves rather
        // than relying on per-element pointer dispatch so the drag survives even
        // when the cursor wanders off the thumb (otherwise dragging feels sticky).
        UpdateScrollbarInput(ctx);

        Content.Y = -ScrollY;
        Content.Width = MathF.Max(0f, Width - ScrollBarWidth - 4f);
        Content.Height = ContentHeight;
        base.Update(ctx);
    }

    private void UpdateScrollbarInput(Context ctx)
    {
        if (ContentHeight <= Height) { _draggingThumb = false; return; }

        var (rx, ry, rw, rh) = AbsoluteRect();
        float trackX = rx + rw - ScrollBarWidth - 2f;
        float visibleRatio = Height / ContentHeight;
        float thumbH = MathF.Max(20f, rh * visibleRatio);
        float scrollRange = MathF.Max(1f, ContentHeight - Height);
        float trackTravel = MathF.Max(1f, rh - thumbH);
        float thumbY = ry + trackTravel * (ScrollY / scrollRange);

        // ctx.MouseX/Y are already in canvas-space coords.
        float cx = ctx.MouseX, cy = ctx.MouseY;

        if (_draggingThumb)
        {
            if (!ctx.MouseDown) { _draggingThumb = false; return; }
            // Map cursor Y back to ScrollY, anchored at the grab offset.
            float newThumbY = cy - _dragGrabOffset;
            float t = (newThumbY - ry) / trackTravel;
            ScrollY = Math.Clamp(t, 0f, 1f) * scrollRange;
            return;
        }

        // Only react on the initial press frame so we don't re-grab every tick.
        if (!ctx.MouseClicked) return;
        bool overTrack = cx >= trackX && cx <= trackX + ScrollBarWidth
                      && cy >= ry     && cy <= ry + rh;
        if (!overTrack) return;

        bool onThumb = cy >= thumbY && cy <= thumbY + thumbH;
        if (onThumb)
        {
            _draggingThumb = true;
            _dragGrabOffset = cy - thumbY;
        }
        else
        {
            // Click on the empty track: page-jump toward the cursor by one viewport.
            float dir = cy < thumbY ? -1f : 1f;
            ScrollY = Math.Clamp(ScrollY + dir * Height, 0f, scrollRange);
        }
    }

    private bool HoverIsInsideMe(Context ctx)
    {
        var e = ctx.Hovered;
        while (e != null) { if (e == this) return true; e = e.Parent; }
        return false;
    }

    public override void Render(Context ctx)
    {
        base.Render(ctx); // bg + scissored children + border

        // Scrollbar overlay (only when content overflows).
        if (ContentHeight <= Height) return;
        var (rx, ry, rw, rh) = AbsoluteRect();
        float a = EffectiveAlpha;
        float ws = ctx.WorldScale;
        float trackX = rx + rw - ScrollBarWidth - 2f;
        var trackW = ctx.ToWorld(trackX, ry);
        SpriteBatcher.DrawSolid(trackW.X, trackW.Y, ScrollBarWidth * ws, rh * ws,
            GuiHelpers.Mul(ScrollBarTrack, a), SpriteLayer.UI);

        float visibleRatio = Height / ContentHeight;
        float thumbH = MathF.Max(20f, rh * visibleRatio);
        float scrollRange = MathF.Max(1f, ContentHeight - Height);
        float thumbY = ry + (rh - thumbH) * (ScrollY / scrollRange);
        var thumbW = ctx.ToWorld(trackX, thumbY);
        SpriteBatcher.DrawSolid(thumbW.X, thumbW.Y, ScrollBarWidth * ws, thumbH * ws,
            GuiHelpers.Mul(ScrollBarThumb, a), SpriteLayer.UI);
    }
}
