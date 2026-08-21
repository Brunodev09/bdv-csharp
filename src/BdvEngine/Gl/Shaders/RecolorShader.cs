namespace BdvEngine;

/// <summary>
/// Sprite shader that performs a "red → target colour" palette swap
/// while preserving the source's shading. Use it to render a single
/// template sprite (e.g. a house with red trim) in any number of team /
/// faction colours without baking N×M variants into the atlas.
///
/// Drop-in for the default batch sprite shader (same <c>a_pos /
/// a_textCoord / a_color</c> attributes + <c>u_proj</c> + <c>u_zScale</c>
/// so it depth-sorts with the rest of the Object layer), plus two extra
/// uniforms set by the host:
///
/// <list type="bullet">
///   <item><c>u_swapColor</c> vec3 — RGB of the replacement colour, 0..1</item>
///   <item><c>u_swapEnabled</c> float — 0 = bypass (pass texture through
///   unchanged), 1 = apply the swap. Use 0 when the target colour is
///   itself red and recolouring is moot.</item>
/// </list>
///
/// Per-fragment behaviour:
/// <list type="number">
///   <item>Sample the texel.</item>
///   <item>Compute "redness" = clamp(R - max(G, B), 0, 1) — the smooth
///   mask of how much red dominates the pixel. Pure red = 1, grey/blue = 0.</item>
///   <item>smoothstep the mask near-binary (clearly-red pixels are
///   fully replaced; clearly-non-red ones untouched). Plain
///   <c>mix(.., redness)</c> would half-blend mid-red pixels with the
///   kingdom colour and produce muddy in-between tones (red + green = brown).</item>
///   <item>Replacement keeps the original's value: a dark red shadow
///   becomes a dark target-colour shadow, a bright red highlight a
///   bright one. Shading carries through automatically.</item>
///   <item>Apply the per-vertex tint at the end so the engine's normal
///   tinting flow still works on top.</item>
/// </list>
/// </summary>
public sealed class RecolorShader : Shader
{
    public RecolorShader() : base("recolor_red")
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

void main()
{
    gl_Position = u_proj * vec4(a_pos.xy, 0.0, 1.0);
    // Per-quad depth via sortY packed into a_pos.z (matches the default
    // batch sprite shader). u_zScale = 0 on non-depth layers makes this
    // a no-op.
    gl_Position.z = 1.0 - (a_pos.z - u_zBias) * u_zScale;
    v_textCoord = a_textCoord;
    v_color = a_color;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;
in vec4 v_color;

uniform sampler2D u_diffuse;
uniform vec3  u_swapColor;
uniform float u_swapEnabled;

out vec4 fragColor;

void main()
{
    vec4 c = texture(u_diffuse, v_textCoord);
    // How much R dominates the brighter of G / B. 0 = grey/blue/green,
    // 1 = pure red.
    float redness = clamp(c.r - max(c.g, c.b), 0.0, 1.0);
    // The mask is near-binary via smoothstep: clearly-red pixels are
    // fully replaced, clearly-non-red ones are left alone, with a
    // narrow transition band between.
    float mask = smoothstep(0.10, 0.30, redness) * u_swapEnabled;
    // Replacement keeps the original's value: a dark red shadow becomes
    // a dark kingdom-colour shadow, a bright red highlight becomes a
    // bright one. Shading carries through automatically.
    vec3 swapped = u_swapColor * c.r;
    vec3 outRgb  = mix(c.rgb, swapped, mask);
    fragColor = vec4(outRgb, c.a) * v_color;
}";
}
