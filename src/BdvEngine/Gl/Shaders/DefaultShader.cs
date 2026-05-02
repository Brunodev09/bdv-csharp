namespace BdvEngine;

public sealed class DefaultShader : Shader
{
    public DefaultShader() : base("default")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;

uniform mat4 u_proj;
uniform mat4 u_transf;

out vec2 v_textCoord;

void main()
{
    gl_Position = u_proj * u_transf * vec4(a_pos, 1.0);
    v_textCoord = a_textCoord;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;

uniform vec4 u_color;
uniform sampler2D u_diffuse;

out vec4 fragColor;

void main()
{
    fragColor = u_color * texture(u_diffuse, v_textCoord);
}";
}
