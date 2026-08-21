using System.Numerics;

namespace BdvEngine;

public sealed class MeshComponentData : IComponentData
{
    public string Name { get; set; } = "mesh";
    public string MaterialName = "";
    public Mesh Mesh = null!;

    public void SetFromJson(System.Text.Json.JsonElement json)
    {
        // Mesh components are typically constructed in code, not loaded from JSON.
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? Name;
        if (json.TryGetProperty("materialName", out var m)) MaterialName = m.GetString() ?? "";
    }
}

public sealed class MeshComponent : BaseComponent
{
    private readonly Mesh _mesh;
    private readonly Material _material;
    private readonly string _materialName;

    /// <summary>The mesh drawn by this component (read by the unified renderer's dispatch).</summary>
    public Mesh Mesh => _mesh;

    /// <summary>The material this component draws with (read by the unified renderer's dispatch).</summary>
    public Material Material => _material;

    public MeshComponent(Mesh mesh, string materialName)
        : base(new MeshComponentData { Name = "mesh", MaterialName = materialName, Mesh = mesh })
    {
        _mesh = mesh;
        _materialName = materialName;
        _material = MaterialManager.Get(materialName);
    }

    public override void Render(Shader shader)
    {
        shader.SetUniform("u_model", _owner.WorldMatrix);

        // Normal matrix: transpose of inverse of upper-left 3x3 of model matrix.
        // For uniform scale + rotation, model matrix itself is fine.
        if (Matrix4x4.Invert(_owner.WorldMatrix, out var inv))
            shader.SetUniform("u_normalMatrix", Matrix4x4.Transpose(inv));
        else
            shader.SetUniform("u_normalMatrix", _owner.WorldMatrix);

        shader.SetUniform("u_color", _material.Color.ToVector4());

        if (_material.DiffuseTexture != null)
        {
            _material.DiffuseTexture.Activate(0);
            shader.SetUniform("u_diffuse", 0);
        }

        _mesh.Draw();
    }

    public override void Unload()
    {
        MaterialManager.Flush(_materialName);
    }
}
