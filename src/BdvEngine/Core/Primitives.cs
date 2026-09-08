namespace BdvEngine;

/// <summary>
/// A mesh awaiting placement — returned by <see cref="Primitives"/> and consumed by
/// <see cref="World.Add(MeshSpec)"/>, which wraps it in a <see cref="SimObject"/>. The material
/// is attached later via <see cref="ObjectHandle.Material"/> (a MeshComponent needs its material
/// name up front, so we can't build it until the fluent chain supplies one).
/// </summary>
public readonly struct MeshSpec
{
    public readonly Mesh Mesh;
    public MeshSpec(Mesh mesh) => Mesh = mesh;
}

/// <summary>
/// Built-in primitive meshes — the Three.js <c>BoxGeometry</c>/<c>SphereGeometry</c>/<c>PlaneGeometry</c>
/// equivalent. Thin wrappers over the existing procedural <see cref="Mesh"/> factories.
/// </summary>
public static class Primitives
{
    // Meshes are SHARED per spec. Two reasons: 841 identical cubes should not be 841 GPU buffers,
    // and the renderer batches instanced draws by (mesh, material) — so a loop calling Cube() would
    // otherwise defeat instancing entirely by handing out a distinct mesh every time.
    // Mesh.Cube()/Sphere()/Plane() still return a fresh, privately-owned mesh when you need one.
    private static readonly Dictionary<string, Mesh> _shared = new();

    public static MeshSpec Cube() => new(Shared("cube", Mesh.Cube));

    public static MeshSpec Sphere(int segments = 24, int rings = 16)
        => new(Shared($"sphere:{segments},{rings}", () => Mesh.Sphere(segments, rings)));

    public static MeshSpec Plane(float size = 1f)
        => new(Shared($"plane:{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                      () => Mesh.Plane(size)));

    private static Mesh Shared(string key, Func<Mesh> build)
    {
        if (_shared.TryGetValue(key, out var m)) return m;
        m = build();
        _shared[key] = m;
        return m;
    }

    /// <summary>Drop the shared meshes (test isolation, or reclaiming buffers on a level swap).
    /// Scenes still holding these keep working; only the NEXT call builds a new one.</summary>
    public static void ClearShared() => _shared.Clear();
}
