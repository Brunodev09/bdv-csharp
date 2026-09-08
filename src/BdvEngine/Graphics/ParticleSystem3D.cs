using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

/// <summary>Where new particles are born, and which way they leave.</summary>
public enum EmitterShape
{
    /// <summary>All from the emitter origin.</summary>
    Point,
    /// <summary>Anywhere inside a sphere of <see cref="ParticleSystem3D.Radius"/>, moving outward.</summary>
    Sphere,
    /// <summary>From a disc of <see cref="ParticleSystem3D.Radius"/>, within
    /// <see cref="ParticleSystem3D.ConeAngle"/> of <see cref="ParticleSystem3D.Direction"/>. The
    /// default, because most effects (fire, exhaust, sparks, fountains) are cones.</summary>
    Cone,
    /// <summary>Anywhere inside a box of <see cref="ParticleSystem3D.BoxSize"/>.</summary>
    Box,
}

/// <summary>How particles combine with what's behind them.</summary>
public enum ParticleBlend
{
    /// <summary>Standard transparency. Smoke, dust, debris, splashes.</summary>
    Alpha,
    /// <summary>Colours add, so overlaps brighten and black is invisible. Fire, sparks, magic,
    /// muzzle flashes — anything that emits light rather than blocking it.</summary>
    Additive,
}

/// <summary>
/// A 3D particle emitter: camera-facing quads simulated on the CPU and drawn in ONE instanced draw
/// call, whatever the particle count.
///
/// <code>
/// var fire = new SimObject(w.NextId(), "campfire");
/// fire.Transform.Position = new Vector3(0, 0.3f, 0);
/// fire.AddComponent(new ParticleSystem3D
/// {
///     EmissionRate = 60, MaxParticles = 400,
///     Blend        = ParticleBlend.Additive,
///     ConeAngle    = 18f, Radius = 0.18f,
///     SpeedMin     = 0.8f, SpeedMax = 1.6f,
///     LifetimeMin  = 0.5f, LifetimeMax = 1.1f,
///     SizeStart    = 0.35f, SizeEnd = 0.05f,
///     ColorStart   = new Color(255, 190, 80), ColorEnd = new Color(180, 30, 0, 0),
///     Gravity      = new Vector3(0, 0.6f, 0),   // fire rises
/// });
/// w.Add(fire);
/// </code>
///
/// <para><b>Simulation is on the CPU, rendering is one instanced call.</b> At the scale this engine
/// targets — a few thousand particles across a scene — the simulation is far cheaper than the
/// per-particle draw calls a naive version would issue, and keeping it on the CPU means collision,
/// gameplay hooks and deterministic seeding all stay possible. The GPU work is one 10-float
/// instance record per live particle and a four-vertex triangle strip built in the vertex shader,
/// so there is no per-particle mesh at all.</para>
///
/// <para><b>Ordering.</b> Systems are sorted back-to-front against each other and drawn after
/// opaque and transparent geometry, depth-tested but not depth-writing. Within a system,
/// <see cref="ParticleBlend.Alpha"/> also sorts its own particles back-to-front each frame (order
/// matters for alpha); <see cref="ParticleBlend.Additive"/> skips that, because addition is
/// commutative and the sort would buy nothing.</para>
///
/// <para><b>Not covered:</b> particle collision, sub-emitters, GPU simulation, and soft
/// (depth-faded) particles — a quad intersecting the ground shows a hard edge. Trails are better
/// done as a separate ribbon primitive than bolted on here.</para>
/// </summary>
public sealed class ParticleSystem3D : BaseComponent
{
    /// <summary>Floats per particle in the instance buffer: centre(3), size(2), rotation(1),
    /// colour(4).</summary>
    internal const int FloatsPerParticle = 10;

    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age, Lifetime;
        public float Rotation, Spin;
        public float SizeJitter;    // 0..1, keeps a system from looking like clones
        public float Seed;          // 0..1, per-particle colour variation
    }

    // ── emission ────────────────────────────────────────────────────────────
    /// <summary>Particles per second. 0 with <see cref="Emitting"/> on means burst-only.</summary>
    [Range(0f, 2000f)] public float EmissionRate = 40f;

    /// <summary>Hard cap on live particles. The pool is allocated once at this size; emission
    /// stalls rather than growing it, so a runaway effect costs frames, never memory.</summary>
    public int MaxParticles = 256;

    /// <summary>Stop spawning (live particles still finish their lives — the difference between
    /// "turn the tap off" and "delete the water").</summary>
    public bool Emitting = true;

    /// <summary>True: a particle keeps its world position after birth, so moving the emitter leaves
    /// a trail behind it. False: particles live in the emitter's local space and follow it around.
    /// True is what you want for smoke from a moving vehicle; false for a shield shimmer.</summary>
    public bool WorldSpace = true;

    // ── shape ───────────────────────────────────────────────────────────────
    public EmitterShape Shape = EmitterShape.Cone;

    /// <summary>Sphere/cone radius.</summary>
    [Range(0f, 20f)] public float Radius = 0.15f;

    /// <summary>Half-angle of the cone, in degrees. 0 is a straight beam, 90 a hemisphere.</summary>
    [Range(0f, 90f)] public float ConeAngle = 20f;

    /// <summary>Full extents for <see cref="EmitterShape.Box"/>.</summary>
    public Vector3 BoxSize = Vector3.One;

    /// <summary>Cone axis, in the emitter's local space. Normalised on use.</summary>
    public Vector3 Direction = Vector3.UnitY;

    // ── per-particle ────────────────────────────────────────────────────────
    [Range(0f, 100f)] public float SpeedMin = 1f;
    [Range(0f, 100f)] public float SpeedMax = 2f;
    [Range(0.01f, 60f)] public float LifetimeMin = 0.6f;
    [Range(0.01f, 60f)] public float LifetimeMax = 1.4f;

    /// <summary>Size at birth and at death, in world units. Interpolated linearly over the
    /// particle's life.</summary>
    [Range(0f, 50f)] public float SizeStart = 0.3f;
    [Range(0f, 50f)] public float SizeEnd = 0.05f;

    /// <summary>Fraction each particle's size is randomly scaled by, so they don't look stamped
    /// from one mould. 0.3 means sizes land in 0.7x..1.3x.</summary>
    [Range(0f, 1f)] public float SizeVariation = 0.25f;

    /// <summary>Colour at birth and at death. The alpha channel is the fade — an effect that
    /// should vanish rather than pop needs <see cref="ColorEnd"/> alpha 0.</summary>
    public Color ColorStart = new(255, 255, 255, 255);
    public Color ColorEnd = new(255, 255, 255, 0);

    /// <summary>World-space acceleration. Negative Y for falling debris, positive for rising smoke
    /// and fire — buoyancy and gravity are the same term with opposite signs.</summary>
    public Vector3 Gravity = new(0, -2f, 0);

    /// <summary>Velocity lost per second, as a fraction. 0 is vacuum; 2 or so reads as air for
    /// smoke and dust.</summary>
    [Range(0f, 20f)] public float Drag;

    /// <summary>Spin range in radians/second, sampled per particle and signed both ways.</summary>
    [Range(0f, 20f)] public float SpinMax = 1.5f;

    // ── render ──────────────────────────────────────────────────────────────
    public ParticleBlend Blend = ParticleBlend.Alpha;

    /// <summary>Texture asset name. Empty uses a soft round dot generated in code, so a system
    /// works with no art at all — which is the difference between "particles are implemented" and
    /// "particles are usable".</summary>
    public string Texture = "";

    /// <summary>Deterministic seed. Two systems built the same with the same seed produce the same
    /// effect, which is what makes a screenshot-diff test of a particle system possible.</summary>
    public int Seed = 1337;

    // ── state ───────────────────────────────────────────────────────────────
    private Particle[] _pool = Array.Empty<Particle>();
    private int _live;
    private double _spawnDebt;
    private SeededRng _rng;
    private Vector3 _boundsMin, _boundsMax;

    /// <summary>Live particle count — what a stats overlay or a test asserts on.</summary>
    public int LiveCount => _live;

    /// <summary>World-space bounds of the live particles, for frustum culling. Meaningless when
    /// <see cref="LiveCount"/> is 0.</summary>
    public Bounds WorldBounds => new(_boundsMin, _boundsMax);

    public ParticleSystem3D() : base(new ParticleData()) => _rng = new SeededRng(Seed);

    /// <summary>Emit <paramref name="count"/> particles at once, ignoring
    /// <see cref="EmissionRate"/> — explosions, impacts, pickups.</summary>
    public void Burst(int count)
    {
        for (int i = 0; i < count; i++) Spawn();
    }

    /// <summary>Kill every live particle immediately.</summary>
    public void Clear() => _live = 0;

    /// <summary>Restart with a fresh RNG, so a replayed effect matches the first run exactly.</summary>
    public void Restart()
    {
        _live = 0;
        _spawnDebt = 0;
        _rng = new SeededRng(Seed);
    }

    public override void Update(double deltaTime)
    {
        float dt = (float)deltaTime;
        if (dt <= 0f) return;

        EnsurePool();

        // Fractional spawns carry over rather than rounding away: at 12 particles/second and 60fps
        // a per-frame round-down would emit nothing at all.
        if (Emitting && EmissionRate > 0f)
        {
            _spawnDebt += EmissionRate * dt;
            while (_spawnDebt >= 1.0)
            {
                _spawnDebt -= 1.0;
                if (!Spawn()) { _spawnDebt = 0; break; }   // pool full: drop the backlog
            }
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        float drag = MathF.Max(Drag, 0f);

        for (int i = _live - 1; i >= 0; i--)
        {
            ref var p = ref _pool[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                // Swap-with-last: O(1) removal, and particle order carries no meaning because the
                // renderer sorts by depth anyway.
                _pool[i] = _pool[--_live];
                continue;
            }

            p.Velocity += Gravity * dt;
            if (drag > 0f) p.Velocity *= MathF.Max(1f - drag * dt, 0f);
            p.Position += p.Velocity * dt;
            p.Rotation += p.Spin * dt;

            min = Vector3.Min(min, p.Position);
            max = Vector3.Max(max, p.Position);
        }

        if (_live == 0) { _boundsMin = _boundsMax = Vector3.Zero; return; }

        // Pad by the largest a particle can draw, so a quad whose centre is just off-screen isn't
        // culled while half of it is still visible.
        float pad = MathF.Max(SizeStart, SizeEnd) * (1f + SizeVariation) * 0.5f;
        var padding = new Vector3(pad);
        min -= padding;
        max += padding;

        // Local-space particles are simulated in the emitter's frame, so the box just computed is
        // in LOCAL coordinates. Culling and sorting both want world coordinates: without this a
        // local-space system is tested against a box sitting wherever its local origin happens to
        // be, which culls visible effects and draws off-screen ones.
        if (!WorldSpace && _owner != null) (min, max) = TransformBounds(min, max, _owner.WorldMatrix);

        _boundsMin = min;
        _boundsMax = max;
    }

    /// <summary>World-space AABB of a local-space AABB. All eight corners have to be transformed —
    /// taking just min and max is only correct for an axis-aligned, positively-scaled matrix, and
    /// silently wrong the moment the emitter is rotated.</summary>
    private static (Vector3 min, Vector3 max) TransformBounds(Vector3 lo, Vector3 hi, Matrix4x4 m)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? lo.X : hi.X,
                                     (i & 2) == 0 ? lo.Y : hi.Y,
                                     (i & 4) == 0 ? lo.Z : hi.Z);
            var w = Vector3.Transform(corner, m);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }
        return (min, max);
    }

    private void EnsurePool()
    {
        int cap = Math.Max(MaxParticles, 0);
        if (_pool.Length == cap) return;
        Array.Resize(ref _pool, cap);
        if (_live > cap) _live = cap;
    }

    private bool Spawn()
    {
        EnsurePool();
        if (_live >= _pool.Length) return false;

        var (localPos, dir) = SampleShape();

        // World-space particles are born at their world position and then forget the emitter;
        // local-space ones stay in local coordinates and get transformed at draw time.
        Vector3 pos = localPos, vel = dir * Lerp(SpeedMin, SpeedMax, Rand());
        if (WorldSpace && _owner != null)
        {
            var m = _owner.WorldMatrix;
            pos = Vector3.Transform(localPos, m);
            vel = Vector3.TransformNormal(vel, m);
        }

        _pool[_live++] = new Particle
        {
            Position = pos,
            Velocity = vel,
            Age = 0f,
            Lifetime = MathF.Max(Lerp(LifetimeMin, LifetimeMax, Rand()), 0.01f),
            Rotation = Rand() * MathF.Tau,
            Spin = (Rand() * 2f - 1f) * SpinMax,
            SizeJitter = 1f + (Rand() * 2f - 1f) * SizeVariation,
            Seed = Rand(),
        };
        return true;
    }

    private (Vector3 pos, Vector3 dir) SampleShape()
    {
        var axis = Direction.LengthSquared() > 1e-8f ? Vector3.Normalize(Direction) : Vector3.UnitY;

        switch (Shape)
        {
            case EmitterShape.Point:
                return (Vector3.Zero, axis);

            case EmitterShape.Sphere:
            {
                var d = RandomUnitVector();
                // Cube-root keeps the distribution uniform by VOLUME; without it particles bunch
                // at the centre, which reads as a dense core rather than a sphere.
                return (d * Radius * MathF.Cbrt(Rand()), d);
            }

            case EmitterShape.Box:
            {
                var half = BoxSize * 0.5f;
                var p = new Vector3((Rand() * 2f - 1f) * half.X,
                                    (Rand() * 2f - 1f) * half.Y,
                                    (Rand() * 2f - 1f) * half.Z);
                return (p, axis);
            }

            default:   // Cone
            {
                Basis(axis, out var right, out var up);
                float a = Rand() * MathF.Tau;
                float r = MathF.Sqrt(Rand());                  // uniform over the disc
                var offset = (right * MathF.Cos(a) + up * MathF.Sin(a)) * r * Radius;

                float spread = MathF.Tan(ConeAngle * MathF.PI / 180f);
                float ba = Rand() * MathF.Tau;
                float br = MathF.Sqrt(Rand()) * spread;
                var dir = Vector3.Normalize(axis + (right * MathF.Cos(ba) + up * MathF.Sin(ba)) * br);
                return (offset, dir);
            }
        }
    }

    /// <summary>Any two axes perpendicular to <paramref name="n"/>. The branch avoids the
    /// degenerate cross product when <paramref name="n"/> is itself near the reference axis.</summary>
    private static void Basis(Vector3 n, out Vector3 right, out Vector3 up)
    {
        var reference = MathF.Abs(n.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        right = Vector3.Normalize(Vector3.Cross(reference, n));
        up = Vector3.Cross(n, right);
    }

    private Vector3 RandomUnitVector()
    {
        // Sample z uniformly then pick an angle: uniform on the sphere's surface, unlike
        // normalising a random cube vector, which clusters toward the corners.
        float z = Rand() * 2f - 1f;
        float a = Rand() * MathF.Tau;
        float r = MathF.Sqrt(MathF.Max(1f - z * z, 0f));
        return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    private float Rand() => (float)_rng.Next();
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Fill <paramref name="into"/> with this system's instance records and return the count.
    ///
    /// <para>Called by the renderer once per frame. <paramref name="camPos"/> drives the
    /// back-to-front sort for <see cref="ParticleBlend.Alpha"/>.</para>
    /// </summary>
    internal int BuildInstances(ref float[] into, Vector3 camPos, ref int[] order)
    {
        if (_live == 0) return 0;

        int need = _live * FloatsPerParticle;
        if (into.Length < need) into = new float[Math.Max(need, into.Length * 2)];

        if (order.Length < _live) order = new int[Math.Max(_live, order.Length * 2)];
        for (int i = 0; i < _live; i++) order[i] = i;

        var model = WorldSpace || _owner == null ? Matrix4x4.Identity : _owner.WorldMatrix;

        if (Blend == ParticleBlend.Alpha)
        {
            // Alpha blending is order-dependent, so draw far particles first. Additive is
            // commutative and skips this entirely.
            var pool = _pool;
            Array.Sort(order, 0, _live, Comparer<int>.Create((x, y) =>
            {
                float dx = Vector3.DistanceSquared(camPos, Vector3.Transform(pool[x].Position, model));
                float dy = Vector3.DistanceSquared(camPos, Vector3.Transform(pool[y].Position, model));
                return dy.CompareTo(dx);
            }));
        }

        var c0 = ColorStart.ToVector4();
        var c1 = ColorEnd.ToVector4();

        for (int n = 0; n < _live; n++)
        {
            ref var p = ref _pool[order[n]];
            float t = Math.Clamp(p.Age / p.Lifetime, 0f, 1f);

            var world = WorldSpace ? p.Position : Vector3.Transform(p.Position, model);
            float size = Lerp(SizeStart, SizeEnd, t) * p.SizeJitter;
            var col = Vector4.Lerp(c0, c1, t);

            int o = n * FloatsPerParticle;
            into[o + 0] = world.X; into[o + 1] = world.Y; into[o + 2] = world.Z;
            into[o + 3] = size;    into[o + 4] = size;
            into[o + 5] = p.Rotation;
            into[o + 6] = col.X; into[o + 7] = col.Y; into[o + 8] = col.Z; into[o + 9] = col.W;
        }
        return _live;
    }

    private sealed class ParticleData : IComponentData
    {
        public string Name { get; set; } = "particles";
        public void SetFromJson(JsonElement json) { }
    }
}

/// <summary>Registers <see cref="ParticleSystem3D"/> with the component registry, so a system
/// serialises into a <c>.scene.json</c> through the generic field bridge. Every one of its fields
/// is a bridge-supported type (numbers, bools, strings, enums, <c>Vector3</c>, <c>Color</c>), so it
/// needs no bespoke read/write path the way meshes and LOD levels do — an effect authored in the
/// F1 inspector saves out and comes back exactly as tuned.</summary>
public sealed class ParticleSystem3DBuilder : IComponentBuilder
{
    public System.Type ComponentType => typeof(ParticleSystem3D);

    public string Type => "particles3d";

    // Fields are restored by SceneSerializer via SceneJson.ApplyFields after construction, so the
    // builder only has to hand back a default instance.
    public IComponent BuildFromJson(JsonElement json) => new ParticleSystem3D();
}
