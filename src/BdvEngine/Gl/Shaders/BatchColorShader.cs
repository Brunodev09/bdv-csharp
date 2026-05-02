namespace BdvEngine;

public sealed class BatchColorShader : Shader
{
    public BatchColorShader() : base("batch_color")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec4 a_color;

uniform mat4 u_proj;

out vec4 v_color;

void main()
{
    gl_Position = u_proj * vec4(a_pos, 1.0);
    v_color = a_color;
}";

    private const string FragmentSource = @"#version 410 core
in vec4 v_color;

out vec4 fragColor;

void main()
{
    fragColor = v_color;
}";
}
