using System.Numerics;

namespace BdvEngine;

/// <summary>One light as the shader sees it. <see cref="Vec"/> is a unit "toward light" direction
/// for directional lights, or a world position for point lights. <see cref="Color"/> already has
/// intensity folded in.</summary>
public struct GpuLight
{
    public int Type;        // 0 = directional, 1 = point
    public Vector3 Vec;     // directional: toward-light unit vector; point: world position
    public Vector3 Color;   // colour × intensity
    public float Range;     // point: falloff radius
}

/// <summary>Per-frame render state shared by every 3D draw this frame — camera, ambient, and the
/// list of lights the scene contributed (Phase 6). Lights beyond <see cref="LightCount"/> in the
/// array are stale and must be ignored.</summary>
public readonly struct FrameParams
{
    public readonly Matrix4x4 Proj;
    public readonly Matrix4x4 View;
    public readonly Vector3 ViewPos;
    public readonly Vector3 Ambient;
    public readonly GpuLight[] Lights;
    public readonly int LightCount;

    public FrameParams(Matrix4x4 proj, Matrix4x4 view, Vector3 viewPos, Vector3 ambient,
                       GpuLight[] lights, int lightCount)
    {
        Proj = proj; View = view; ViewPos = viewPos; Ambient = ambient;
        Lights = lights; LightCount = lightCount;
    }
}

/// <summary>
/// A shader that renders lit/unlit/PBR meshes. The unified renderer picks one per material
/// (<see cref="Material.Shading"/>), binds it once per frame via <see cref="SetFrame"/>, then
/// calls <see cref="SetObject"/> per draw. Subclass to add new shader families.
/// </summary>
public abstract class MeshShader : Shader
{
    /// <summary>Max lights a single draw considers (must match the shaders' <c>MAX_LIGHTS</c>).</summary>
    public const int MaxLights = 8;

    protected MeshShader(string name) : base(name) { }

    /// <summary>Bind the frame-wide uniforms this shader consumes (a shader only sets what it uses).</summary>
    public abstract void SetFrame(in FrameParams f);

    /// <summary>Bind per-object + per-material uniforms and any textures, then the caller draws.</summary>
    public abstract void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material);

    /// <summary>Shared helper for lit families: bind ambient, view position and the light array.</summary>
    protected void SetLights(in FrameParams f)
    {
        SetUniform("u_ambientColor", f.Ambient);
        SetUniform("u_viewPos", f.ViewPos);
        SetUniform("u_lightCount", f.LightCount);
        for (int i = 0; i < f.LightCount; i++)
        {
            var l = f.Lights[i];
            SetUniform($"u_lightType[{i}]", l.Type);
            SetUniform($"u_lightVec[{i}]", l.Vec);
            SetUniform($"u_lightColor[{i}]", l.Color);
            SetUniform($"u_lightRange[{i}]", l.Range);
        }
    }
}
