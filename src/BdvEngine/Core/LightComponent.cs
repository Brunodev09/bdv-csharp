using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

public enum LightType { Directional, Point }

/// <summary>A light as a scene node (Phase 6). Attach to a <see cref="SimObject"/> and the
/// unified renderer collects it each frame. A point light takes its world position from the
/// owner's transform; a directional light uses <see cref="Direction"/> (travel direction). Add
/// them via <see cref="World.AddPointLight"/> / <see cref="World.AddDirectionalLight"/>.</summary>
public sealed class LightComponent : BaseComponent
{
    public LightType Type;
    public Vector3 Color = Vector3.One;
    public float Intensity = 1f;
    public Vector3 Direction = new(0, -1, 0);   // directional only (travel direction)
    public float Range = 20f;                   // point only (falloff radius, world units)

    public LightComponent() : base(new LightData()) { }

    private sealed class LightData : IComponentData
    {
        public string Name { get; set; } = "light";
        public void SetFromJson(JsonElement json) { }
    }
}
