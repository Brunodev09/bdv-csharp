namespace BdvEngine.Gui;

public enum ArrowDirection { Up, Down, Left, Right }

/// <summary>
/// Square button with a triangle glyph instead of a label. Useful for steppers,
/// scrollbars, paginators, and minimap pan controls. Behaviour mirrors
/// <see cref="Button"/> — fires on mouse-up inside the bounds.
/// </summary>
public sealed class Arrow : Element
{
    public ArrowDirection Direction;
    public Color GlyphColor   = new(245, 245, 250, 255);
    public Color BgIdle       = new( 40,  45,  60, 220);
    public Color BgHover      = new( 65,  75,  95, 235);
    public Color BgPressed    = new( 95, 110, 145, 245);
    public Color BgDisabled   = new( 35,  35,  40, 180);
    public Action? OnClickCallback;

    private bool _pressed;

    public Arrow(float x, float y, float size, ArrowDirection dir)
    { X = x; Y = y; Width = Height = size; Direction = dir; }

    public Arrow OnClick(Action cb) { OnClickCallback = cb; return this; }
    public Arrow WithColors(Color idle, Color hover, Color pressed)
    { BgIdle = idle; BgHover = hover; BgPressed = pressed; return this; }
    public Arrow WithGlyphColor(Color c) { GlyphColor = c; return this; }

    public override void Update(Context ctx)
    {
        if (!Visible || !Enabled) { _pressed = false; base.Update(ctx); return; }
        bool over = ctx.Hovered == this;
        if (over && ctx.MouseClicked) _pressed = true;
        if (_pressed && ctx.MouseReleased)
        {
            if (over) OnClickCallback?.Invoke();
            _pressed = false;
        }
        if (!ctx.MouseDown) _pressed = false;
        base.Update(ctx);
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (ax, ay) = AbsolutePosition();
        Color bg = !Enabled ? BgDisabled
                  : _pressed ? BgPressed
                  : ctx.Hovered == this ? BgHover
                  : BgIdle;
        float ws = ctx.WorldScale;
        var bgW = ctx.ToWorld(ax, ay);
        SpriteBatcher.DrawSolid(bgW.X, bgW.Y, Width * ws, Height * ws, bg, SpriteLayer.UIBack);

        // Inset triangle by ~25% of the rect on each side, then convert to world coords.
        float pad = Width * 0.25f;
        float l = ax + pad, r = ax + Width - pad;
        float t = ay + pad, b = ay + Height - pad;
        float cx = (l + r) * 0.5f, cy = (t + b) * 0.5f;
        var wL  = ctx.ToWorld(l,  cy); var wR  = ctx.ToWorld(r,  cy);
        var wT  = ctx.ToWorld(cx, t);  var wB  = ctx.ToWorld(cx, b);
        var wTL = ctx.ToWorld(l,  t);  var wTR = ctx.ToWorld(r,  t);
        var wBL = ctx.ToWorld(l,  b);  var wBR = ctx.ToWorld(r,  b);
        switch (Direction)
        {
            case ArrowDirection.Up:    Draw.Triangle(wBL.X, wBL.Y, wBR.X, wBR.Y, wT.X, wT.Y, GlyphColor); break;
            case ArrowDirection.Down:  Draw.Triangle(wTL.X, wTL.Y, wTR.X, wTR.Y, wB.X, wB.Y, GlyphColor); break;
            case ArrowDirection.Left:  Draw.Triangle(wTR.X, wTR.Y, wBR.X, wBR.Y, wL.X, wL.Y, GlyphColor); break;
            case ArrowDirection.Right: Draw.Triangle(wTL.X, wTL.Y, wBL.X, wBL.Y, wR.X, wR.Y, GlyphColor); break;
        }
        base.Render(ctx);
    }
}
