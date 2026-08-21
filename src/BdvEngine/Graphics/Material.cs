using System.Numerics;

namespace BdvEngine;

/// <summary>Which built-in shader family a 3D material renders with — the unified renderer maps
/// this to a concrete shader (Unlit / Lit / PBR-lite). 2D sprite materials ignore it.</summary>
public enum MaterialShading { Unlit, Lit, Pbr }

public sealed class Material : IDisposable
{
    private readonly Dictionary<string, object> _uniforms = new();

    public string Name { get; }
    public string DiffuseTextureName { get; private set; }
    public Texture? DiffuseTexture { get; private set; }
    public Color Color { get; set; }

    /// <summary>3D shader family. Defaults to <see cref="MaterialShading.Lit"/> so existing 3D
    /// materials render exactly as before; the unified renderer dispatches on this.</summary>
    public MaterialShading Shading { get; set; } = MaterialShading.Lit;

    /// <summary>PBR metallic factor [0..1] (used when <see cref="Shading"/> is Pbr).</summary>
    public float Metallic { get; set; }

    /// <summary>PBR roughness factor [0..1] (used when <see cref="Shading"/> is Pbr).</summary>
    public float Roughness { get; set; } = 0.5f;

    /// <summary>When true the renderer disables back-face culling for this material — for
    /// single-sided meshes (e.g. heightmap terrain). Retires the old world-level cull toggle.</summary>
    public bool DoubleSided { get; set; }
    public Shader? CustomShader { get; }
    public bool HasCustomShader => CustomShader != null;

    /// <summary>Optional tangent-space normal map, bound to texture unit 1
    /// when <see cref="ReceivesLighting"/> is on. Paired with a
    /// <see cref="NormalLitSpriteShader"/> to give a flat sprite per-pixel
    /// directional lighting (the Candide-style 2.5D look). Null → the
    /// sprite draws unlit / flat.</summary>
    public string? NormalTextureName { get; private set; }
    public Texture? NormalTexture { get; private set; }

    /// <summary>When true, the batcher pushes the scene's ambient + point
    /// lights into this material's shader each flush and binds
    /// <see cref="NormalTexture"/> to unit 1. Only meaningful for a
    /// material whose custom shader consumes them (NormalLitSpriteShader).
    /// Off by default so every existing material renders exactly as before.</summary>
    public bool ReceivesLighting { get; set; }

    /// <summary>Precomputed batch key for <see cref="SpriteBatcher"/> —
    /// previously built fresh on every <c>DrawSolid</c>/<c>DrawTextureUV</c>
    /// via string concatenation, allocating once per quad. At tens of
    /// thousands of quads per frame (zoomed-out world with kingdom
    /// fills, borders, terrain) that was the dominant GC pressure +
    /// caused noticeable FPS drops despite a small draw-call count.
    /// Cached at construction since name / shader / texture all form
    /// the Material's immutable identity; <see cref="SetDiffuseTexture"/>
    /// rebuilds it.</summary>
    internal string BatchKey { get; private set; } = "";

    public Material(string name, string diffuseTextureName, Color color, Shader? shader = null)
    {
        Name = name;
        DiffuseTextureName = diffuseTextureName;
        Color = color;
        CustomShader = shader;
        if (!string.IsNullOrEmpty(diffuseTextureName))
            DiffuseTexture = TextureManager.Get(diffuseTextureName);
        RecomputeBatchKey();
    }

    public void SetDiffuseTexture(string name)
    {
        if (DiffuseTexture != null) TextureManager.Flush(DiffuseTextureName);
        DiffuseTextureName = name;
        DiffuseTexture = string.IsNullOrEmpty(name) ? null : TextureManager.Get(name);
        RecomputeBatchKey();
    }

    /// <summary>Attach (or clear, with null/empty) the tangent-space normal
    /// map. Does not on its own enable lighting — set
    /// <see cref="ReceivesLighting"/> too. The texture must already be
    /// registered (e.g. via <see cref="NormalMapGenerator"/> or the asset
    /// loader).</summary>
    public void SetNormalTexture(string? name)
    {
        if (NormalTexture != null) TextureManager.Flush(NormalTextureName!);
        NormalTextureName = name;
        NormalTexture = string.IsNullOrEmpty(name) ? null : TextureManager.Get(name);
    }

    private void RecomputeBatchKey()
        => BatchKey = HasCustomShader
            ? CustomShader!.Name + ":" + DiffuseTextureName + ":" + Name
            : "__default_batch__:" + DiffuseTextureName;

    // Each setter checks if the existing boxed value matches the new
    // one — skip the box allocation for unchanged values. Critical for
    // per-frame uniforms (water shader's u_time, etc.) where naive
    // assignment was leaking a boxed float every frame.
    public void SetUniform(string name, float v)
    {
        if (_uniforms.TryGetValue(name, out var ex) && ex is float ef && ef == v) return;
        _uniforms[name] = v;
    }
    public void SetUniform(string name, int v)
    {
        if (_uniforms.TryGetValue(name, out var ex) && ex is int ei && ei == v) return;
        _uniforms[name] = v;
    }
    public void SetUniform(string name, Vector2 v)
    {
        if (_uniforms.TryGetValue(name, out var ex) && ex is Vector2 ev && ev == v) return;
        _uniforms[name] = v;
    }
    public void SetUniform(string name, Vector3 v)
    {
        if (_uniforms.TryGetValue(name, out var ex) && ex is Vector3 ev && ev == v) return;
        _uniforms[name] = v;
    }
    public void SetUniform(string name, Vector4 v)
    {
        if (_uniforms.TryGetValue(name, out var ex) && ex is Vector4 ev && ev == v) return;
        _uniforms[name] = v;
    }
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
        if (NormalTexture != null) TextureManager.Flush(NormalTextureName!);
        NormalTexture = null;
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
