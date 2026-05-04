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

    public override void OnPointerDown (PointerEvent e) { if (Enabled) _pressed = true; }
    public override void OnPointerUp   (PointerEvent e) { _pressed = false; }
    public override void OnPointerExit (PointerEvent e) { /* keep _pressed true so a drag-away cancels at click time */ }
    public override void OnPointerClick(PointerEvent e) { if (Enabled) OnClickCallback?.Invoke(); }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = RenderRect();
        Color bg = !Enabled ? BgDisabled
                  : _pressed ? BgPressed
                  : ctx.Hovered == this ? BgHover
                  : BgIdle;
        float a = EffectiveAlpha;
        var w = ctx.ToWorld(rx, ry);
        float ws = ctx.WorldScale;
        SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Mul(bg, a), SpriteLayer.UIBack);

        var font = Font ?? ctx.DefaultFont;
        if (font != null)
        {
            float labelScale = TextScale * RenderScale;
            float baseline = ry + rh * 0.5f + font.Ascent * labelScale * 0.32f;
            TextRenderer.DrawScreen(font, Label, rx + rw * 0.5f, baseline,
                labelScale, GuiHelpers.Mul(TextColor, a), ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                default, TextAlign.Center);
        }
        base.Render(ctx);
    }
}
