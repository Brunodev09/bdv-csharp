using System.Numerics;

namespace BdvEngine;

/// <summary>
/// A directional light (the sun). For the slice a world has exactly one; Phase 6 promotes lights
/// to scene nodes and allows many. <see cref="Direction"/> is the direction the light TRAVELS
/// (e.g. pointing down and to one side); the engine flips the sign internally when feeding the
/// shader, so games never hit the old <c>-LightDirection</c> footgun.
/// </summary>
public sealed class DirectionalLight
{
    public Vector3 Direction;
    public Vector3 Color;

    public DirectionalLight(Vector3 direction, Vector3? color = null)
    {
        Direction = Vector3.Normalize(direction);
        Color = color ?? new Vector3(0.95f, 0.93f, 0.86f);
    }
}

/// <summary>
/// Scene-wide lighting + sky, owned by <see cref="World"/> — the Unity-scene / Three.js ambient
/// environment. Kept off the node graph so <see cref="Scene"/> stays pure data.
/// </summary>
public sealed class WorldEnvironment
{
    public Vector3 Sky = new(0.10f, 0.12f, 0.18f);
    public Vector3 Ambient = new(0.28f, 0.28f, 0.34f);
    public DirectionalLight Sun = new(new Vector3(-0.5f, -1f, -0.35f));

    /// <summary>Sun shadow settings. On by default — an unshadowed 3D scene reads as floating
    /// geometry, which is the single biggest thing separating "engine demo" from "game".</summary>
    public ShadowSettings Shadows { get; } = new();

    /// <summary>Skip objects outside the camera (and the sun's) frustum. On by default; turn it off
    /// to confirm a disappearing object is a culling bug rather than a scene bug.</summary>
    public bool Culling = true;

    /// <summary>Collapse repeated (mesh, material) pairs into one instanced draw call. On by
    /// default; turn it off to A/B a rendering difference against the plain path.</summary>
    public bool Instancing = true;

    /// <summary>Procedural gradient sky. Off by default — enabling it REPLACES <see cref="Sky"/>
    /// as the background, and a flat clear colour is right for plenty of scenes.</summary>
    public SkySettings SkyGradient { get; } = new();

    /// <summary>Distance fog. Off by default; fog is an art choice, not a fix.</summary>
    public FogSettings Fog { get; } = new();

    /// <summary>HDR post-processing for the 3D path: exposure, bloom, tonemap and grading. Off by
    /// default — turning it on changes how every existing scene looks, which has to be the author's
    /// call rather than something that happens on upgrade.</summary>
    public PostFxSettings PostFx { get; } = new();
}
