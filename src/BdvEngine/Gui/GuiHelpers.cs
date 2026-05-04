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
}
