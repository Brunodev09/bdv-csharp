using System.Numerics;

namespace BdvEngine;

/// <summary>
/// Base for the instanced shader family. Identical to <see cref="MeshShader"/> except that the
/// per-object model and normal matrices arrive as vertex attributes (divisor 1) rather than
/// uniforms — which is the whole point, since a uniform can only describe one object per draw.
///
/// <para>Fragment stages are the SAME strings the non-instanced shaders use, so an instanced tree
/// and a hand-placed one shade identically. Only the vertex stage differs.</para>
/// </summary>
public abstract class InstancedMeshShader : MeshShader
{
    protected InstancedMeshShader(string name) : base(name) { }

    /// <summary>Instanced draws take their transform per instance, so this only binds material
    /// state. The matrix arguments are ignored.</summary>
    public override void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material)
        => SetMaterial(material);

    public abstract void SetMaterial(Material material);

    /// <summary>Positions/normals/uvs as usual, plus the model matrix at locations 5-8 and the
    /// normal matrix at 9-11.</summary>
    protected const string InstancedVert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
layout(location = 5) in mat4 i_model;      // consumes 5,6,7,8
layout(location = 9) in mat3 i_normal;     // consumes 9,10,11
uniform mat4 u_proj, u_view;
out vec3 v_normal; out vec2 v_uv; out vec3 v_fragPos;
void main() {
    vec4 world = i_model * vec4(a_pos, 1.0);
    gl_Position = u_proj * u_view * world;
    v_fragPos = world.xyz;
    v_normal = i_normal * a_normal;
    v_uv = a_uv;
}";
}

/// <summary>Instanced twin of <see cref="LitMeshShader"/>.</summary>
public sealed class InstancedLitMeshShader : InstancedMeshShader
{
    public InstancedLitMeshShader() : base("mesh_inst_lit") => Load(InstancedVert, LitMeshShader.Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
        SetLights(f);
        SetShadow(f);
        SetSkyFog(f);
    }

    public override void SetMaterial(Material material)
    {
        SetUniform("u_color", material.Color.ToVector4());
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }
}

/// <summary>Instanced twin of <see cref="PbrMeshShader"/>.</summary>
public sealed class InstancedPbrMeshShader : InstancedMeshShader
{
    public InstancedPbrMeshShader() : base("mesh_inst_pbr") => Load(InstancedVert, PbrMeshShader.Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
        SetLights(f);
        SetShadow(f);
        SetSkyFog(f);
    }

    public override void SetMaterial(Material material)
    {
        SetUniform("u_color", material.Color.ToVector4());
        SetUniform("u_metallic", material.Metallic);
        SetUniform("u_roughness", material.Roughness);
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }
}

/// <summary>Instanced twin of <see cref="UnlitMeshShader"/>.</summary>
public sealed class InstancedUnlitMeshShader : InstancedMeshShader
{
    public InstancedUnlitMeshShader() : base("mesh_inst_unlit") => Load(InstancedVert, UnlitMeshShader.Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
    }

    public override void SetMaterial(Material material)
    {
        SetUniform("u_color", material.Color.ToVector4());
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }
}

/// <summary>Depth-only instanced shader for the shadow pass.</summary>
public sealed class InstancedDepthShader : Shader
{
    public InstancedDepthShader() : base("depth_inst") => Load(Vert, DepthShader.Frag);

    public void SetFrame(in Matrix4x4 lightViewProj) => SetUniform("u_lightViewProj", lightViewProj);

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 5) in mat4 i_model;
uniform mat4 u_lightViewProj;
void main() { gl_Position = u_lightViewProj * i_model * vec4(a_pos, 1.0); }";
}
