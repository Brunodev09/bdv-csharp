namespace BdvEngine;

public sealed class LitShader : Shader
{
    public LitShader() : base("lit3d")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_textCoord;

uniform mat4 u_proj;
uniform mat4 u_view;
uniform mat4 u_model;
uniform mat4 u_normalMatrix;

out vec3 v_normal;
out vec2 v_textCoord;
out vec3 v_fragPos;

void main()
{
    vec4 worldPos = u_model * vec4(a_pos, 1.0);
    gl_Position = u_proj * u_view * worldPos;
    v_fragPos = worldPos.xyz;
    v_normal = (u_normalMatrix * vec4(a_normal, 0.0)).xyz;
    v_textCoord = a_textCoord;
}";

    private const string FragmentSource = @"#version 410 core
in vec3 v_normal;
in vec2 v_textCoord;
in vec3 v_fragPos;

uniform vec4 u_color;
uniform sampler2D u_diffuse;
uniform vec3 u_lightDir;
uniform vec3 u_lightColor;
uniform vec3 u_ambientColor;
uniform vec3 u_viewPos;

out vec4 fragColor;

void main()
{
    vec4 texColor = texture(u_diffuse, v_textCoord) * u_color;
    vec3 normal = normalize(v_normal);
    vec3 lightDir = normalize(u_lightDir);

    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * u_lightColor;

    vec3 viewDir = normalize(u_viewPos - v_fragPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);
    vec3 specular = spec * u_lightColor * 0.5;

    vec3 result = (u_ambientColor + diffuse + specular) * texColor.rgb;
    fragColor = vec4(result, texColor.a);
}";
}
