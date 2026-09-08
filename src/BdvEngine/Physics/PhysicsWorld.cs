using System.Numerics;

namespace BdvEngine;

/// <summary>
/// The registry of live <see cref="Collider"/>s and the queries over them.
///
/// <para><b>Broadphase is a linear scan with AABB rejection.</b> That is genuinely the right call
/// at this scale: an AABB overlap is a handful of compares, and a few hundred colliders cost less
/// than the bookkeeping a grid or BVH would need. Swap in a spatial hash when a real scene makes
/// this show up in a profile — the query surface here won't have to change.</para>
///
/// <para>Static, like the engine's other managers, because a game has one physics world in the same
/// way it has one <see cref="MaterialManager"/>. <see cref="Clear"/> resets it between levels.</para>
/// </summary>
public static class PhysicsWorld
{
    private static readonly List<Collider> _colliders = new();

    /// <summary>Every registered collider. Read-only — use <see cref="Register"/> /
    /// <see cref="Unregister"/>, which colliders call from their own Load/Unload.</summary>
    public static IReadOnlyList<Collider> Colliders => _colliders;

    public static void Register(Collider c)
    {
        if (!_colliders.Contains(c)) _colliders.Add(c);
    }

    public static void Unregister(Collider c) => _colliders.Remove(c);

    /// <summary>Drop every collider — call on a level swap, or the old level's geometry keeps
    /// colliding with the new one's.</summary>
    public static void Clear() => _colliders.Clear();

    private static bool Eligible(Collider c, int layerMask, Collider? ignore, bool includeTriggers)
        => c.Enabled
        && !ReferenceEquals(c, ignore)
        && (c.Layer & layerMask) != 0
        && (includeTriggers || !c.IsTrigger)
        && c.Owner != null;

    // ── queries ──────────────────────────────────────────────────────────────

    /// <summary>Nearest collider along a ray. The workhorse: click-to-select in world space, line
    /// of sight, ground probes, keeping a chase camera out of walls.</summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                               out RayHit hit, int layerMask = ~0, Collider? ignore = null,
                               bool includeTriggers = false)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f) return false;
        direction = Vector3.Normalize(direction);

        float best = maxDistance;
        Collider? bestCollider = null;
        Vector3 bestNormal = Vector3.UnitY;

        foreach (var c in _colliders)
        {
            if (!Eligible(c, layerMask, ignore, includeTriggers)) continue;
            // Broadphase: skip the exact test unless the ray's own bounds could reach the shape.
            if (!RayHitsBounds(origin, direction, best, c.WorldBounds)) continue;
            if (!c.Raycast(origin, direction, best, out float d, out var n)) continue;
            if (d >= best) continue;
            best = d;
            bestCollider = c;
            bestNormal = n;
        }

        if (bestCollider == null) return false;
        hit = new RayHit(bestCollider, origin + direction * best, bestNormal, best);
        return true;
    }

    private static bool RayHitsBounds(Vector3 o, Vector3 d, float maxDist, in Bounds b)
        => Physics.RayBox(o, d, maxDist, b, out _, out _) || b.Contains(o);

    /// <summary>Every collider overlapping a sphere. Explosion radius, "what can I interact with",
    /// trigger checks.</summary>
    public static List<Collider> OverlapSphere(Vector3 center, float radius, int layerMask = ~0,
                                               Collider? ignore = null, bool includeTriggers = true)
    {
        var result = new List<Collider>();
        var query = Bounds.FromCenterExtents(center, new Vector3(radius));
        foreach (var c in _colliders)
        {
            if (!Eligible(c, layerMask, ignore, includeTriggers)) continue;
            if (!query.Overlaps(c.WorldBounds)) continue;
            if (c.SignedDistance(center) <= radius) result.Add(c);
        }
        return result;
    }

    /// <summary>Colliders overlapping a capsule — the shape a character occupies, so this is what
    /// "who is touching the player" asks.</summary>
    public static List<Collider> OverlapCapsule(Vector3 a, Vector3 b, float radius, int layerMask = ~0,
                                                Collider? ignore = null, bool includeTriggers = true)
    {
        var result = new List<Collider>();
        var query = new Bounds(Vector3.Min(a, b) - new Vector3(radius),
                               Vector3.Max(a, b) + new Vector3(radius));
        foreach (var c in _colliders)
        {
            if (!Eligible(c, layerMask, ignore, includeTriggers)) continue;
            if (!query.Overlaps(c.WorldBounds)) continue;

            // Sample along the capsule axis. Exact for boxes and spheres in practice, and cheap;
            // the alternative is a full GJK, which this engine does not need yet.
            const int samples = 5;
            for (int i = 0; i < samples; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)(samples - 1));
                if (c.SignedDistance(p) <= radius) { result.Add(c); break; }
            }
        }
        return result;
    }

    /// <summary>Ground height directly under a point, from whichever collider is highest below it.
    /// Terrain and boxes both answer, so a character walks up onto a crate the same way it walks up
    /// a hill.</summary>
    public static bool GroundHeight(Vector3 from, float searchDown, out float y, out Vector3 normal,
                                    int layerMask = ~0, Collider? ignore = null)
    {
        if (Raycast(from, -Vector3.UnitY, searchDown, out var hit, layerMask, ignore))
        {
            y = hit.Point.Y;
            normal = hit.Normal;
            return true;
        }
        y = from.Y;
        normal = Vector3.UnitY;
        return false;
    }
}
