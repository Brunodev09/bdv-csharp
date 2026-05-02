using System.Numerics;

namespace BdvEngine;

public sealed class Material : IDisposable
{
    private readonly Dictionary<string, object> _uniforms = new();

    public string Name { get; }
    public string DiffuseTextureName { get; private set; }
    public Texture? DiffuseTexture { get; private set; }
    public Color Color { get; set; }
    public Shader? CustomShader { get; }
    public bool HasCustomShader => CustomShader != null;

    public Material(string name, string diffuseTextureName, Color color, Shader? shader = null)
    {
        Name = name;
        DiffuseTextureName = diffuseTextureName;
        Color = color;
        CustomShader = shader;
        if (!string.IsNullOrEmpty(diffuseTextureName))
            DiffuseTexture = TextureManager.Get(diffuseTextureName);
    }

    public void SetDiffuseTexture(string name)
    {
        if (DiffuseTexture != null) TextureManager.Flush(DiffuseTextureName);
        DiffuseTextureName = name;
        DiffuseTexture = string.IsNullOrEmpty(name) ? null : TextureManager.Get(name);
    }

    public void SetUniform(string name, float v) => _uniforms[name] = v;
    public void SetUniform(string name, int v) => _uniforms[name] = v;
    public void SetUniform(string name, Vector2 v) => _uniforms[name] = v;
    public void SetUniform(string name, Vector3 v) => _uniforms[name] = v;
    public void SetUniform(string name, Vector4 v) => _uniforms[name] = v;
    public void SetUniform(string name, Matrix4x4 v) => _uniforms[name] = v;

    public void ApplyUniforms(Shader shader)
    {
        foreach (var (name, value) in _uniforms)
        {
            try
            {
                switch (value)
                {
                    case float f: shader.SetUniform(name, f); break;
                    case int i: shader.SetUniform(name, i); break;
                    case Vector2 v2: shader.SetUniform(name, v2); break;
                    case Vector3 v3: shader.SetUniform(name, v3); break;
                    case Vector4 v4: shader.SetUniform(name, v4); break;
                    case Matrix4x4 m: shader.SetUniform(name, m); break;
                }
            }
            catch
            {
                // uniform not active in this shader; skip
            }
        }
    }

    public void Dispose()
    {
        if (DiffuseTexture != null) TextureManager.Flush(DiffuseTextureName);
        DiffuseTexture = null;
    }
}

public static class MaterialManager
{
    private sealed class Node { public required Material Material; public int Count; }
    private static readonly Dictionary<string, Node> _materials = new();

    public static void Register(Material material)
    {
        if (_materials.ContainsKey(material.Name)) return;
        _materials[material.Name] = new Node { Material = material, Count = 0 };
    }

    public static Material Get(string name)
    {
        if (!_materials.TryGetValue(name, out var node))
            throw new InvalidOperationException($"MaterialManager: material '{name}' not registered.");
        node.Count++;
        return node.Material;
    }

    public static void Flush(string name)
    {
        if (!_materials.TryGetValue(name, out var node)) return;
        node.Count--;
        if (node.Count < 1)
        {
            node.Material.Dispose();
            _materials.Remove(name);
        }
    }
}
