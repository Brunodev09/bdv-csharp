using System;
using System.Globalization;

namespace BdvEngine.Gui;

/// <summary>Sizing modes used by the flex / grid layout engine. Only
/// applies when a container assigns children sizes automatically —
/// <see cref="Panel"/>/<see cref="Element"/> in absolute mode still
/// uses the raw <see cref="Element.Width"/> / <see cref="Element.Height"/>
/// pixel values.</summary>
public enum SizeMode
{
    /// <summary>Fixed pixel size — the same as writing a number
    /// directly.</summary>
    Fixed,
    /// <summary>Fit content — labels take their text width, containers
    /// take the sum of their children (plus padding + gap).</summary>
    Auto,
    /// <summary>Percentage of the parent's available inner size on
    /// this axis. Value 0..100.</summary>
    Percent,
    /// <summary>Fraction of remaining space (like CSS <c>1fr</c>).
    /// Multiple flex siblings split leftover space in proportion to
    /// their Value.</summary>
    Flex,
}

/// <summary>Compact size descriptor — mode + one float. Used for
/// per-axis sizing on children of flex / grid containers.</summary>
public readonly struct Sizing
{
    public readonly SizeMode Mode;
    public readonly float Value;

    public Sizing(SizeMode mode, float value) { Mode = mode; Value = value; }

    public static readonly Sizing Auto      = new(SizeMode.Auto, 0);
    public static Sizing Fixed(float px)    => new(SizeMode.Fixed, px);
    public static Sizing Percent(float pct) => new(SizeMode.Percent, pct);
    public static Sizing Flex(float fr = 1) => new(SizeMode.Flex, fr);

    /// <summary>Parse a CSS-ish string. Accepts:
    ///   <list type="bullet">
    ///     <item>plain number: <c>"108"</c> → 108 px</item>
    ///     <item>ending in <c>px</c>: <c>"108px"</c> → 108 px</item>
    ///     <item>ending in <c>%</c>: <c>"50%"</c> → 50 % of parent</item>
    ///     <item>ending in <c>fr</c>: <c>"1fr"</c> → 1 unit of flex</item>
    ///     <item><c>"auto"</c> → content-sized</item>
    ///   </list></summary>
    public static Sizing Parse(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s == "auto") return Auto;
        if (s.EndsWith("%",  StringComparison.Ordinal))  return Percent(ParseFloat(s[..^1]));
        if (s.EndsWith("fr", StringComparison.Ordinal))  return Flex(ParseFloat(s[..^2]));
        if (s.EndsWith("px", StringComparison.Ordinal))  return Fixed(ParseFloat(s[..^2]));
        return Fixed(ParseFloat(s));
    }

    private static float ParseFloat(string s)
        => float.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>Convenience container for 4-side pixel padding / margin.
/// Reads like CSS: single value = all sides, two = vertical/horizontal,
/// four = top/right/bottom/left.</summary>
public readonly struct Insets
{
    public readonly float Top, Right, Bottom, Left;
    public Insets(float top, float right, float bottom, float left)
    { Top = top; Right = right; Bottom = bottom; Left = left; }

    public static readonly Insets Zero = new(0, 0, 0, 0);
    public static Insets All(float v)       => new(v, v, v, v);
    public static Insets VH(float v, float h) => new(v, h, v, h);
}
