namespace BdvEngine;

/// <summary>
/// Per-glyph animation parameters. Defaults (all zero / false) render as static text.
/// Effects compose freely — set Wave + Rainbow + Pop together for a juice-y banner.
///
/// Time semantics: if Time is 0, TextRenderer reads <see cref="BdvEngine.Time.TotalF"/>.
/// Override for per-instance timelines (e.g., damage popups counting from spawn).
/// </summary>
public struct TextAnim
{
    /// <summary>Override clock — if non-zero, used instead of the global Time.TotalF.</summary>
    public float Time;
    /// <summary>Per-glyph time offset in seconds; gives a "wave running through letters" feel.</summary>
    public float Stagger;

    /// <summary>Vertical sine offset (px). 0 disables.</summary>
    public float WaveAmplitude;
    /// <summary>Wave angular speed (rad/sec). Default 6 ≈ 1 cycle/sec.</summary>
    public float WaveSpeed;

    /// <summary>Scale pulse amount, 0..1. 0.2 = pulses between 0.8× and 1.2×.</summary>
    public float PopAmount;
    /// <summary>Pop angular speed.</summary>
    public float PopSpeed;

    /// <summary>Random per-frame jitter (px). 0 disables.</summary>
    public float Shake;

    /// <summary>Cycle hue per glyph over time when true. Multiplies the user's color.</summary>
    public bool Rainbow;
    /// <summary>Hue cycle speed (rad/sec).</summary>
    public float RainbowSpeed;

    public static TextAnim None => default;

    public static TextAnim Wave(float amplitude = 6f, float speed = 6f, float stagger = 0.08f)
        => new() { WaveAmplitude = amplitude, WaveSpeed = speed, Stagger = stagger };

    public static TextAnim Pop(float amount = 0.25f, float speed = 8f, float stagger = 0.06f)
        => new() { PopAmount = amount, PopSpeed = speed, Stagger = stagger };

    public static TextAnim Shaky(float pixels = 2f) => new() { Shake = pixels };

    public static TextAnim RainbowText(float speed = 3f, float stagger = 0.15f)
        => new() { Rainbow = true, RainbowSpeed = speed, Stagger = stagger };
}
