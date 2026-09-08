namespace BdvEngine;

/// <summary>
/// A mesh deformed by a <see cref="Skin"/> — the component the renderer routes to the skinning
/// shaders. Same shape as <see cref="MeshComponent"/>, plus the skeleton binding.
/// </summary>
public sealed class SkinnedMeshComponent : BaseComponent
{
    public Mesh Mesh { get; }
    public Material Material { get; }
    public Skin Skin { get; }

    private readonly string _materialName;

    public SkinnedMeshComponent(Mesh mesh, string materialName, Skin skin)
        : base(new SkinnedMeshData { MaterialName = materialName })
    {
        if (!mesh.IsSkinned)
            throw new ArgumentException(
                "SkinnedMeshComponent needs a mesh built with the skinned vertex layout " +
                "(joints + weights). Use MeshComponent for a static mesh.", nameof(mesh));
        Mesh = mesh;
        Skin = skin;
        _materialName = materialName;
        Material = MaterialManager.Get(materialName);
    }

    public override void Unload() => MaterialManager.Flush(_materialName);

    private sealed class SkinnedMeshData : IComponentData
    {
        public string Name { get; set; } = "skinnedMesh";
        public string MaterialName = "";
        public void SetFromJson(System.Text.Json.JsonElement json) { }
    }
}
