namespace BdvEngine.Gui;

/// <summary>
/// Static or animated text. Reads the default font from the Context unless one is
/// set explicitly. Width/Height aren't required — text spans whatever the font
/// metrics produce — but if you set them they participate in hit testing (off by
/// default since labels usually shouldn't intercept clicks).
/// </summary>
public class Label : Element
{
    public string Text;
    public Color TextColor = Color.White;
    public float Scale = 0.4f;
    public TextAlign Align = TextAlign.Left;
    public TextAnim Anim;
    public Font? Font;
    /// <summary>If true, wrap to the element's Width by inserting line breaks.</summary>
    public bool WordWrap;
    /// <summary>If true, parse simple inline tags: &lt;color=#rrggbb&gt;…&lt;/color&gt;.</summary>
    public bool RichText;

    public Label(float x, float y, string text)
    {
        X = x; Y = y; Text = text; Pickable = false;
    }

    public Label WithFont(Font font) { Font = font; return this; }
    public Label WithScale(float scale) { Scale = scale; return this; }
    public Label WithColor(Color color) { TextColor = color; return this; }
    public Label WithAlign(TextAlign align) { Align = align; return this; }
    public Label WithAnim(TextAnim anim) { Anim = anim; return this; }
    public Label Wrap(bool wrap = true) { WordWrap = wrap; return this; }
    public Label Rich(bool rich = true) { RichText = rich; return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var font = Font ?? ctx.DefaultFont;
        if (font == null) return;
        var (rx, ry, rw, _) = AbsoluteRect();
        Color baseColor = GuiHelpers.Mul(TextColor, EffectiveAlpha);

        if (RichText)
        {
            // Single-line rich (color tags) — wrap not supported in this version.
            TextRenderer.DrawScreenRich(font, Text, rx, ry + font.Ascent * Scale, Scale,
                baseColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH);
            base.Render(ctx);
            return;
        }

        if (WordWrap && rw > 0)
        {
            float y = ry + font.Ascent * Scale;
            foreach (var line in TextRenderer.Wrap(font, Text, rw, Scale))
            {
                TextRenderer.DrawScreen(font, line, rx, y, Scale, baseColor,
                    ctx.Camera, ctx.ViewportW, ctx.ViewportH, Anim, Align);
                y += font.LineAdvance * Scale;
            }
        }
        else
        {
            TextRenderer.DrawScreen(font, Text, rx, ry + font.Ascent * Scale, Scale,
                baseColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH, Anim, Align);
        }
        base.Render(ctx);
    }
}
