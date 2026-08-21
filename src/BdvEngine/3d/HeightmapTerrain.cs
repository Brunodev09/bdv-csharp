using System.Numerics;

namespace BdvEngine;

/// <summary>Reusable heightmap terrain for the 3D path — the engine's
/// Unity-Terrain analogue. Builds a single lit <see cref="Mesh"/> from a height
/// function, bakes a per-vertex colour map into a texture, and supports bilinear
/// height sampling so gameplay can ground objects (players, props) on the
/// surface. The terrain is centred on the world origin and spans
/// <see cref="WorldSize"/> units on X and Z.</summary>
public sealed class HeightmapTerrain
{
    public int Resolution { get; }
    public float CellSize { get; }
    public float WorldSize => (Resolution - 1) * CellSize;

    // Heights laid out row-major: index = x + z * Resolution.
    private readonly float[] _heights;
    private readonly Mesh _mesh;
    private readonly string _materialName;

    /// <param name="resolution">Vertices per side. Kept under ~250 so triangle
    /// indices fit the mesh's 16-bit index buffer.</param>
    /// <param name="cellSize">World units between adjacent vertices.</param>
    /// <param name="heightAt">World (x,z) → surface height (Y).</param>
    /// <param name="colorAt">World (x,z,height) → surface colour at that vertex.</param>
    public HeightmapTerrain(int resolution, float cellSize,
        Func<float, float, float> heightAt,
        Func<float, float, float, Color> colorAt,
        string materialName = "terrain")
    {
        if (resolution > 256)
            throw new ArgumentOutOfRangeException(nameof(resolution),
                "HeightmapTerrain resolution must be <= 256 (16-bit mesh indices).");

        Resolution = resolution;
        CellSize = cellSize;
        _materialName = materialName;

        int n = resolution;
        float half = WorldSize / 2f;
        _heights = new float[n * n];

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            _heights[x + z * n] = heightAt(x * cellSize - half, z * cellSize - half);

        var verts = new float[n * n * Mesh.FloatsPerVertex];
        var pixels = new byte[n * n * 4];
        int vi = 0;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = x * cellSize - half;
            float wz = z * cellSize - half;
            float h = _heights[x + z * n];

            // Central-difference normal from neighbouring heights.
            float hl = _heights[Math.Max(x - 1, 0) + z * n];
            float hr = _heights[Math.Min(x + 1, n - 1) + z * n];
            float hb = _heights[x + Math.Max(z - 1, 0) * n];
            float hf = _heights[x + Math.Min(z + 1, n - 1) * n];
            var normal = Vector3.Normalize(new Vector3(hl - hr, 2f * cellSize, hb - hf));

            verts[vi++] = wx; verts[vi++] = h; verts[vi++] = wz;
            verts[vi++] = normal.X; verts[vi++] = normal.Y; verts[vi++] = normal.Z;
            // Sample the texel centre that corresponds to this vertex.
            verts[vi++] = (x + 0.5f) / n; verts[vi++] = (z + 0.5f) / n;

            var c = colorAt(wx, wz, h);
            int pi = (x + z * n) * 4;
            pixels[pi] = c.R; pixels[pi + 1] = c.G; pixels[pi + 2] = c.B; pixels[pi + 3] = 255;
        }

        var idx = new ushort[(n - 1) * (n - 1) * 6];
        int ii = 0;
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            ushort v00 = (ushort)(x + z * n);
            ushort v10 = (ushort)(x + 1 + z * n);
            ushort v11 = (ushort)(x + 1 + (z + 1) * n);
            ushort v01 = (ushort)(x + (z + 1) * n);
            // Same winding as Mesh.Plane's top face (CCW from above) so it
            // survives back-face culling.
            idx[ii++] = v00; idx[ii++] = v10; idx[ii++] = v11;
            idx[ii++] = v00; idx[ii++] = v11; idx[ii++] = v01;
        }

        _mesh = new Mesh(verts, idx);

        string texName = materialName + "_tex";
        var tex = Texture.CreateBlank(texName, n, n);
        tex.UploadRgba(n, n, pixels);
        TextureManager.Register(texName, tex);
        MaterialManager.Register(new Material(materialName, texName, Color.White));
    }

    /// <summary>Bilinearly interpolated surface height at world (x,z).</summary>
    public float SampleHeight(float wx, float wz)
    {
        int n = Resolution;
        float half = WorldSize / 2f;
        float gx = (wx + half) / CellSize;
        float gz = (wz + half) / CellSize;
        int x0 = (int)MathF.Floor(gx);
        int z0 = (int)MathF.Floor(gz);
        float fx = gx - x0;
        float fz = gz - z0;
        int xa = Math.Clamp(x0, 0, n - 1), xb = Math.Clamp(x0 + 1, 0, n - 1);
        int za = Math.Clamp(z0, 0, n - 1), zb = Math.Clamp(z0 + 1, 0, n - 1);
        float h00 = _heights[xa + za * n], h10 = _heights[xb + za * n];
        float h01 = _heights[xa + zb * n], h11 = _heights[xb + zb * n];
        float h0 = h00 + (h10 - h00) * fx;
        float h1 = h01 + (h11 - h01) * fx;
        return h0 + (h1 - h0) * fz;
    }

    /// <summary>A <see cref="SimObject"/> that renders the terrain mesh. Add it to
    /// a <see cref="Scene"/> like any other object.</summary>
    public SimObject CreateObject(int id = 1, string name = "terrain")
    {
        var obj = new SimObject(id, name);
        obj.AddComponent(new MeshComponent(_mesh, _materialName));
        return obj;
    }
}
