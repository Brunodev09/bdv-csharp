namespace BdvEngine.Gui;

/// <summary>
/// On/off toggle: a square box on the left and a label to the right. The clickable
/// area is the whole <see cref="Element.Width"/>×<see cref="Element.Height"/> rect
/// so the label is part of the hit target. Toggles on mouse-up inside.
/// </summary>
public sealed class Checkbox : Element
{
    public string LabelText;
    public bool Value;
    public Color BoxIdle    = new( 40,  45,  60, 220);
    public Color BoxHover   = new( 60,  70,  90, 235);
    public Color BoxPressed = new( 90, 100, 130, 245);
    public Color CheckColor = new( 95, 200, 140, 255);
    public Color BorderColor = new(100, 110, 130, 255);
    public Color TextColor  = new(230, 235, 245, 255);
    public Font? Font;
    public float TextScale = 0.32f;
    public Action<bool>? OnChangeCallback;

    private bool _pressed;

    public Checkbox(float x, float y, float w, float h, string label, bool value)
    { X = x; Y = y; Width = w; Height = h; LabelText = label; Value = value; }

    public Checkbox OnChange(Action<bool> cb) { OnChangeCallback = cb; return this; }
    public Checkbox WithFont(Font f, float scale = 0.32f) { Font = f; TextScale = scale; return this; }
    public Checkbox WithTextColor(Color c) { TextColor = c; return this; }

    public override void Update(Context ctx)
    {
        if (!Visible || !Enabled) { _pressed = false; base.Update(ctx); return; }
        bool over = ctx.Hovered == this;
        if (over && ctx.MouseClicked) _pressed = true;
        if (_pressed && ctx.MouseReleased)
        {
            if (over) { Value = !Value; OnChangeCallback?.Invoke(Value); }
            _pressed = false;
        }
        if (!ctx.MouseDown) _pressed = false;
        base.Update(ctx);
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (ax, ay) = AbsolutePosition();
        float ws = ctx.WorldScale;
        float box = Height; // square box on the left, height = row height

        var w0 = ctx.ToWorld(ax, ay);
        Color bg = _pressed ? BoxPressed : (ctx.Hovered == this ? BoxHover : BoxIdle);
        SpriteBatcher.DrawSolid(w0.X, w0.Y, box * ws, box * ws, bg, SpriteLayer.UIBack);

        if (Value)
        {
            float pad = box * 0.22f;
            var w1 = ctx.ToWorld(ax + pad, ay + pad);
            SpriteBatcher.DrawSolid(w1.X, w1.Y, (box - pad * 2f) * ws, (box - pad * 2f) * ws, CheckColor, SpriteLayer.UIBack);
        }

        Draw.RectOutline(w0.X, w0.Y, box * ws, box * ws, BorderColor);

        var font = Font ?? ctx.DefaultFont;
        if (font != null)
        {
            float baseline = ay + Height * 0.5f + font.Ascent * TextScale * 0.32f;
            TextRenderer.DrawScreen(font, LabelText, ax + box + 8f, baseline,
                TextScale, TextColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                default, TextAlign.Left);
        }
        base.Render(ctx);
    }
}
