using System.Numerics;

namespace BdvEngine;

/// <summary>Curve used to bring unbounded HDR light into the 0..1 a display can show.</summary>
public enum TonemapMode
{
    /// <summary>Clip anything above 1. Everything bright turns into a flat white blob, which is
    /// exactly what makes un-tonemapped renders look cheap.</summary>
    None,
    /// <summary>x/(1+x). Cheap, never clips, but desaturates highlights and lifts the whole image
    /// slightly. Fine for stylised work.</summary>
    Reinhard,
    /// <summary>Filmic ACES approximation. Keeps colour in bright regions and holds contrast in the
    /// shadows — the default, and the single biggest reason a render stops looking flat.</summary>
    Aces,
}

/// <summary>
/// Post-processing for the 3D path: render the scene to an HDR buffer, then bloom, tonemap and
/// grade it on the way to the screen.
///
/// <para>Reached through <c>World.Environment.PostFx</c>, alongside <c>Shadows</c> and
/// <c>Fog</c>:</para>
/// <code>
/// w.Environment.PostFx.Enabled = true;
/// w.Environment.PostFx.Exposure = 1.2f;
/// w.Environment.PostFx.Bloom.Threshold = 1.1f;
/// w.Environment.PostFx.Vignette = 0.3f;
/// </code>
///
/// <para><b>This is distinct from the 2D <see cref="Bloom"/> class</b>, which stays as it is.
/// That one takes emissive content the game draws explicitly; this one finds bright pixels in the
/// rendered scene by luminance. Neither can do the other's job, and merging them would give both a
/// worse API.</para>
/// </summary>
public sealed class PostFxSettings
{
    /// <summary>Master switch. Off means the scene renders straight to the window exactly as it did
    /// before this existed — no HDR buffer allocated, no extra passes, no cost.</summary>
    public bool Enabled;

    /// <summary>Linear multiplier applied before tonemapping. Above 1 brightens, below darkens.
    /// This is the knob to reach for first: it is what makes a scene feel lit rather than
    /// illuminated.</summary>
    [Range(0.05f, 8f)] public float Exposure = 1f;

    public TonemapMode Tonemap = TonemapMode.Aces;

    public BloomSettings Bloom { get; } = new();

    // ── grading, applied after the tonemap ──────────────────────────────────
    /// <summary>1 is neutral. Pivots around mid-grey, so raising it darkens shadows and brightens
    /// highlights rather than just scaling everything.</summary>
    [Range(0f, 3f)] public float Contrast = 1f;

    /// <summary>1 is neutral, 0 is greyscale, above 1 pushes colour. Useful in small doses; a
    /// desaturated scene reads as cold or bleak without touching a single light.</summary>
    [Range(0f, 3f)] public float Saturation = 1f;

    /// <summary>Per-channel multiplier. Warm firelight is roughly (1.05, 1.0, 0.92); a cold night
    /// is the inverse.</summary>
    public Vector3 Tint = Vector3.One;

    /// <summary>Corner darkening, 0..1. Subtle values (0.2-0.4) pull the eye to the centre; it is
    /// the cheapest way to make a frame feel composed rather than merely rendered.</summary>
    [Range(0f, 1f)] public float Vignette;

    /// <summary>Output gamma. 2.2 is the standard sRGB-ish encode. Change only if the whole image
    /// looks washed out or crushed.</summary>
    [Range(1f, 3f)] public float Gamma = 2.2f;
}

/// <summary>Luminance-driven bloom over the rendered scene.</summary>
public sealed class BloomSettings
{
    public bool Enabled = true;

    /// <summary>Luminance above which a pixel starts to glow. Meaningful only because the scene is
    /// rendered in HDR: with an 8-bit buffer nothing can exceed 1, so no threshold above 1 would
    /// ever be crossed and bloom could only ever smear things that were already visible.</summary>
    [Range(0f, 10f)] public float Threshold = 1.0f;

    /// <summary>Width of the soft ramp below <see cref="Threshold"/>. A hard cut makes bloom pop on
    /// and off as a highlight drifts across the threshold; the knee is what stops that flickering.
    /// </summary>
    [Range(0f, 2f)] public float Knee = 0.5f;

    /// <summary>How much of the blurred result is added back. 0 is off.</summary>
    [Range(0f, 5f)] public float Intensity = 0.6f;

    /// <summary>Blur ping-pong pairs. Each one roughly doubles the halo width. 1-4 is sane; beyond
    /// that use a bigger <see cref="Downsample"/> instead, which is far cheaper per unit of blur.
    /// </summary>
    [Range(1, 6)] public int Iterations = 3;

    /// <summary>Resolution divisor for the blur chain. 2 is a crisp halo, 4 is soft and a quarter
    /// of the cost.</summary>
    [Range(1, 8)] public int Downsample = 2;
}
