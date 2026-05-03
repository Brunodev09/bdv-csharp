namespace BdvEngine.Gui;

/// <summary>
/// Rectangular clickable widget with three color states (idle / hover / pressed) plus
/// disabled. Centered text uses the context's default font unless overridden. The
/// click event fires on mouse-up *inside* the button, not on press — same convention
/// as native OS buttons, so users can drag away to cancel.
/// </summary>
public sealed class Button : Element
{
    public string Label;
    public Color BgIdle     = new(45, 50, 70, 230);
    public Color BgHover    = new(70, 80, 110, 240);
    public Color BgPressed  = new(95, 110, 150, 245);
    public Color BgDisabled = new(40, 40, 45, 200);
    public Color TextColor  = Color.White;
    public Font? Font;
    public float TextScale  = 0.4f;
    public Action? OnClickCallback;

    private bool _pressed;

    public Button(float x, float y, float w, float h, string label)
    { X = x; Y = y; Width = w; Height = h; Label = label; }

    public Button OnClick(Action cb) { OnClickCallback = cb; return this; }
    public Button WithFont(Font font, float scale = 0.4f) { Font = font; TextScale = scale; return this; }
    public Button WithTextColor(Color c) { TextColor = c; return this; }
    public Button WithColors(Color idle, Color hover, Color pressed)
    {
        BgIdle = idle; BgHover = hover; BgPressed = pressed; return this;
    }

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
        var (rx, ry, rw, rh) = RenderRect();
        Color bg = !Enabled ? BgDisabled
                  : _pressed ? BgPressed
                  : ctx.Hovered == this ? BgHover
                  : BgIdle;
        var w = ctx.ToWorld(rx, ry);
        float ws = ctx.WorldScale;
        SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws, bg, SpriteLayer.UIBack);

        var font = Font ?? ctx.DefaultFont;
        if (font != null)
        {
            // Scale the label with the button so a hover-pulse doesn't desync visually.
            float labelScale = TextScale * RenderScale;
            float baseline = ry + rh * 0.5f + font.Ascent * labelScale * 0.32f;
            TextRenderer.DrawScreen(font, Label, rx + rw * 0.5f, baseline,
                labelScale, TextColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                default, TextAlign.Center);
        }
        base.Render(ctx);
    }
}
