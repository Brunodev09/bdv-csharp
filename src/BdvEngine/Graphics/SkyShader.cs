using System.Numerics;

namespace BdvEngine;

/// <summary>Procedural gradient sky. Off by default: turning it on REPLACES
/// <see cref="WorldEnvironment.Sky"/> as the background, and a flat clear colour is the right
/// answer for plenty of scenes (2D games, debug views, stylised looks).</summary>
public sealed class SkySettings
{
    public bool Enabled;

    /// <summary>Colour at the horizon, where most of the visible sky sits.</summary>
    public Vector3 Horizon = new(0.62f, 0.72f, 0.86f);

    /// <summary>Colour straight up.</summary>
    public Vector3 Zenith = new(0.20f, 0.40f, 0.76f);

    /// <summary>Colour below the horizon — what you see looking down past the edge of the world.
    /// Keeping it distinct from the horizon is what stops a terrain-less scene reading as a
    /// floating disc in a void.</summary>
    public Vector3 Ground = new(0.26f, 0.25f, 0.23f);

    /// <summary>Brightness of the glow around the sun. 0 disables it.</summary>
    public float SunGlow = 0.6f;
}

/// <summary>Distance fog. Also off by default — fog is an art direction choice, not a fix.</summary>
public sealed class FogSettings
{
    public bool Enabled;

    /// <summary>Exponential-squared falloff rate. Roughly, visibility ends around
    /// <c>2.5 / Density</c> world units: 0.01 fades out over ~250 units, 0.05 over ~50.</summary>
    public float Density = 0.012f;

    /// <summary>When true (and the sky is on) fog takes its colour from the sky in the direction
    /// being looked at, so distant geometry dissolves into the actual horizon rather than into a
    /// flat grey that only matches from one angle.</summary>
    public bool UseSkyColor = true;

    /// <summary>Colour used when <see cref="UseSkyColor"/> is off, or when the sky is disabled.</summary>
    public Vector3 Color = new(0.62f, 0.72f, 0.86f);
}

/// <summary>
/// Fullscreen gradient sky.
///
/// <para>Drawn as a screen-covering quad BEFORE the meshes, in place of the clear. Doing it that
/// way avoids the usual skybox depth trick (<c>gl_Position.z = w</c> plus a LEQUAL depth func)
/// entirely, and costs the same as the clear it replaces.</para>
///
/// <para>The gradient itself lives in <see cref="SkyGlsl"/>, shared verbatim with the lit and PBR
/// fragment stages so fog can dissolve geometry into exactly the sky behind it.</para>
/// </summary>
public sealed class SkyShader : Shader
{
    public SkyShader() : base("sky") => Load(Vert, Frag);

    /// <summary>Bind the gradient parameters. <paramref name="sunToward"/> points AT the sun (the
    /// opposite of the sun's travel direction).</summary>
    public void SetSky(in SkySettings sky, Vector3 sunToward, Vector3 sunColor)
    {
        SetUniform("u_skyHorizon", sky.Horizon);
        SetUniform("u_skyZenith", sky.Zenith);
        SetUniform("u_skyGround", sky.Ground);
        SetUniform("u_sunDir", sunToward);
        SetUniform("u_sunTint", sunColor);
        SetUniform("u_sunGlow", sky.SunGlow);
    }

    public void SetCamera(in Matrix4x4 invViewProj, Vector3 camPos)
    {
        SetUniform("u_invViewProj", invViewProj);
        SetUniform("u_camPos", camPos);
    }

    /// <summary>
    /// The gradient, shared by this shader and by the fog in the mesh shaders.
    ///
    /// <para>The horizon-to-zenith blend is raised to a fractional power so the horizon band stays
    /// wide; a linear blend puts the transition halfway up the sky, where nobody looks.</para>
    /// </summary>
    internal const string SkyGlsl = @"
uniform vec3  u_skyHorizon, u_skyZenith, u_skyGround;
uniform vec3  u_sunDir, u_sunTint;
uniform float u_sunGlow;

vec3 skyColor(vec3 dir) {
    dir = normalize(dir);
    vec3 c = dir.y >= 0.0
        ? mix(u_skyHorizon, u_skyZenith, pow(clamp(dir.y, 0.0, 1.0), 0.45))
        : mix(u_skyHorizon, u_skyGround, clamp(-dir.y * 3.0, 0.0, 1.0));

    if (u_sunGlow > 0.0) {
        float s = max(dot(dir, normalize(u_sunDir)), 0.0);
        // Two lobes: a tight disc plus a broad haze. One alone reads as either a sticker or a smear.
        c += u_sunTint * u_sunGlow * (pow(s, 350.0) + 0.12 * pow(s, 8.0));
    }
    return c;
}
";

    /// <summary>Distance fog, shared by the lit and PBR fragment stages. Depends on
    /// <see cref="SkyGlsl"/> being declared first, since it blends toward the sky.</summary>
    internal const string FogGlsl = @"
uniform int   u_fogOn;
uniform int   u_fogUseSky;
uniform float u_fogDensity;
uniform vec3  u_fogColor;

// Exponential-SQUARED falloff. A linear ramp has a visible edge where it begins, which reads as a
// wall of haze rather than as distance.
vec3 applyFog(vec3 color, vec3 fragPos, vec3 viewPos) {
    if (u_fogOn == 0) return color;
    vec3 toFrag = fragPos - viewPos;
    float d = length(toFrag) * u_fogDensity;
    float f = 1.0 - exp(-d * d);
    vec3 target = (u_fogUseSky != 0) ? skyColor(toFrag) : u_fogColor;
    return mix(color, target, clamp(f, 0.0, 1.0));
}
";

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
uniform mat4 u_invViewProj;
uniform vec3 u_camPos;
out vec3 v_dir;
void main() {
    // The unit quad spans [-0.5, 0.5]; x2 covers clip space exactly.
    vec2 ndc = a_pos.xy * 2.0;
    gl_Position = vec4(ndc, 0.0, 1.0);
    // Unproject the far plane to get the view ray for this pixel.
    vec4 world = u_invViewProj * vec4(ndc, 1.0, 1.0);
    v_dir = world.xyz / world.w - u_camPos;
}";

    private const string Frag = @"#version 410 core
in vec3 v_dir;
out vec4 fragColor;
" + SkyGlsl + @"
void main() { fragColor = vec4(skyColor(v_dir), 1.0); }";
}
