using System.Numerics;

namespace BdvEngine;

/// <summary>What a query hit.</summary>
public readonly struct RayHit
{
    public readonly Collider Collider;
    public readonly Vector3 Point;
    public readonly Vector3 Normal;
    public readonly float Distance;

    public RayHit(Collider collider, Vector3 point, Vector3 normal, float distance)
    {
        Collider = collider;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    public SimObject? Object => Collider?.Owner;
}

/// <summary>
/// 3D collision maths — stateless primitives shared by <see cref="Collider"/>,
/// <see cref="PhysicsWorld"/> and <see cref="CharacterController"/>.
///
/// <para>The 2D <see cref="Collision"/> class is unrelated and stays as it is; this is the 3D
/// counterpart, kept separate rather than overloading a class whose whole API is rectangles.</para>
/// </summary>
public static class Physics
{
    /// <summary>Nearest point to <paramref name="p"/> on the segment a→b. Capsule tests all reduce
    /// to this, which is exactly why capsules are cheap enough to be the default character shape.</summary>
    public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-12f) return a;
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f);
        return a + ab * t;
    }

    /// <summary>Nearest points between two segments. Needed for capsule-vs-capsule.</summary>
    public static (Vector3 p1, Vector3 p2) ClosestPointsBetweenSegments(
        Vector3 a1, Vector3 b1, Vector3 a2, Vector3 b2)
    {
        var d1 = b1 - a1;
        var d2 = b2 - a2;
        var r = a1 - a2;
        float aa = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);

        // Degenerate cases first: a zero-length segment is a point, and the general solve divides
        // by a determinant that vanishes there.
        if (aa <= 1e-12f && e <= 1e-12f) return (a1, a2);
        if (aa <= 1e-12f) return (a1, ClosestPointOnSegment(a1, a2, b2));
        if (e <= 1e-12f) return (ClosestPointOnSegment(a2, a1, b1), a2);

        float c = Vector3.Dot(d1, r);
        float b = Vector3.Dot(d1, d2);
        float denom = aa * e - b * b;

        // denom == 0 means the segments are parallel; any point works as a starting guess.
        float s = denom > 1e-12f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
        float t = (b * s + f) / e;

        if (t < 0f) { t = 0f; s = Math.Clamp(-c / aa, 0f, 1f); }
        else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / aa, 0f, 1f); }

        return (a1 + d1 * s, a2 + d2 * t);
    }

    // ── ray tests ────────────────────────────────────────────────────────────

    /// <summary>Slab test against an AABB. The normal comes from whichever slab was entered last,
    /// which is the face actually crossed.</summary>
    public static bool RayBox(Vector3 origin, Vector3 dir, float maxDistance, in Bounds box,
                              out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        float tmin = 0f, tmax = maxDistance;
        int axis = 1;
        float sign = 1f;

        for (int a = 0; a < 3; a++)
        {
            float o = Axis(origin, a), d = Axis(dir, a);
            float mn = Axis(box.Min, a), mx = Axis(box.Max, a);
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < mn || o > mx) return false;   // parallel and outside the slab
                continue;
            }
            float inv = 1f / d;
            float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
            float s = -1f;
            if (t1 > t2) { (t1, t2) = (t2, t1); s = 1f; }
            if (t1 > tmin) { tmin = t1; axis = a; sign = s; }
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        distance = tmin;
        normal = a2v(axis) * sign;
        return true;

        static Vector3 a2v(int a) => a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;
    }

    public static bool RaySphere(Vector3 origin, Vector3 dir, float maxDistance,
                                 Vector3 center, float radius, out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        var m = origin - center;
        float b = Vector3.Dot(m, dir);
        float c = Vector3.Dot(m, m) - radius * radius;
        if (c > 0f && b > 0f) return false;            // outside and pointing away

        float disc = b * b - c;
        if (disc < 0f) return false;

        float t = -b - MathF.Sqrt(disc);
        if (t < 0f) t = 0f;                            // origin inside the sphere
        if (t > maxDistance) return false;

        distance = t;
        var hit = origin + dir * t;
        normal = Vector3.Normalize(hit - center);
        return true;
    }

    /// <summary>Ray vs capsule: the infinite-cylinder solve, falling back to the cap spheres when
    /// the hit lands beyond either end.</summary>
    public static bool RayCapsule(Vector3 origin, Vector3 dir, float maxDistance,
                                  Vector3 a, Vector3 b, float radius,
                                  out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        var ba = b - a;
        var oa = origin - a;
        float baba = Vector3.Dot(ba, ba);
        if (baba < 1e-12f) return RaySphere(origin, dir, maxDistance, a, radius, out distance, out normal);

        float bard = Vector3.Dot(ba, dir);
        float baoa = Vector3.Dot(ba, oa);
        float rdoa = Vector3.Dot(dir, oa);
        float oaoa = Vector3.Dot(oa, oa);

        float A = baba - bard * bard;
        float B = baba * rdoa - baoa * bard;
        float C = baba * oaoa - baoa * baoa - radius * radius * baba;
        float h = B * B - A * C;

        if (h >= 0f && MathF.Abs(A) > 1e-12f)
        {
            float t = (-B - MathF.Sqrt(h)) / A;
            float y = baoa + t * bard;
            if (y > 0f && y < baba && t >= 0f && t <= maxDistance)   // hit the cylinder body
            {
                distance = t;
                var hit = origin + dir * t;
                var onAxis = a + ba * (y / baba);
                normal = Vector3.Normalize(hit - onAxis);
                return true;
            }
        }

        // Caps: take whichever hemisphere the ray reaches first.
        bool h1 = RaySphere(origin, dir, maxDistance, a, radius, out float d1, out var n1);
        bool h2 = RaySphere(origin, dir, maxDistance, b, radius, out float d2, out var n2);
        if (h1 && (!h2 || d1 <= d2)) { distance = d1; normal = n1; return true; }
        if (h2) { distance = d2; normal = n2; return true; }
        return false;
    }

    /// <summary>Ray vs heightfield by fixed-step marching, then a bisection refine.
    ///
    /// <para>Marching can step over a thin spike between samples; the step is tied to the terrain's
    /// cell size to keep that rare. Exact heightfield ray tracing would need per-cell traversal,
    /// which is not worth it for camera probes and ground checks.</para></summary>
    public static bool RayHeightmap(Vector3 origin, Vector3 dir, float maxDistance,
                                    TerrainCollider terrain, out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        float step = MathF.Max(terrain.Terrain.CellSize * 0.5f, 0.05f);
        float prevT = 0f;
        float prevDelta = origin.Y - terrain.HeightAt(origin.X, origin.Z);
        if (prevDelta < 0f) return false;   // starting underground: no meaningful surface hit

        for (float t = step; t <= maxDistance; t += step)
        {
            var p = origin + dir * t;
            float delta = p.Y - terrain.HeightAt(p.X, p.Z);
            if (delta <= 0f)
            {
                // Bracketed a crossing — bisect for a clean contact point.
                float lo = prevT, hi = t;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var pm = origin + dir * mid;
                    if (pm.Y - terrain.HeightAt(pm.X, pm.Z) > 0f) lo = mid; else hi = mid;
                }
                distance = hi;
                var hp = origin + dir * hi;
                normal = terrain.NormalAt(hp.X, hp.Z);
                return true;
            }
            prevT = t;
            prevDelta = delta;
        }
        return false;
    }

    private static float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    // ── overlap resolution ───────────────────────────────────────────────────

    /// <summary>
    /// How far, and in which direction, to push a sphere out of <paramref name="other"/>. Returns
    /// false when they don't overlap.
    ///
    /// <para>Written against <see cref="Collider.ClosestPoint"/> so it works for every shape pair
    /// without an N-by-N matrix of special cases. Capsule-vs-capsule is the one exception that
    /// needs its own segment-to-segment solve, handled by the caller.</para>
    /// </summary>
    public static bool ResolveSphere(Vector3 center, float radius, Collider other,
                                     out Vector3 pushDirection, out float depth)
    {
        pushDirection = Vector3.UnitY;
        depth = 0f;

        // Signed distance answers both questions at once: whether we overlap, and by how much —
        // including when the centre is deep inside, where a closest-point difference degenerates.
        float sd = other.SignedDistance(center);
        if (sd > radius) return false;

        pushDirection = other.OutwardNormal(center);
        depth = radius - sd;      // sd < 0 when inside, so a buried sphere gets pushed further
        return true;
    }
}
