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
    public void SetObject(in Matrix4x4 model) => SetUniform("u_model", model);

    internal const string Frag = @"#version 410 core
void main() { }";   // depth is written automatically; nothing else to do

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
uniform mat4 u_lightViewProj, u_model;
void main() { gl_Position = u_lightViewProj * u_model * vec4(a_pos, 1.0); }";
}

/// <summary>Depth-only shader for skinned meshes — same skinning maths as
/// <see cref="SkinnedMeshShader"/>, so a posed character casts a posed shadow.</summary>
public sealed class SkinnedDepthShader : Shader
{
    public SkinnedDepthShader() : base("depth_only_skinned") => Load(Vert, DepthShader.Frag);

    public void SetFrame(in Matrix4x4 lightViewProj) => SetUniform("u_lightViewProj", lightViewProj);
    public void SetObject(in Matrix4x4 model) => SetUniform("u_model", model);

    public void SetJoints(Matrix4x4[] palette, int count)
    {
        int n = Math.Min(count, Skin.MaxJoints);
        for (int i = 0; i < n; i++) SetUniform($"u_joints[{i}]", palette[i]);
    }

    private const string Vert = @"#version 410 core
#define MAX_JOINTS 64
layout(location = 0) in vec3 a_pos;
layout(location = 3) in vec4 a_joints;
layout(location = 4) in vec4 a_weights;
uniform mat4 u_lightViewProj, u_model;
uniform mat4 u_joints[MAX_JOINTS];

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
    gl_Position = u_lightViewProj * u_model * (skin * vec4(a_pos, 1.0));
}";
}
