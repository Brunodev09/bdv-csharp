using System.Numerics;

namespace BdvEngine;

/// <summary>
/// A world-space axis-aligned bounding box. The common currency of the physics broadphase: every
/// collider can produce one cheaply, and rejecting a pair on AABB overlap is far cheaper than any
/// exact shape test.
/// </summary>
public readonly struct Bounds
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public Bounds(Vector3 min, Vector3 max) { Min = min; Max = max; }

    public static Bounds FromCenterExtents(Vector3 center, Vector3 extents)
        => new(center - extents, center + extents);

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public Vector3 Extents => (Max - Min) * 0.5f;

    public bool Overlaps(in Bounds o)
        => Min.X <= o.Max.X && Max.X >= o.Min.X
        && Min.Y <= o.Max.Y && Max.Y >= o.Min.Y
        && Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    public bool Contains(Vector3 p)
        => p.X >= Min.X && p.X <= Max.X
        && p.Y >= Min.Y && p.Y <= Max.Y
        && p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>Grow by <paramref name="amount"/> on every axis — used to sweep a query by the
    /// distance a body is about to travel, so a fast mover can't tunnel past a collider between
    /// broadphase and narrowphase.</summary>
    public Bounds Expanded(float amount)
        => new(Min - new Vector3(amount), Max + new Vector3(amount));

    public Bounds Union(in Bounds o) => new(Vector3.Min(Min, o.Min), Vector3.Max(Max, o.Max));

    /// <summary>Nearest point on (or in) the box to <paramref name="p"/>. The workhorse of
    /// sphere-vs-box and capsule-vs-box: both reduce to "how far is the nearest box point".</summary>
    public Vector3 ClosestPoint(Vector3 p) => Vector3.Clamp(p, Min, Max);

    public override string ToString() => $"[{Min:F2} .. {Max:F2}]";
}
