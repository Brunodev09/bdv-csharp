namespace BdvEngine.Gui;

/// <summary>
/// Top-level container. Owns the per-frame Context, performs hit testing, and
/// dispatches Update / Render down the tree. Game code constructs one Root, builds
/// the widget tree under it, then calls Root.Update / Root.Render each frame.
/// </summary>
public sealed class Root : Element
{
    public Font? Font;
    public CanvasScaleMode ScaleMode = CanvasScaleMode.ConstantPixelSize;
    public float ReferenceWidth  = 1600f;
    public float ReferenceHeight = 900f;
    /// <summary>0 = match width, 1 = match height, in-between blends. Only used by ScaleWithScreenSize.</summary>
    public float MatchWidthOrHeight = 0.5f;
    public float CurrentScale { get; private set; } = 1f;

    private bool _prevMouseDown;
    private float _lastMouseX, _lastMouseY;
    private Element? _lastHovered;
    private Element? _pressTarget;
    private readonly Context _ctx = new();

    public Root() { Pickable = false; X = 0; Y = 0; Width = 0; Height = 0; }

    /// <summary>Root's rect is the *logical* viewport (post-scale). Children anchor
    /// against this — so on ScaleWithScreenSize, anchors compute against the reference
    /// resolution and the whole tree visually scales when actual viewport differs.</summary>
    public override (float X, float Y, float W, float H) AbsoluteRect()
    {
        ComputeScale();
        return (0, 0, _ctx.ViewportW / CurrentScale, _ctx.ViewportH / CurrentScale);
    }

    private void ComputeScale()
    {
        CurrentScale = ScaleMode switch
        {
            CanvasScaleMode.ScaleWithScreenSize when ReferenceWidth > 0 && ReferenceHeight > 0
                => MathF.Pow(_ctx.ViewportW / ReferenceWidth,  1f - MatchWidthOrHeight)
                 * MathF.Pow(_ctx.ViewportH / ReferenceHeight, MatchWidthOrHeight),
            CanvasScaleMode.ConstantPhysicalSize when Gfx.WindowWidth > 0
                => Gfx.FramebufferWidth / (float)Gfx.WindowWidth,
            _ => 1f,
        };
        if (CurrentScale < 0.001f) CurrentScale = 1f;
    }

    public Root WithScaleMode(CanvasScaleMode mode) { ScaleMode = mode; return this; }
    public Root WithReferenceResolution(float w, float h) { ReferenceWidth = w; ReferenceHeight = h; return this; }

    /// <summary>Set the default font used by labels/buttons that don't override it.</summary>
    public Root WithFont(Font font) { Font = font; return this; }

    public void Update(Camera2D camera, int viewportW, int viewportH)
    {
        var mouse = InputManager.GetMousePosition();
        bool down = InputManager.IsLeftDown;

        _ctx.Camera = camera;
        _ctx.ViewportW = viewportW;
        _ctx.ViewportH = viewportH;
        _ctx.DefaultFont = Font;
        ComputeScale();
        _ctx.UIScale = CurrentScale;
        // Convert mouse from actual screen pixels into logical (post-scale) coords.
        _ctx.MouseX = mouse.X / CurrentScale;
        _ctx.MouseY = mouse.Y / CurrentScale;
        _ctx.MouseDown = down;
        _ctx.MouseClicked = down && !_prevMouseDown;
        _ctx.MouseReleased = !down && _prevMouseDown;

        _ctx.Hovered = HitTest(this, _ctx.MouseX, _ctx.MouseY);
        // Capturing element (e.g., slider being dragged) overrides hover.
        if (_ctx.Capturing != null) _ctx.Hovered = _ctx.Capturing;

        // ── Pointer event dispatch (Phase 3) ───────────────────────────────────
        var pe = new PointerEvent(
            _ctx.MouseX, _ctx.MouseY,
            _ctx.MouseX - _lastMouseX, _ctx.MouseY - _lastMouseY,
            _ctx.MouseDown);

        var current = _ctx.Hovered;
        if (current != _lastHovered)
        {
            _lastHovered?.DispatchPointerExit(pe);
            current?.DispatchPointerEnter(pe);
            _lastHovered = current;
        }
        if (_ctx.MouseClicked && current != null)
        {
            current.DispatchPointerDown(pe);
            _pressTarget = current;
        }
        if (_pressTarget != null && _ctx.MouseDown && (pe.DeltaX != 0 || pe.DeltaY != 0))
            _pressTarget.DispatchPointerDrag(pe);
        if (_ctx.MouseReleased)
        {
            if (_pressTarget != null)
            {
                _pressTarget.DispatchPointerUp(pe);
                if (_pressTarget == current) _pressTarget.DispatchPointerClick(pe);
            }
            _pressTarget = null;
        }
        // ─────────────────────────────────────────────────────────────────────

        Update(_ctx);

        if (_ctx.MouseReleased) _ctx.Capturing = null;
        _lastMouseX = _ctx.MouseX;
        _lastMouseY = _ctx.MouseY;
        _prevMouseDown = down;
    }

    public void Render(Camera2D camera, int viewportW, int viewportH)
    {
        _ctx.Camera = camera;
        _ctx.ViewportW = viewportW;
        _ctx.ViewportH = viewportH;
        _ctx.DefaultFont = Font;
        ComputeScale();
        _ctx.UIScale = CurrentScale;
        Render(_ctx);
    }

    /// <summary>Depth-first reverse-order pick — last drawn is topmost.
    /// Skips non-Interactable subtrees so disabled panels stop receiving input.</summary>
    private static Element? HitTest(Element el, float sx, float sy)
    {
        if (!el.Visible || !el.Interactable) return null;
        for (int i = el.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(el.Children[i], sx, sy);
            if (hit != null) return hit;
        }
        return el.Pickable && el.ContainsScreenPoint(sx, sy) ? el : null;
    }
}
