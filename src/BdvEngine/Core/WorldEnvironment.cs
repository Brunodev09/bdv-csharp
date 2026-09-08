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
}
