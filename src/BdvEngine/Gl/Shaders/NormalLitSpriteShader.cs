namespace BdvEngine;

/// <summary>
/// Normal-mapped sprite shader — the core of the Candide-style 2.5D look.
/// A flat sprite still draws as a flat quad, but a paired <b>normal map</b>
/// (bound to texture unit 1) tells the shader which way each texel "faces,"
/// so the left side of a sprite brightens while the right falls into
/// shadow, giving flat pixel art a rounded, three-dimensional form.
///
/// <para><b>Composes with the existing fullscreen <see cref="Lighting"/>
/// multiply pass</b> instead of fighting it. That pass already handles
/// "how much (coloured) light reaches this position" (day/night, torch
/// glow, cave darkening). This shader adds only the two things a
/// position-based pass can't: </para>
/// <list type="number">
///   <item><b>Sun form</b> — a directional shade from a global sun
///     direction, deliberately centred so a FLAT surface (n = +Z) stays
///     at exactly ×1.0. Surfaces tilted toward the sun brighten, those
///     tilted away darken. Because it averages to 1, it never
///     double-darkens on top of the multiply pass.</item>
///   <item><b>Point highlights</b> — an ADDITIVE-only rim from nearby
///     point lights: only the extra a tilted surface catches versus a
///     flat one, so the torch visibly rakes across a wall's near edge
///     without dimming anything.</item>
/// </list>
///
/// <para>Drop-in for <see cref="BatchSpriteShader"/>: identical vertex
/// attribute layout and per-quad <c>u_zScale</c> depth packing, so lit
/// sprites still sort correctly on the Object layer. Uniforms are pushed
/// by <see cref="Lighting.UploadForward"/> each flush.</para>
/// </summary>
public sealed class NormalLitSpriteShader : Shader
{
    public NormalLitSpriteShader() : base("normal_lit_sprite")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;
layout(location = 2) in vec4 a_color;

uniform mat4  u_proj;
uniform float u_zScale;
uniform float u_zBias;

out vec2 v_textCoord;
out vec4 v_color;
out vec2 v_world;

void main()
{
    gl_Position = u_proj * vec4(a_pos.xy, 0.0, 1.0);
    // Same per-quad Y→Z depth packing as BatchSpriteShader so lit
    // sprites depth-sort against the rest of the Object layer.
    gl_Position.z = 1.0 - (a_pos.z - u_zBias) * u_zScale;
    v_textCoord = a_textCoord;
    v_color     = a_color;
    v_world     = a_pos.xy;   // world-space position for per-light direction
}";

    private static readonly string FragmentSource = $@"#version 410 core
#define MAX_LIGHTS {Lighting.MaxLights}

in vec2 v_textCoord;
in vec4 v_color;
in vec2 v_world;

uniform sampler2D u_diffuse;   // unit 0 — albedo (the pixel art)
uniform sampler2D u_normal;    // unit 1 — tangent-space normal map

uniform vec3  u_sunDir;        // world-space direction TO the sun (normalized)
uniform float u_formAmount;    // strength of the sun form shading
uniform float u_pointAmount;   // strength of the additive point-light rim
uniform float u_lightHeight;   // how far point lights float above the ground

uniform int   u_lightCount;
uniform vec2  u_lightPos[MAX_LIGHTS];
uniform float u_lightRadius[MAX_LIGHTS];
uniform vec3  u_lightColor[MAX_LIGHTS];

out vec4 fragColor;

void main()
{{
    vec4 base = v_color * texture(u_diffuse, v_textCoord);
    if (base.a < 0.05) discard;

    // Decode the tangent-space normal. Flat (unlit) texels encode
    // (0.5, 0.5, 1.0) → n = (0,0,1), i.e. facing the camera.
    vec3 n = normalize(texture(u_normal, v_textCoord).rgb * 2.0 - 1.0);

    // ── Sun form: centred on a FLAT surface so it multiplies to ×1.0
    //    there (no net darkening vs the multiply pass). Tilt toward the
    //    sun → brighter; away → darker. Clamp so the shadow side never
    //    goes fully black.
    float flatDot = u_sunDir.z;               // dot((0,0,1), sunDir)
    float sunDot  = dot(n, u_sunDir);
    float form = max(1.0 + u_formAmount * (sunDot - flatDot), 0.35);

    // ── Additive point-light rim: ONLY the extra a tilted surface
    //    catches over a flat one, so flat sprites are untouched and the
    //    multiply pass keeps ownership of the base torch glow.
    vec3 hi = vec3(0.0);
    for (int i = 0; i < MAX_LIGHTS; i++)
    {{
        if (i >= u_lightCount) break;
        vec2  d2 = u_lightPos[i] - v_world;
        float d  = length(d2);
        float r  = u_lightRadius[i];
        if (d >= r) continue;

        vec3  L       = normalize(vec3(d2, u_lightHeight));
        float ndl     = max(dot(n, L), 0.0);
        float flatNdl = max(L.z, 0.0);        // what a flat sprite would catch
        float bonus   = max(ndl - flatNdl, 0.0);
        float att     = clamp(1.0 - d / r, 0.0, 1.0);
        att *= att;
        hi += u_lightColor[i] * (bonus * att * u_pointAmount);
    }}

    fragColor = vec4(base.rgb * form + base.rgb * hi, base.a);
}}";
}
