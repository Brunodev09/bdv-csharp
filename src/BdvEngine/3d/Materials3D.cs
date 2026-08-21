namespace BdvEngine;

/// <summary>Unity-style convenience for the 3D path. The mesh shaders always multiply the sampled
/// texel by <c>u_color</c>, so every material needs a texture even for a plain colour. This
/// provides a shared 1×1 white texture and a one-liner for flat-shaded coloured materials — the
/// equivalent of Unity's default material.</summary>
public static class Materials3D
{
    public const string WhiteTexture = "__white3d";
    private static bool _whiteReady;
    private static readonly HashSet<string> _registered = new();

    /// <summary>Create + register the shared white texture once. Safe to call
    /// repeatedly. Must run after the GL context exists (i.e. from Init onward).</summary>
    public static void EnsureWhiteTexture()
    {
        if (_whiteReady) return;
        _whiteReady = true;
        var tex = Texture.CreateBlank(WhiteTexture, 2, 2);
        Span<byte> px = stackalloc byte[2 * 2 * 4];
        px.Fill(255);
        tex.UploadRgba(2, 2, px);
        TextureManager.Register(WhiteTexture, tex);
    }

    /// <summary>Register (once) and return the name of a flat-shaded material of
    /// the given colour. Idempotent for a given name, so it is safe to call from
    /// loops that spawn many objects sharing a palette.</summary>
    public static string Solid(string name, Color color)
    {
        EnsureWhiteTexture();
        if (_registered.Add(name))
            MaterialManager.Register(new Material(name, WhiteTexture, color));
        return name;
    }
}
