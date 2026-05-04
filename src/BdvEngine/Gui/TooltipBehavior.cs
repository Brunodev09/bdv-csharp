namespace BdvEngine.Gui;

/// <summary>
/// Hover-triggered text tooltip. Attach to any pickable element and pass the text:
/// <code>button.AddBehavior(new TooltipBehavior("Generates a fresh world."));</code>
///
/// Shows after <see cref="Delay"/> seconds of continuous hover, anchored just below
/// the owner element (auto-flips above if it would go off the bottom edge, auto-clamps
/// horizontally to stay on-screen). Disappears immediately on pointer exit.
///
/// Renders on the UI layer so it sits above panel backgrounds; for sibling widgets
/// added *after* the tooltip's owner, layering follows insertion order — usually fine
/// since tooltips show transiently and the active hover element is on top of where the
/// cursor is anyway.
/// </summary>
public sealed class TooltipBehavior : IElementBehavior
{
    public string Text;
    public float Delay = 0.5f;
    public float TextScale = 0.26f;
    public float Padding = 8f;
    public Font? Font;
    public Color Background = new(15, 18, 26, 240);
    public Color Border     = new(120, 130, 160, 255);
    public Color TextColor  = new(235, 240, 250, 255);

    private bool _hovered;
    private float _hoverStart = -1f;

    public TooltipBehavior(string text, float delay = 0.5f)
    { Text = text; Delay = delay; }

    public TooltipBehavior WithFont(Font f, float scale = 0.26f) { Font = f; TextScale = scale; return this; }
    public TooltipBehavior WithColors(Color bg, Color border, Color text)
    { Background = bg; Border = border; TextColor = text; return this; }

    public void OnPointerEnter(Element owner, PointerEvent e)
    {
        _hovered = true;
        _hoverStart = Time.TotalF;
    }

    public void OnPointerExit(Element owner, PointerEvent e)
    {
        _hovered = false;
        _hoverStart = -1f;
    }

    public void Render(Context ctx, Element owner)
    {
        if (!_hovered || _hoverStart < 0f) return;
        if (Time.TotalF - _hoverStart < Delay) return;

        var font = Font ?? ctx.DefaultFont;
        if (font == null || string.IsNullOrEmpty(Text)) return;

        // Box dims from text measurement + padding.
        float textW = font.Measure(Text) * TextScale;
        float textH = font.LineAdvance * TextScale;
        float w = textW + Padding * 2f;
        float h = textH + Padding * 2f;

        // Position just below the owner, centered horizontally.
        var (rx, ry, rw, rh) = owner.AbsoluteRect();
        float tx = rx + (rw - w) * 0.5f;
        float ty = ry + rh + 6f;
        // Clamp to viewport; flip above if it would overflow the bottom.
        if (tx < 4f) tx = 4f;
        if (tx + w > ctx.ViewportW - 4f) tx = ctx.ViewportW - 4f - w;
        if (ty + h > ctx.ViewportH - 4f) ty = ry - h - 6f;

        float ws = ctx.WorldScale;
        var w0 = ctx.ToWorld(tx, ty);
        // UIBack so background renders before UI-layer text — otherwise the white-pixel
        // batch (created late, when the tooltip pops) flushes after the font batch and
        // covers the tooltip's own text.
        SpriteBatcher.DrawSolid(w0.X, w0.Y, w * ws, h * ws, Background, SpriteLayer.UIBack);
        Draw.RectOutline(w0.X, w0.Y, w * ws, h * ws, Border);

        float baseline = ty + Padding + font.Ascent * TextScale;
        TextRenderer.DrawScreen(font, Text, tx + Padding, baseline,
            TextScale, TextColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH);
    }
}
