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
    /// <summary>Horizontal alignment of the button label. Defaults to Center
    /// (classic button look). Use Left for list-row style buttons.</summary>
    public TextAlign Align = TextAlign.Center;
    /// <summary>Padding inset from the edge for non-center alignments.</summary>
    public float TextPadding = 8f;
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
    public Button WithAlign(TextAlign align) { Align = align; return this; }
    public Button WithTextPadding(float pad) { TextPadding = pad; return this; }

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
        var w = ctx.ToWorld(rx, ry);
        float ws = ctx.WorldScale;
        SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Apply(bg, this), SpriteLayer.UIBack);

        var font = Font ?? ctx.DefaultFont;
        if (font != null && !string.IsNullOrEmpty(Label))
        {
            float labelScale = TextScale * RenderScale;
            // Auto-shrink the label so it never visually spills past
            // the button's edges. Matches Label.AutoFit's behaviour —
            // single-line button labels are the common case, and
            // letting them overflow into adjacent buttons (e.g. tight
            // diplomacy-matrix cells) looks broken. Floored at 0.10
            // for legibility.
            float avail = rw - 2f * TextPadding;
            if (avail > 0f)
            {
                float measured = font.Measure(Label) * labelScale;
                if (measured > avail) labelScale *= avail / measured;
                if (labelScale < 0.10f) labelScale = 0.10f;
            }
            float baseline = ry + rh * 0.5f + font.Ascent * labelScale * 0.32f;
            float tx = Align switch
            {
                TextAlign.Left  => rx + TextPadding,
                TextAlign.Right => rx + rw - TextPadding,
                _               => rx + rw * 0.5f,
            };
            TextRenderer.DrawScreen(font, Label, tx, baseline,
                labelScale, GuiHelpers.Apply(TextColor, this), ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                default, Align);
        }
        base.Render(ctx);
    }
}
