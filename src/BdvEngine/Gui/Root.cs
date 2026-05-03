namespace BdvEngine.Gui;

/// <summary>
/// Top-level container. Owns the per-frame Context, performs hit testing, and
/// dispatches Update / Render down the tree. Game code constructs one Root, builds
/// the widget tree under it, then calls Root.Update / Root.Render each frame.
/// </summary>
public sealed class Root : Element
{
    public Font? Font;
    private bool _prevMouseDown;
    private readonly Context _ctx = new();

    public Root() { Pickable = false; X = 0; Y = 0; Width = 0; Height = 0; }

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
        _ctx.MouseX = mouse.X;
        _ctx.MouseY = mouse.Y;
        _ctx.MouseDown = down;
        _ctx.MouseClicked = down && !_prevMouseDown;
        _ctx.MouseReleased = !down && _prevMouseDown;

        _ctx.Hovered = HitTest(this, _ctx.MouseX, _ctx.MouseY);
        // Capturing element (e.g., slider being dragged) overrides hover.
        if (_ctx.Capturing != null) _ctx.Hovered = _ctx.Capturing;

        Update(_ctx);

        if (_ctx.MouseReleased) _ctx.Capturing = null;
        _prevMouseDown = down;
    }

    public void Render(Camera2D camera, int viewportW, int viewportH)
    {
        _ctx.Camera = camera;
        _ctx.ViewportW = viewportW;
        _ctx.ViewportH = viewportH;
        _ctx.DefaultFont = Font;
        Render(_ctx);
    }

    /// <summary>Depth-first reverse-order pick — last drawn is topmost.</summary>
    private static Element? HitTest(Element el, float sx, float sy)
    {
        if (!el.Visible) return null;
        for (int i = el.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(el.Children[i], sx, sy);
            if (hit != null) return hit;
        }
        return el.Pickable && el.ContainsScreenPoint(sx, sy) ? el : null;
    }
}
