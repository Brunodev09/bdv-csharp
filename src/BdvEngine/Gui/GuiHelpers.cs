namespace BdvEngine.Gui;

internal static class GuiHelpers
{
    /// <summary>Multiply a color's alpha channel by k (clamped 0..255). Used by
    /// widgets to apply Element.EffectiveAlpha during rendering.</summary>
    public static Color Mul(Color c, float k)
    {
        if (k >= 1f) return c;
        if (k <= 0f) return new Color(c.R, c.G, c.B, 0);
        return new Color(c.R, c.G, c.B, (byte)Math.Clamp(c.A * k, 0f, 255f));
    }

    /// <summary>Channel-wise multiply two colors (0..255 → normalized 0..1
    /// multiplied → back to 0..255). White is the identity. Used for
    /// modulate/tint cascades — every ancestor's Modulate gets multiplied
    /// into descendants' rendered colors.</summary>
    public static Color MulColor(Color a, Color b)
    {
        // Fast paths for the common no-op case (most widgets never set Modulate).
        if (a.R == 255 && a.G == 255 && a.B == 255 && a.A == 255) return b;
        if (b.R == 255 && b.G == 255 && b.B == 255 && b.A == 255) return a;
        return new Color(
            (byte)((a.R * b.R) / 255),
            (byte)((a.G * b.G) / 255),
            (byte)((a.B * b.B) / 255),
            (byte)((a.A * b.A) / 255));
    }

    /// <summary>Apply an element's full render-time color stack to a base
    /// color: channel-wise multiply by the element's <see cref="Element.EffectiveModulate"/>,
    /// then alpha-multiply by <see cref="Element.EffectiveAlpha"/>. One call
    /// covers both knobs so renderers stay one-liners.</summary>
    public static Color Apply(Color baseColor, Element e)
        => Mul(MulColor(baseColor, e.EffectiveModulate), e.EffectiveAlpha);
}
