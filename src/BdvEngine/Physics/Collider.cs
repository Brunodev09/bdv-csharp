using System.Numerics;

namespace BdvEngine;

/// <summary>
/// Base for 3D collision shapes. Attach one to a <see cref="SimObject"/> and it joins the
/// <see cref="PhysicsWorld"/> automatically on load.
///
/// <para>Distinct from the 2D <see cref="ColliderComponent"/>, which stays as it is — ColonySim and
/// the other 2D games depend on it, and merging the two would give both a worse API than either has
/// now.</para>
///
/// <para><b>Boxes are world-axis-aligned.</b> A collider follows its object's position and scale but
/// ignores its rotation. That covers terrain props, walls and crates; a tilted ramp needs either a
/// sphere/capsule approximation or oriented boxes, which are not in v1.</para>
/// </summary>
public abstract class Collider : BaseComponent
{
    /// <summary>Local-space offset from the owner's origin — the usual case being a character
    /// capsule that must sit above the feet rather than centred on them.</summary>
    public Vector3 Center;

    /// <summary>Triggers report overlaps but never block movement. A pickup, a damage volume, a
    /// "you have entered the boss arena" region.</summary>
    public bool IsTrigger;

    /// <summary>Layer bitmask, tested against a query's mask. Lets a camera ray ignore the player,
    /// or a footstep probe hit only terrain.</summary>
    public int Layer = 1;

    /// <summary>Skip this collider without detaching it — cheaper than add/remove churn for things
    /// that toggle, like a door that opens.</summary>
    public bool Enabled = true;

    protected Collider(IComponentData data) : base(data) { }

    /// <summary>World-space centre, following the owner's transform.</summary>
    public Vector3 WorldCenter => Owner == null
        ? Center
        : Vector3.Transform(Center, Owner.WorldMatrix);

    /// <summary>Per-axis scale from the owner's world matrix. Sizes below are in LOCAL units and
    /// are multiplied by this — the same convention as Unity, so a unit cube scaled 40x with a
    /// size-1 box collider gets a 40-unit collider rather than needing the number written twice.</summary>
    protected Vector3 WorldScaleVector
    {
        get
        {
            if (Owner == null) return Vector3.One;
            var m = Owner.WorldMatrix;
            return new Vector3(
                new Vector3(m.M11, m.M12, m.M13).Length(),
                new Vector3(m.M21, m.M22, m.M23).Length(),
                new Vector3(m.M31, m.M32, m.M33).Length());
        }
    }

    /// <summary>Single scale factor for the radially symmetric shapes. A sphere or capsule under
    /// non-uniform scale has no correct single answer, so the largest axis wins: that keeps the
    /// collider enclosing the visual rather than cutting into it.</summary>
    protected float WorldScale
    {
        get
        {
            var s = WorldScaleVector;
            return MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        }
    }

    public abstract Bounds WorldBounds { get; }

    /// <summary>Closest point on this shape's surface (or interior) to <paramref name="p"/>, plus
    /// how deep <paramref name="p"/> sits inside. Every narrowphase test in
    /// <see cref="Physics"/> is written against this, so adding a shape means implementing one
    /// method rather than N pairwise cases.</summary>
    public abstract Vector3 ClosestPoint(Vector3 p);

    /// <summary>Signed distance from <paramref name="p"/> to the surface — negative inside.</summary>
    public abstract float SignedDistance(Vector3 p);

    /// <summary>
    /// Direction that pushes <paramref name="p"/> out of the shape — the gradient of
    /// <see cref="SignedDistance"/>.
    ///
    /// <para>Taking it from the signed distance field rather than from <c>p - ClosestPoint(p)</c>
    /// is what makes penetration resolution work for every shape uniformly. The difference vector
    /// is fine while <paramref name="p"/> is outside, but says nothing useful once it is inside
    /// (a box returns the point itself, and a heightfield returns the surface directly BELOW,
    /// which would push a sunken character further down).</para>
    /// </summary>
    public virtual Vector3 OutwardNormal(Vector3 p)
    {
        var d = p - ClosestPoint(p);
        if (d.LengthSquared() > 1e-10f && SignedDistance(p) > 0f) return Vector3.Normalize(d);

        const float e = 1e-3f;
        var g = new Vector3(
            SignedDistance(p + new Vector3(e, 0, 0)) - SignedDistance(p - new Vector3(e, 0, 0)),
            SignedDistance(p + new Vector3(0, e, 0)) - SignedDistance(p - new Vector3(0, e, 0)),
            SignedDistance(p + new Vector3(0, 0, e)) - SignedDistance(p - new Vector3(0, 0, e)));
        return g.LengthSquared() > 1e-12f ? Vector3.Normalize(g) : Vector3.UnitY;
    }

    /// <summary>Ray hit against this shape. <paramref name="distance"/> is along a normalised
    /// <paramref name="direction"/>.</summary>
    public abstract bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                                 out float distance, out Vector3 normal);

    // Register on SetOwner rather than Load: a collider is useful the moment it has a transform,
    // and this way it works whether it was attached during Init or spawned mid-game. Register is
    // idempotent, so the later Load call costs nothing.
    public override void SetOwner(SimObject owner)
    {
        base.SetOwner(owner);
        PhysicsWorld.Register(this);
    }

    public override void Load() => PhysicsWorld.Register(this);

    /// <summary>Leaves the physics world. Note that detaching a <see cref="SimObject"/> from the
    /// scene does NOT call this — call it yourself, or <see cref="PhysicsWorld.Clear"/> on a level
    /// swap, or the old level keeps colliding with the new one.</summary>
    public override void Unload() => PhysicsWorld.Unregister(this);

    protected sealed class ColliderData : IComponentData
    {
        public string Name { get; set; } = "collider3d";
        public void SetFromJson(System.Text.Json.JsonElement json) { }
    }
}

/// <summary>An axis-aligned box. Ignores the owner's rotation (see <see cref="Collider"/>).</summary>
public sealed class BoxCollider : Collider
{
    /// <summary>Full box dimensions in local units, before the owner's scale.</summary>
    public Vector3 Size = Vector3.One;

    public BoxCollider() : base(new ColliderData()) { }
    public BoxCollider(Vector3 size, Vector3 center = default) : base(new ColliderData())
    {
        Size = size;
        Center = center;
    }

    // Per-axis scale, unlike the sphere/capsule: a box can represent non-uniform scale exactly,
    // and flattening it to a max would turn every scaled floor into a cube.
    public override Bounds WorldBounds
        => Bounds.FromCenterExtents(WorldCenter, Size * 0.5f * WorldScaleVector);

    public override Vector3 ClosestPoint(Vector3 p) => WorldBounds.ClosestPoint(p);

    public override float SignedDistance(Vector3 p)
    {
        var b = WorldBounds;
        var d = Vector3.Max(b.Min - p, p - b.Max);
        float outside = Vector3.Max(d, Vector3.Zero).Length();
        // Inside the box every component of d is negative; the least-negative one is the distance
        // to the nearest face, which is what a penetration depth should report.
        float inside = MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0f);
        return outside + inside;
    }

    public override bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                                 out float distance, out Vector3 normal)
        => Physics.RayBox(origin, direction, maxDistance, WorldBounds, out distance, out normal);
}

/// <summary>A sphere.</summary>
public sealed class SphereCollider : Collider
{
    public float Radius = 0.5f;

    public SphereCollider() : base(new ColliderData()) { }
    public SphereCollider(float radius, Vector3 center = default) : base(new ColliderData())
    {
        Radius = radius;
        Center = center;
    }

    public float WorldRadius => Radius * WorldScale;

    public override Bounds WorldBounds => Bounds.FromCenterExtents(WorldCenter, new Vector3(WorldRadius));

    public override Vector3 ClosestPoint(Vector3 p)
    {
        var c = WorldCenter;
        var d = p - c;
        float len = d.Length();
        return len < 1e-6f ? c + new Vector3(WorldRadius, 0, 0) : c + d / len * WorldRadius;
    }

    public override float SignedDistance(Vector3 p) => Vector3.Distance(p, WorldCenter) - WorldRadius;

    public override bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                                 out float distance, out Vector3 normal)
        => Physics.RaySphere(origin, direction, maxDistance, WorldCenter, WorldRadius, out distance, out normal);
}

/// <summary>
/// A Y-axis-aligned capsule — the character shape. <see cref="Height"/> is the total height
/// including both caps, so a 1.8m character with a 0.3m radius is Height 1.8, Radius 0.3.
///
/// <para>Capsules are what character controllers use because the rounded bottom slides over steps
/// and terrain seams instead of catching on them the way a box's edge does.</para>
/// </summary>
public sealed class CapsuleCollider : Collider
{
    public float Radius = 0.3f;
    public float Height = 1.8f;

    public CapsuleCollider() : base(new ColliderData()) { }
    public CapsuleCollider(float radius, float height, Vector3 center = default) : base(new ColliderData())
    {
        Radius = radius;
        Height = height;
        Center = center;
    }

    public float WorldRadius => Radius * WorldScale;
    public float WorldHeight => MathF.Max(Height * WorldScale, WorldRadius * 2f);

    /// <summary>Centres of the two hemisphere caps — the endpoints of the capsule's inner segment.
    /// Every capsule test reduces to a segment-distance query against these.</summary>
    public (Vector3 a, Vector3 b) WorldSegment()
    {
        float half = MathF.Max(WorldHeight * 0.5f - WorldRadius, 0f);
        var c = WorldCenter;
        return (c - new Vector3(0, half, 0), c + new Vector3(0, half, 0));
    }

    public override Bounds WorldBounds
    {
        get
        {
            var (a, b) = WorldSegment();
            float r = WorldRadius;
            return new Bounds(Vector3.Min(a, b) - new Vector3(r), Vector3.Max(a, b) + new Vector3(r));
        }
    }

    public override Vector3 ClosestPoint(Vector3 p)
    {
        var (a, b) = WorldSegment();
        var onAxis = Physics.ClosestPointOnSegment(p, a, b);
        var d = p - onAxis;
        float len = d.Length();
        return len < 1e-6f ? onAxis + new Vector3(WorldRadius, 0, 0) : onAxis + d / len * WorldRadius;
    }

    public override float SignedDistance(Vector3 p)
    {
        var (a, b) = WorldSegment();
        return Vector3.Distance(p, Physics.ClosestPointOnSegment(p, a, b)) - WorldRadius;
    }

    public override bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                                 out float distance, out Vector3 normal)
    {
        var (a, b) = WorldSegment();
        return Physics.RayCapsule(origin, direction, maxDistance, a, b, WorldRadius, out distance, out normal);
    }
}

/// <summary>
/// Collision against a <see cref="HeightmapTerrain"/>. Terrain is the one surface a game is always
/// standing on, and approximating it with boxes would need thousands of them — this samples the
/// heightfield directly instead.
/// </summary>
public sealed class TerrainCollider : Collider
{
    private readonly HeightmapTerrain _terrain;

    public TerrainCollider(HeightmapTerrain terrain) : base(new ColliderData()) => _terrain = terrain;

    public HeightmapTerrain Terrain => _terrain;

    /// <summary>Where the heightfield's own origin sits in the world. A terrain sampled in its
    /// local frame has to be offset by this, or a terrain placed anywhere but the world origin
    /// reports heights from the wrong part of the field — and characters walk straight through it.
    /// Translation only: a rotated heightfield is no longer a heightfield.</summary>
    public Vector3 WorldOrigin => Owner?.WorldMatrix.Translation ?? Vector3.Zero;

    /// <summary>Surface height at a WORLD (x, z).</summary>
    public float HeightAt(float x, float z)
    {
        var o = WorldOrigin;
        return o.Y + _terrain.SampleHeight(x - o.X, z - o.Z);
    }

    /// <summary>Surface normal from finite differences of the heightfield — what a character
    /// controller compares against its slope limit.</summary>
    public Vector3 NormalAt(float x, float z)
    {
        float e = _terrain.CellSize * 0.5f;
        float hl = HeightAt(x - e, z), hr = HeightAt(x + e, z);
        float hd = HeightAt(x, z - e), hu = HeightAt(x, z + e);
        return Vector3.Normalize(new Vector3(hl - hr, 2f * e, hd - hu));
    }

    public override Bounds WorldBounds
    {
        get
        {
            var o = WorldOrigin;
            float half = _terrain.WorldSize * 0.5f;
            // Y range is deliberately generous: the exact height range isn't exposed, and an
            // over-large broadphase box only costs a few extra narrowphase tests.
            return new Bounds(new Vector3(o.X - half, o.Y - 1000f, o.Z - half),
                              new Vector3(o.X + half, o.Y + 1000f, o.Z + half));
        }
    }

    public override Vector3 ClosestPoint(Vector3 p) => new(p.X, HeightAt(p.X, p.Z), p.Z);

    public override float SignedDistance(Vector3 p) => p.Y - HeightAt(p.X, p.Z);

    /// <summary>Out of a heightfield is always upward, along the surface normal — never the
    /// straight-down direction that <see cref="ClosestPoint"/> implies for a sunken point.</summary>
    public override Vector3 OutwardNormal(Vector3 p) => NormalAt(p.X, p.Z);

    public override bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                                 out float distance, out Vector3 normal)
        => Physics.RayHeightmap(origin, direction, maxDistance, this, out distance, out normal);
}
