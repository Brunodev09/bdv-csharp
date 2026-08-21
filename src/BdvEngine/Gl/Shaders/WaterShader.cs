namespace BdvEngine;

/// <summary>
/// Sprite shader for animated water tiles. Samples the tile texture
/// (a water cell from the biomes atlas) and modulates it with two
/// sin-waves driven by world position + a u_time uniform so the
/// shimmer crawls across the surface instead of strobing in place.
///
/// Drop-in for the default batch sprite shader (same vertex layout +
/// u_proj + u_zScale) plus one extra uniform the host updates each
/// frame:
///
/// <list type="bullet">
///   <item><c>u_time</c> float — seconds since app start. Drives the
///   wave phase. Refresh once per frame from
///   <c>Engine.ElapsedTime</c>; the shader takes care of wrapping so
///   long-session drift is fine.</item>
/// </list>
///
/// World-position-anchored (not screen-anchored): waves stay glued to
/// the water as the camera pans, so a stationary pawn sees the same
/// patch of ripples flow past them. Screen-anchored ripples would
/// freeze on pan and look like a glitch.
/// </summary>
public sealed class WaterShader : Shader
{
    public WaterShader() : base("water")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;
layout(location = 2) in vec4 a_color;

uniform mat4 u_proj;
uniform float u_zScale;
uniform float u_zBias;

out vec2 v_textCoord;
out vec4 v_color;
out vec2 v_worldPos;     // <-- so the fragment shader can phase-shift per tile

void main()
{
    gl_Position = u_proj * vec4(a_pos.xy, 0.0, 1.0);
    // Per-quad depth via sortY packed into a_pos.z (matches the
    // default batch sprite shader). u_zScale = 0 on Ground layer makes
    // this a no-op.
    gl_Position.z = 1.0 - (a_pos.z - u_zBias) * u_zScale;
    v_textCoord = a_textCoord;
    v_color = a_color;
    v_worldPos = a_pos.xy;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;
in vec4 v_color;
in vec2 v_worldPos;

uniform sampler2D u_diffuse;
uniform float u_time;

out vec4 fragColor;

void main()
{
    vec4 base = texture(u_diffuse, v_textCoord);

    // Three crossed sin-waves at low spatial frequency — produces a
    // smooth, broad ripple pattern that reads as gentle swells.
    // Lower frequencies (0.06–0.12) span many tiles per cycle instead
    // of the previous high-frequency (0.22) bands that printed as
    // visible dot grids at small zoom levels.
    float w1 = sin(v_worldPos.x * 0.06 + v_worldPos.y * 0.04 + u_time * 0.9);
    float w2 = sin(v_worldPos.x * 0.04 - v_worldPos.y * 0.07 + u_time * 0.7);
    float w3 = sin((v_worldPos.x + v_worldPos.y) * 0.09 + u_time * 1.2);
    float ripple = (w1 + w2 + w3) / 3.0;              // [-1, 1] normalised

    // Brightness wobble — smooth, continuous, no hard caps. Reads as
    // a moving light on the water surface rather than discrete dots.
    // Range [0.92, 1.08] keeps the biome tint dominant.
    float bright = 1.0 + ripple * 0.08;
    vec3 lit = base.rgb * bright;

    // Subtle hue shift toward a lighter cyan in the bright phases and
    // a slightly darker blue in the troughs — gives the surface depth
    // without ever painting hard white spots. Mix factor stays tiny
    // (max 0.12) so the water always reads as the underlying biome
    // colour, just *moving*.
    vec3 crestTint  = vec3(0.55, 0.80, 0.95);
    vec3 troughTint = vec3(0.05, 0.15, 0.35);
    float crestMix  = smoothstep(0.2, 0.9, ripple)  * 0.10;
    float troughMix = smoothstep(0.2, 0.9, -ripple) * 0.08;
    vec3 outRgb = mix(lit, crestTint,  crestMix);
    outRgb      = mix(outRgb, troughTint, troughMix);

    fragColor = vec4(outRgb, base.a) * v_color;
}";
}
