namespace BdvEngine;

/// <summary>
/// Convenience material factory — the "standard material" of the unified API. For the slice a
/// Standard material is a flat-shaded colour under the lit shader (built on the existing
/// <see cref="Materials3D"/> white-texture trick). Phase 4 turns this into a real StandardMaterial
/// with textures + metallic/roughness and per-material shader selection, at which point the
/// signatures here grow (a texture overload, etc.) without changing call sites that pass a colour.
/// </summary>
public static class Materials
{
    private static int _auto;

    /// <summary>A Blinn-Phong lit coloured material with an auto-generated name (the default look).</summary>
    public static string Standard(Color color) => Materials3D.Solid($"__std_{_auto++}", color);

    /// <summary>A lit coloured material with an explicit, idempotent name (safe to call repeatedly
    /// from loops that spawn many objects sharing a palette).</summary>
    public static string Standard(string name, Color color) => Materials3D.Solid(name, color);

    /// <summary>An unlit (flat) coloured material — ignores scene lighting.</summary>
    public static string Unlit(Color color)
    {
        var m = Make($"__unlit_{_auto++}", color);
        m.Shading = MaterialShading.Unlit;
        return m.Name;
    }

    /// <summary>A PBR-lite coloured material with metallic/roughness (0..1).</summary>
    public static string Pbr(Color color, float metallic = 0f, float roughness = 0.5f)
    {
        var m = Make($"__pbr_{_auto++}", color);
        m.Shading = MaterialShading.Pbr;
        m.Metallic = metallic;
        m.Roughness = roughness;
        return m.Name;
    }

    // Registers a coloured material (via the shared white texture) and returns the Material so the
    // shading family / PBR params can be set on it.
    private static Material Make(string name, Color color)
    {
        Materials3D.Solid(name, color);
        return MaterialManager.Get(name);
    }
}
