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
    public static MeshSpec Cube() => new(Mesh.Cube());
    public static MeshSpec Sphere(int segments = 24, int rings = 16) => new(Mesh.Sphere(segments, rings));
    public static MeshSpec Plane(float size = 1f) => new(Mesh.Plane(size));
}
