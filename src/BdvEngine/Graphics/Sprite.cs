using System.Numerics;

namespace BdvEngine;

public class Sprite : IDisposable
{
    protected readonly List<Vertex> _vertices = new();
    protected readonly Material _material;
    protected readonly string _materialName;

    public string Name { get; }
    public float Width { get; }
    public float Height { get; }
    public bool HasCustomShader => _material.HasCustomShader;
    public Material Material => _material;
    public IReadOnlyList<Vertex> Vertices => _vertices;
    public SpriteLayer Layer { get; set; } = SpriteLayer.Ground;

    public Sprite(string name, string materialName, float width = 100, float height = 100)
    {
        Name = name;
        _materialName = materialName;
        Width = width;
        Height = height;
        _material = MaterialManager.Get(materialName);
    }

    public virtual void Load()
    {
        _vertices.Clear();
        _vertices.AddRange(new[]
        {
            new Vertex(0,      0,       0, 0, 0),
            new Vertex(0,      Height,  0, 0, 1),
            new Vertex(Width,  Height,  0, 1, 1),
            new Vertex(Width,  Height,  0, 1, 1),
            new Vertex(Width,  0,       0, 1, 0),
            new Vertex(0,      0,       0, 0, 0),
        });
    }

    public virtual void Update(double tick) { }

    /// <summary>
    /// Queue this sprite into the batcher. One draw call per (texture × shader) at flush time.
    /// </summary>
    public virtual void Render(Shader shader, Matrix4x4 modelMatrix)
    {
        // For Object layer, use the sprite's world Y (bottom of sprite ≈ M42 + Height*scaleY)
        // as the sort key so feet-on-ground sorting works as expected.
        float sortY = Layer == SpriteLayer.Object ? modelMatrix.M42 + Height * modelMatrix.M22 : 0f;
        SpriteBatcher.Push(_vertices, _material, modelMatrix, Layer, sortY);
    }

    public virtual void Dispose()
    {
        MaterialManager.Flush(_materialName);
    }
}
