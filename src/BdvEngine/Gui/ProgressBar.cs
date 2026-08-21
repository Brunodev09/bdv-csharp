using System;

namespace BdvEngine.Gui;

/// <summary>
/// Filled horizontal bar — track + fill + optional centered text. Use
/// for HP / hunger / progress / load indicators instead of ASCII like
/// <c>[####  ]</c> which doesn't scale, doesn't tint, and looks bad
/// on non-monospace fonts.
///
/// <see cref="Value"/> and <see cref="Max"/> drive the fill ratio.
/// <see cref="FillColor"/> tints the filled region; the track stays
/// at <see cref="TrackColor"/>. Optional <see cref="Label"/> renders
/// centered over the bar in <see cref="LabelColor"/>.
/// </summary>
public sealed class ProgressBar : Panel
{
    public float Value;
    public float Max = 1f;
    public Color TrackColor = new(40, 46, 60, 255);
    public Color FillColor  = new(80, 160, 100, 255);
    public Color BorderColor = new(90, 105, 140, 255);
    public string? Label;
    public Color LabelColor = new(255, 255, 255, 255);
    public Font? Font;
    public float LabelScale = 0.20f;

    public ProgressBar(float x, float y, float w, float h, float max = 100f) : base(x, y, w, h)
    {
        Max = max;
        Pickable = false;
        WithBackground(TrackColor).WithBorder(BorderColor, 1f);
    }

    public ProgressBar WithValue(float v)    { Value = v;    return this; }
    public ProgressBar WithMax(float m)      { Max   = m;    return this; }
    public ProgressBar WithFill(Color c)     { FillColor = c; return this; }
    public ProgressBar WithLabel(string? s, Color? color = null)
    {
        Label = s; if (color.HasValue) LabelColor = color.Value; return this;
    }
    public ProgressBar WithFont(Font f, float scale = 0.20f) { Font = f; LabelScale = scale; return this; }

    public override void Render(Context ctx)
    {
        // Background (track) + border come from the Panel base render.
        base.Render(ctx);
        if (!Visible) return;

        var (rx, ry, rw, rh) = AbsoluteRect();
        float ratio = Max <= 0 ? 0 : MathF.Max(0f, MathF.Min(1f, Value / Max));
        if (ratio <= 0f) goto LABEL;

        // Inset by 1 px so the fill doesn't paint over the border.
        float inset = 1f;
        float innerW = MathF.Max(0f, rw - inset * 2);
        float fillW = innerW * ratio;
        if (fillW > 0)
        {
            float ws = ctx.WorldScale;
            var topLeft = ctx.ToWorld(rx + inset, ry + inset);
            float a = EffectiveAlpha;
            SpriteBatcher.DrawSolid(topLeft.X, topLeft.Y, fillW * ws, (rh - inset * 2) * ws,
                GuiHelpers.Mul(FillColor, a), SpriteLayer.UI);
        }

    LABEL:
        if (!string.IsNullOrEmpty(Label))
        {
            var font = Font ?? ctx.DefaultFont;
            if (font != null)
            {
                float w = font.Measure(Label) * LabelScale;
                float lx = rx + (rw - w) * 0.5f;
                float ly = ry + (rh + font.Ascent * LabelScale) * 0.5f - font.Ascent * LabelScale * 0.5f;
                TextRenderer.DrawScreen(font, Label, lx, ly, LabelScale,
                    GuiHelpers.Apply(LabelColor, this),
                    ctx.Camera, ctx.ViewportW, ctx.ViewportH);
            }
        }
    }
}
