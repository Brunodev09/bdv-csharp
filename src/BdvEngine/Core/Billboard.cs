using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

/// <summary>A camera-facing 2D sprite anchored at a 3D world position (Phase 8 — in-world sprites:
/// health bars over enemies, map pins, emotes). Attach to a <see cref="SimObject"/>; the renderer
/// draws it as a quad that always faces the camera, depth-tested against the 3D scene.</summary>
public sealed class BillboardComponent : BaseComponent
{
    public Material Material;
    public float Width;
    public float Height;
    public Vector3 Offset;   // world-space offset from the owner's position (e.g. float above head)

    public BillboardComponent(string materialName, float width, float height, Vector3 offset = default)
        : base(new Data())
    {
        Material = MaterialManager.Get(materialName);
        Width = width;
        Height = height;
        Offset = offset;
    }

    private sealed class Data : IComponentData
    {
        public string Name { get; set; } = "billboard";
        public void SetFromJson(JsonElement json) { }
    }
}

/// <summary>Unlit textured shader for <see cref="BillboardComponent"/>: builds a camera-facing
/// quad in the vertex shader from world-space camera right/up vectors, so the sprite always faces
/// the viewer regardless of camera angle.</summary>
public sealed class BillboardShader : Shader
{
    public BillboardShader() : base("billboard") => Load(Vert, Frag);

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;   // unit-quad corner in [-0.5, 0.5]
layout(location = 2) in vec2 a_uv;
uniform mat4 u_proj, u_view;
uniform vec3 u_camRight, u_camUp, u_worldPos;
uniform vec2 u_size;
out vec2 v_uv;
void main() {
    vec3 world = u_worldPos + u_camRight * (a_pos.x * u_size.x) + u_camUp * (a_pos.y * u_size.y);
    gl_Position = u_proj * u_view * vec4(world, 1.0);
    v_uv = a_uv;
}";

    private const string Frag = @"#version 410 core
in vec2 v_uv;
uniform vec4 u_color;
uniform sampler2D u_diffuse;
out vec4 fragColor;
void main() { fragColor = texture(u_diffuse, v_uv) * u_color; }";
}
