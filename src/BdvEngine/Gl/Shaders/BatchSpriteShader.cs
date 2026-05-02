namespace BdvEngine;

public sealed class BatchSpriteShader : Shader
{
    public BatchSpriteShader() : base("batch_sprite")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;
layout(location = 2) in vec4 a_color;

uniform mat4 u_proj;

out vec2 v_textCoord;
out vec4 v_color;

void main()
{
    gl_Position = u_proj * vec4(a_pos, 1.0);
    v_textCoord = a_textCoord;
    v_color = a_color;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;
in vec4 v_color;

uniform sampler2D u_diffuse;

out vec4 fragColor;

void main()
{
    fragColor = v_color * texture(u_diffuse, v_textCoord);
}";
}
