using System.Numerics;

namespace BdvEngine;

/// <summary>Flat, unlit shader — samples base colour × tint, ignores lighting. For UI-ish 3D,
/// emissive props, or debugging.</summary>
public sealed class UnlitMeshShader : MeshShader
{
    public UnlitMeshShader() : base("mesh_unlit") => Load(Vert, Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
    }

    public override void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material)
    {
        SetUniform("u_model", model);
        SetUniform("u_color", material.Color.ToVector4());
        SetUniform("u_alphaCutoff", material.EffectiveCutoff);
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 2) in vec2 a_uv;
uniform mat4 u_proj, u_view, u_model;
out vec2 v_uv;
void main() { gl_Position = u_proj * u_view * u_model * vec4(a_pos, 1.0); v_uv = a_uv; }";

    /// <summary>Shared with the instanced variant so the two can't drift.</summary>
    internal const string Frag = @"#version 410 core
in vec2 v_uv;
uniform vec4 u_color;
uniform sampler2D u_diffuse;
uniform float u_alphaCutoff;
out vec4 fragColor;
void main() {
    vec4 c = texture(u_diffuse, v_uv) * u_color;
    if (u_alphaCutoff > 0.0 && c.a < u_alphaCutoff) discard;
    fragColor = c;
}";
}
