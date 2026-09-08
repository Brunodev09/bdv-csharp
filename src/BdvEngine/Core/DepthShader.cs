using System.Numerics;

namespace BdvEngine;

/// <summary>
/// Depth-only shaders for the shadow pass. No lighting, no textures, no colour attachment — the
/// only output is the fragment depth the main pass compares against.
///
/// <para>Two variants for the same reason the main pass has two: a skinned mesh has to be posed by
/// its joint palette before its depth means anything, or an animated character would cast the
/// shadow of its bind pose.</para>
/// </summary>
public sealed class DepthShader : Shader
{
    public DepthShader() : base("depth_only") => Load(Vert, Frag);

    public void SetFrame(in Matrix4x4 lightViewProj) => SetUniform("u_lightViewProj", lightViewProj);

    public void SetObject(in Matrix4x4 model, Material material)
    {
        SetUniform("u_model", model);
        BindCutout(this, material);
    }

    /// <summary>Bind the alpha-test state shared by every depth variant. The texture is bound only
    /// for a cutout material; otherwise the cutoff is 0 and the fragment never samples, so an
    /// opaque depth pass costs exactly what it did before.</summary>
    internal static void BindCutout(Shader shader, Material material)
    {
        shader.SetUniform("u_alphaCutoff", material.EffectiveCutoff);
        // Bind the texture even for an opaque material. The sampler is declared either way, and an
        // unbound unit makes the driver warn about an unloadable texture; binding is a cheap state
        // change, while the FETCH is what the cutoff branch actually avoids.
        if (material.DiffuseTexture == null) return;
        material.DiffuseTexture.Activate(0);
        shader.SetUniform("u_diffuse", 0);
    }

    /// <summary>
    /// Depth-only fragment stage with alpha testing.
    ///
    /// <para>This is what makes a leaf card cast a leaf-shaped shadow instead of the rectangle it
    /// is modelled as. The cutoff is a uniform, so the branch is coherent across the whole draw and
    /// an opaque material never touches the texture unit.</para>
    /// </summary>
    internal const string Frag = @"#version 410 core
in vec2 v_uv;
uniform sampler2D u_diffuse;
uniform float u_alphaCutoff;
void main() {
    if (u_alphaCutoff > 0.0 && texture(u_diffuse, v_uv).a < u_alphaCutoff) discard;
    // Depth is written automatically for whatever survives.
}";

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 2) in vec2 a_uv;
uniform mat4 u_lightViewProj, u_model;
out vec2 v_uv;
void main() {
    gl_Position = u_lightViewProj * u_model * vec4(a_pos, 1.0);
    v_uv = a_uv;
}";
}

/// <summary>Depth-only shader for skinned meshes — same skinning maths as
/// <see cref="SkinnedMeshShader"/>, so a posed character casts a posed shadow.</summary>
public sealed class SkinnedDepthShader : Shader
{
    public SkinnedDepthShader() : base("depth_only_skinned") => Load(Vert, DepthShader.Frag);

    public void SetFrame(in Matrix4x4 lightViewProj) => SetUniform("u_lightViewProj", lightViewProj);

    public void SetObject(in Matrix4x4 model, Material material)
    {
        SetUniform("u_model", model);
        DepthShader.BindCutout(this, material);
    }

    public void SetJoints(Matrix4x4[] palette, int count)
    {
        int n = Math.Min(count, Skin.MaxJoints);
        for (int i = 0; i < n; i++) SetUniform($"u_joints[{i}]", palette[i]);
    }

    private const string Vert = @"#version 410 core
#define MAX_JOINTS 64
layout(location = 0) in vec3 a_pos;
layout(location = 2) in vec2 a_uv;
layout(location = 3) in vec4 a_joints;
layout(location = 4) in vec4 a_weights;
uniform mat4 u_lightViewProj, u_model;
uniform mat4 u_joints[MAX_JOINTS];
out vec2 v_uv;

int jointIndex(float f) { return int(clamp(f, 0.0, float(MAX_JOINTS - 1))); }

void main() {
    v_uv = a_uv;
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
    gl_Position = u_lightViewProj * u_model * (skin * vec4(a_pos, 1.0));
}";
}
