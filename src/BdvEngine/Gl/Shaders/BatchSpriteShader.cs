namespace BdvEngine;

public sealed class BatchSpriteShader : Shader
{
    public BatchSpriteShader() : base("batch_sprite")
    {
        Load(VertexSource, FragmentSource);
    }

    // a_pos.z carries the per-quad sortY (depth anchor) on the Object
    // layer; u_zScale converts it into clip-space Z so the depth buffer
    // resolves overlapping quads without a CPU Y-sort. On non-Object
    // layers the batcher sets u_zScale = 0 → gl_Position.z stays at 1.0
    // (flat, alpha-blended draw-order ordering).
    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;
layout(location = 2) in vec4 a_color;

uniform mat4 u_proj;
uniform float u_zScale;
uniform float u_zBias;   // per-frame min sortY, so absolute world Y can't overflow clip-Z

out vec2 v_textCoord;
out vec4 v_color;

void main()
{
    gl_Position = u_proj * vec4(a_pos.xy, 0.0, 1.0);
    gl_Position.z = 1.0 - (a_pos.z - u_zBias) * u_zScale;
    v_textCoord = a_textCoord;
    v_color = a_color;
}";

    // Alpha discard at the very low end (~0.05) lets the depth buffer
    // skip transparent texels in atlas sprites that would otherwise
    // write Z and occlude back-to-front later quads. Cheap, no shader
    // branches on the colour path.
    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;
in vec4 v_color;

uniform sampler2D u_diffuse;

out vec4 fragColor;

void main()
{
    vec4 c = v_color * texture(u_diffuse, v_textCoord);
    if (c.a < 0.05) discard;
    fragColor = c;
}";
}
