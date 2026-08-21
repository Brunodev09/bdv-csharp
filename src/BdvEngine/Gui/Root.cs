namespace BdvEngine.Gui;

/// <summary>
/// Stable z-tiers above sibling-order. Widgets placed in a higher layer render
/// on top of (and hit-test before) every widget in lower layers, regardless of
/// how either was added or whether anyone called BringToFront. Layer order is
/// back-to-front:
///   HUD      — the always-on screen UI (info panels, button bars, score readouts)
///   Panels   — slide-out drawers, side panels, anything secondary that opens
///              over the HUD but below modals (e.g., Sidebar)
///   Modals   — dialogs that demand attention (Help popup, city/entity panels,
///              confirmation prompts). Backdrops live here too.
///   Tooltips — transient hover hints that should never be obscured.
/// </summary>
public enum UiLayer { HUD, Panels, Modals, Tooltips }

/// <summary>
/// Top-level container. Owns the per-frame Context, performs hit testing, and
/// dispatches Update / Render down the tree. Game code constructs one Root, builds
/// the widget tree under it, then calls Root.Update / Root.Render each frame.
///
/// Root maintains four <see cref="UiLayer"/> containers as its direct children
/// in fixed back-to-front order. Calls to <see cref="Add{T}(T)"/> route to the
/// HUD layer by default; <see cref="Add{T}(T, UiLayer)"/> targets a specific
/// layer. Layer containers themselves are not pickable and don't clip, so they
/// add zero behavioral overhead vs. parenting directly to Root.
/// </summary>
public sealed class Root : Element
{
    public Font? Font;
    /// <summary>F9-toggled layout inspector. Owned here so every
    /// project gets it automatically.</summary>
    public readonly UiLayoutInspector LayoutInspector = new();
    public CanvasScaleMode ScaleMode = CanvasScaleMode.ConstantPixelSize;
    public float ReferenceWidth  = 1600f;
    public float ReferenceHeight = 900f;
    /// <summary>0 = match width, 1 = match height, in-between blends. Only used by ScaleWithScreenSize.</summary>
    public float MatchWidthOrHeight = 0.5f;
    public float CurrentScale { get; private set; } = 1f;

    /// <summary>When set true, the next mouse-down→up sequence is swallowed (no
    /// OnPointerDown / OnPointerClick / OnPointerUp dispatched to any element).
    /// Use when you spawn a UI element in response to a click that originated
    /// outside the UI — otherwise the new element receives the same click and
    /// e.g. immediately dismisses itself. Auto-clears after the release.</summary>
    public bool SuppressNextClick { get; set; }

    private bool _prevMouseDown;
    private bool _swallowingClick;
    private float _lastMouseX, _lastMouseY;
    private Element? _lastHovered;
    private Element? _pressTarget;
    private readonly Context _ctx = new();

    /// <summary>One stretched container per <see cref="UiLayer"/>, indexed by enum value.
    /// Built once in the constructor and never removed — they're Root's only direct
    /// children. All public Add() routes elements into one of these.</summary>
    private readonly Panel[] _layers;

    public Root()
    {
        Pickable = false; X = 0; Y = 0; Width = 0; Height = 0;
        // Build layer containers in enum order so render walks them back-to-front.
        // Each is full-viewport stretched, non-pickable (so it doesn't intercept
        // clicks), and ClipChildren=false (so a layer can't trim a widget that
        // anchors past the viewport — e.g., the Sidebar's tab poking past 0).
        var names = System.Enum.GetValues(typeof(UiLayer));
        _layers = new Panel[names.Length];
        foreach (UiLayer l in names)
        {
            var p = new Panel(0, 0, 0, 0);
            p.AnchorTo(Anchor.StretchAll);
            p.NotPickable();
            p.NoClip();
            base.Add(p);
            _layers[(int)l] = p;
        }
    }

    /// <summary>Add a child to the specified UI layer. Layer order dominates
    /// sibling-order — anything in <see cref="UiLayer.Modals"/> renders above
    /// and hit-tests before anything in <see cref="UiLayer.Panels"/>, etc.</summary>
    public T Add<T>(T child, UiLayer layer) where T : Element
    {
        _layers[(int)layer].Add(child);
        return child;
    }

    /// <summary>Add a child to the default <see cref="UiLayer.HUD"/> layer.
    /// Hides the base <see cref="Element.Add{T}(T)"/> so existing call sites
    /// keep working unchanged — they just end up in HUD.</summary>
    public new T Add<T>(T child) where T : Element => Add(child, UiLayer.HUD);

    /// <summary>Remove a child regardless of which layer it lives in. Use this
    /// instead of <c>root.Children.Remove(x)</c> — that direct mutation now
    /// only sees the layer containers, not user-added widgets.</summary>
    public void Remove(Element child) => child.RemoveFromParent();

    /// <summary>Toggle every widget in a layer on/off in one call. Use
    /// from modal hosts to fully hide the underlying HUD when a modal
    /// opens — the engine's <see cref="SpriteLayer"/> tiers flatten
    /// across UI-tree depth, so text from "lower" layers can otherwise
    /// punch through an overlay panel's backdrop.</summary>
    public void SetLayerVisible(UiLayer layer, bool visible)
        => _layers[(int)layer].Visible = visible;

    /// <summary>Root's rect is the *logical* viewport (post-scale). Children anchor
    /// against this — so on ScaleWithScreenSize, anchors compute against the reference
    /// resolution and the whole tree visually scales when actual viewport differs.</summary>
    public override (float X, float Y, float W, float H) AbsoluteRect()
    {
        ComputeScale();
        return (0, 0, _ctx.ViewportW / CurrentScale, _ctx.ViewportH / CurrentScale);
    }

    /// <summary>True if the cursor is currently over any pickable Gui element. Use
    /// this from your game's input handlers to swallow clicks that landed on UI
    /// (otherwise a click on a button also passes through to whatever map/world
    /// hit-test you're running). Re-runs a fresh hit-test, so it reflects the
    /// current mouse position regardless of frame timing.</summary>
    public bool IsCursorOverUI
    {
        get
        {
            ComputeScale();
            var m = InputManager.GetMousePosition();
            return HitTest(this, m.X / CurrentScale, m.Y / CurrentScale) != null;
        }
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

    public void Update(Camera camera, int viewportW, int viewportH)
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

        // SuppressNextClick: arm the swallow on this frame's MouseClicked, then
        // skip down/up/click dispatches until the release ends the sequence.
        if (SuppressNextClick && _ctx.MouseClicked)
        {
            _swallowingClick = true;
            SuppressNextClick = false;
        }

        if (_ctx.MouseClicked && current != null && !_swallowingClick)
        {
            current.DispatchPointerDown(pe);
            _pressTarget = current;
        }
        if (_pressTarget != null && _ctx.MouseDown && (pe.DeltaX != 0 || pe.DeltaY != 0))
            _pressTarget.DispatchPointerDrag(pe);
        if (_ctx.MouseReleased)
        {
            if (_swallowingClick)
            {
                _swallowingClick = false;
            }
            else if (_pressTarget != null)
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

        LayoutInspector.Update(this, _ctx);
    }

    public void Render(Camera camera, int viewportW, int viewportH)
    {
        _ctx.Camera = camera;
        _ctx.ViewportW = viewportW;
        _ctx.ViewportH = viewportH;
        _ctx.DefaultFont = Font;
        ComputeScale();
        _ctx.UIScale = CurrentScale;
        Render(_ctx);
    }

    /// <summary>Override the base tree walk so each UiLayer flushes its
    /// SpriteBatcher queues (UIBack + UI + Overlay) before the next
    /// layer starts drawing. Without this flush, higher-UiLayer BG
    /// quads would still get overdrawn by lower-UiLayer TEXT — because
    /// SpriteLayer.UI flushes globally AFTER UIBack, regardless of
    /// which UiLayer container the widget lives in. The flush turns
    /// each UiLayer into its own painter's-algorithm pass so a Panels
    /// widget always fully occludes any HUD content beneath it.</summary>
    public override void Render(Context ctx)
    {
        if (!Visible) return;
        for (int i = 0; i < Behaviors.Count; i++) Behaviors[i].Render(ctx, this);
        // Walk each layer container in enum order, flushing between.
        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Render(ctx);
            SpriteBatcher.Flush();
        }
        LayoutInspector.Render(this, ctx);
        SpriteBatcher.Flush();
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
