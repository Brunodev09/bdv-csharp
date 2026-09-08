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

    /// <summary>Sun shadow state. Only light 0 (the environment sun) is shadowed — see
    /// <see cref="ShadowMap"/> for why point lights aren't.</summary>
    public readonly bool ShadowsOn;
    public readonly Matrix4x4 LightViewProj;
    public readonly float ShadowBias, ShadowTexel, ShadowSoftness, ShadowStrength;

    /// <summary>Sky + fog. Fog samples the sky gradient in the view direction, so distant geometry
    /// dissolves into the actual horizon rather than into a flat colour.</summary>
    public readonly SkySettings? Sky;
    public readonly FogSettings? Fog;
    public readonly Vector3 SunToward, SunTint;

    public FrameParams(Matrix4x4 proj, Matrix4x4 view, Vector3 viewPos, Vector3 ambient,
                       GpuLight[] lights, int lightCount,
                       bool shadowsOn = false, Matrix4x4 lightViewProj = default,
                       float shadowBias = 0f, float shadowTexel = 0f,
                       float shadowSoftness = 1f, float shadowStrength = 0.75f,
                       SkySettings? sky = null, FogSettings? fog = null,
                       Vector3 sunToward = default, Vector3 sunTint = default)
    {
        Proj = proj; View = view; ViewPos = viewPos; Ambient = ambient;
        Lights = lights; LightCount = lightCount;
        ShadowsOn = shadowsOn; LightViewProj = lightViewProj;
        ShadowBias = shadowBias; ShadowTexel = shadowTexel;
        ShadowSoftness = shadowSoftness; ShadowStrength = shadowStrength;
        Sky = sky; Fog = fog; SunToward = sunToward; SunTint = sunTint;
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

    /// <summary>Shared helper for lit families: bind the sun shadow map and its parameters.</summary>
    protected void SetShadow(in FrameParams f)
    {
        SetUniform("u_shadowOn", f.ShadowsOn ? 1 : 0);
        if (!f.ShadowsOn) return;
        SetUniform("u_lightViewProj", f.LightViewProj);
        SetUniform("u_shadowMap", ShadowMap.ShadowTextureUnit);
        SetUniform("u_shadowBias", f.ShadowBias);
        SetUniform("u_shadowTexel", f.ShadowTexel);
        SetUniform("u_shadowSoft", f.ShadowSoftness);
        SetUniform("u_shadowStrength", f.ShadowStrength);
    }

    /// <summary>Shared helper for lit families: bind the sky gradient and fog parameters. The sky
    /// uniforms are bound even when the gradient is disabled, because fog can still fall back to a
    /// flat colour and the shader reads the same names either way.</summary>
    protected void SetSkyFog(in FrameParams f)
    {
        bool fogOn = f.Fog is { Enabled: true };
        SetUniform("u_fogOn", fogOn ? 1 : 0);
        if (!fogOn) return;

        var fog = f.Fog!;
        SetUniform("u_fogDensity", fog.Density);

        // Fog matches the sky only when there IS a sky; otherwise it would blend toward a gradient
        // nobody can see, and distant geometry would fade to the wrong colour.
        bool useSky = fog.UseSkyColor && f.Sky is { Enabled: true };
        SetUniform("u_fogUseSky", useSky ? 1 : 0);
        SetUniform("u_fogColor", fog.Color);

        var sky = f.Sky ?? new SkySettings();
        SetUniform("u_skyHorizon", sky.Horizon);
        SetUniform("u_skyZenith", sky.Zenith);
        SetUniform("u_skyGround", sky.Ground);
        SetUniform("u_sunDir", f.SunToward);
        SetUniform("u_sunTint", f.SunTint);
        SetUniform("u_sunGlow", sky.SunGlow);
    }

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
