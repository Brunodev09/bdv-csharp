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

    public Label(float x, float y, string text)
    {
        X = x; Y = y; Text = text; Pickable = false;
    }

    public Label WithFont(Font font) { Font = font; return this; }
    public Label WithScale(float scale) { Scale = scale; return this; }
    public Label WithColor(Color color) { TextColor = color; return this; }
    public Label WithAlign(TextAlign align) { Align = align; return this; }
    public Label WithAnim(TextAnim anim) { Anim = anim; return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var font = Font ?? ctx.DefaultFont;
        if (font == null) return;
        var (ax, ay) = AbsolutePosition();
        // Treat (X, Y) as the text's top-left; shift by the font ascent to get the baseline.
        TextRenderer.DrawScreen(font, Text, ax, ay + font.Ascent * Scale, Scale, TextColor,
            ctx.Camera, ctx.ViewportW, ctx.ViewportH, Anim, Align);
        base.Render(ctx);
    }
}
