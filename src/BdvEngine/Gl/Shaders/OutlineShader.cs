namespace BdvEngine;

/// <summary>
/// Sprite shader that draws a pulsing outline around the opaque
/// silhouette of the sampled texture. Drop-in replacement for the
/// default batch sprite shader (same <c>a_pos / a_textCoord / a_color</c>
/// attributes + <c>u_proj</c> + <c>u_zScale</c>), with three extra
/// uniforms set by the host:
///
/// <list type="bullet">
///   <item><c>u_texelSize</c> vec2 — (1/texW, 1/texH); used to step to
///   the 4 cardinal neighbour texels for the silhouette dilation pass.</item>
///   <item><c>u_time</c> float — wall-clock seconds; drives the pulse.</item>
///   <item><c>u_outlineCol</c> vec3 — RGB of the outline (0..1).</item>
/// </list>
///
/// Fragment-shader behaviour:
/// <list type="number">
///   <item>Sample the centre texel. If its alpha exceeds OPAQUE_T we
///   render the texture normally (the unit body is unchanged).</item>
///   <item>Otherwise (transparent pixel) sample the 4 cardinal
///   neighbours. If any is opaque, the current pixel is on the
///   silhouette's outer edge → output the outline colour modulated by
///   the pulse. Else discard.</item>
/// </list>
///
/// Engine-side (alongside BatchSpriteShader) so games can opt into it
/// without re-implementing the GL plumbing each time.
/// </summary>
public sealed class OutlineShader : Shader
{
    public OutlineShader() : base("outline_pulse")
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
    // Per-QUAD depth via sortY in a_pos.z (matches BatchSpriteShader)
    // so outlined units depth-sort consistently with normal units and
    // buildings. u_zScale = 0 disables it (non-depth layers).
    gl_Position.z = 1.0 - (a_pos.z - u_zBias) * u_zScale;
    v_textCoord = a_textCoord;
    v_color = a_color;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;
in vec4 v_color;

uniform sampler2D u_diffuse;
uniform vec2 u_texelSize;
uniform float u_time;
uniform vec3 u_outlineCol;

out vec4 fragColor;

const float OPAQUE_T   = 0.50;
const float PULSE_FREQ = 6.0;
const float PULSE_LO   = 0.55;
const float PULSE_HI   = 1.00;

void main()
{
    vec4 c = texture(u_diffuse, v_textCoord);
    if (c.a >= OPAQUE_T) {
        // Inside the silhouette — draw the unit normally. Alpha already
        // passes the depth-test cutoff so this writes Z.
        fragColor = v_color * c;
        return;
    }
    float aN = texture(u_diffuse, v_textCoord + vec2(0.0, -u_texelSize.y)).a;
    float aS = texture(u_diffuse, v_textCoord + vec2(0.0,  u_texelSize.y)).a;
    float aE = texture(u_diffuse, v_textCoord + vec2( u_texelSize.x, 0.0)).a;
    float aW = texture(u_diffuse, v_textCoord + vec2(-u_texelSize.x, 0.0)).a;
    float maxA = max(max(aN, aS), max(aE, aW));
    if (maxA < OPAQUE_T) discard;

    float pulse = mix(PULSE_LO, PULSE_HI, 0.5 + 0.5 * sin(u_time * PULSE_FREQ));
    fragColor = vec4(u_outlineCol * pulse, 1.0);
}";
}
