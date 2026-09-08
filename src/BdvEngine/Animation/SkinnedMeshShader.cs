using System.Numerics;

namespace BdvEngine;

/// <summary>
/// Base for the skinning shader family: same contract as <see cref="MeshShader"/> plus a joint
/// matrix palette bound per draw.
///
/// <para>The vertex stage is shared and the fragment stages are the SAME strings the static
/// shaders use (<c>LitMeshShader.Frag</c> / <c>PbrMeshShader.Frag</c>), so a skinned character and
/// a static prop light identically — a divergence there would be a miserable thing to chase.</para>
/// </summary>
public abstract class SkinnedMeshShader : MeshShader
{
    protected SkinnedMeshShader(string name) : base(name) { }

    /// <summary>Bind the joint palette for the next draw. Entries beyond <paramref name="count"/>
    /// are left stale — no vertex references them, because the loader validated joint indices.</summary>
    public void SetJoints(Matrix4x4[] palette, int count)
    {
        int n = Math.Min(count, Skin.MaxJoints);
        for (int i = 0; i < n; i++) SetUniform($"u_joints[{i}]", palette[i]);
    }

    /// <summary>
    /// Linear blend skinning. Weights are normalised in the shader because exporters routinely emit
    /// sums a hair off 1.0, and an unnormalised sum shows up as a mesh that subtly inflates or
    /// shrinks around the joints.
    ///
    /// <para>A vertex with no weights at all falls back to identity rather than collapsing to the
    /// origin — that failure mode looks like the model exploding toward its pivot and is easy to
    /// mistake for a bad bind pose.</para>
    /// </summary>
    protected const string SkinVert = @"#version 410 core
#define MAX_JOINTS 64
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
layout(location = 3) in vec4 a_joints;
layout(location = 4) in vec4 a_weights;
uniform mat4 u_proj, u_view, u_model, u_normalMatrix;
uniform mat4 u_joints[MAX_JOINTS];
out vec3 v_normal; out vec2 v_uv; out vec3 v_fragPos;

int jointIndex(float f) { return int(clamp(f, 0.0, float(MAX_JOINTS - 1))); }

void main() {
    float wsum = dot(a_weights, vec4(1.0));
    mat4 skin;
    if (wsum > 1e-5) {
        vec4 w = a_weights / wsum;
        skin = w.x * u_joints[jointIndex(a_joints.x)]
             + w.y * u_joints[jointIndex(a_joints.y)]
             + w.z * u_joints[jointIndex(a_joints.z)]
             + w.w * u_joints[jointIndex(a_joints.w)];
    } else {
        skin = mat4(1.0);
    }

    vec4 world = u_model * (skin * vec4(a_pos, 1.0));
    gl_Position = u_proj * u_view * world;
    v_fragPos = world.xyz;
    v_normal = (u_normalMatrix * (skin * vec4(a_normal, 0.0))).xyz;
    v_uv = a_uv;
}";
}

/// <summary>Blinn-Phong skinned shader — the skinned twin of <see cref="LitMeshShader"/>.</summary>
public sealed class SkinnedLitMeshShader : SkinnedMeshShader
{
    public SkinnedLitMeshShader() : base("mesh_skinned_lit") => Load(SkinVert, LitMeshShader.Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
        SetLights(f);
        SetShadow(f);
        SetSkyFog(f);
    }

    public override void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material)
    {
        SetUniform("u_model", model);
        SetUniform("u_normalMatrix", normalMatrix);
        SetUniform("u_color", material.Color.ToVector4());
        SetUniform("u_alphaCutoff", material.EffectiveCutoff);
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }
}

/// <summary>PBR-lite skinned shader — the skinned twin of <see cref="PbrMeshShader"/>, and the one
/// imported glTF characters land on (the loader marks glTF materials <c>Pbr</c>).</summary>
public sealed class SkinnedPbrMeshShader : SkinnedMeshShader
{
    public SkinnedPbrMeshShader() : base("mesh_skinned_pbr") => Load(SkinVert, PbrMeshShader.Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
        SetLights(f);
        SetShadow(f);
        SetSkyFog(f);
    }

    public override void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material)
    {
        SetUniform("u_model", model);
        SetUniform("u_normalMatrix", normalMatrix);
        SetUniform("u_color", material.Color.ToVector4());
        SetUniform("u_alphaCutoff", material.EffectiveCutoff);
        SetUniform("u_metallic", material.Metallic);
        SetUniform("u_roughness", material.Roughness);
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }
}
