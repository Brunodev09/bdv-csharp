using System;
using System.Numerics;

namespace BdvEngine;

/// <summary>
/// The single author-facing surface for a level (Q3 = Option B). Wraps a pure <see cref="Scene"/>
/// (the node graph) and owns the <see cref="Camera"/> and <see cref="WorldEnvironment"/>. Objects
/// are added fluently and just render — no manual scene pumping, no per-object id bookkeeping:
/// <code>
/// World.Add(Primitives.Cube()).At(0, 1, 0).Material(Materials.Standard(Color.Red));
/// </code>
/// </summary>
public sealed class World
{
    public Scene Scene { get; } = new();
    public Camera Camera { get; } = new();
    public WorldEnvironment Environment { get; } = new();

    private int _nextId = 1;

    /// <summary>Add a primitive/mesh to the world; returns a handle for fluent placement + material.</summary>
    public ObjectHandle Add(MeshSpec spec)
    {
        var obj = new SimObject(_nextId++, $"obj_{_nextId}");
        Scene.AddObject(obj);
        return new ObjectHandle(obj, spec.Mesh);
    }

    /// <summary>Add a pre-built object (already carrying its own components/children).</summary>
    public ObjectHandle Add(SimObject obj)
    {
        Scene.AddObject(obj);
        return new ObjectHandle(obj, null);
    }

    /// <summary>Load a <c>.glb</c> model into the world; returns a handle to its root for
    /// fluent placement — <c>World.Load("hero.glb").At(2, 0, 0)</c>.</summary>
    public ObjectHandle Load(string path)
    {
        var root = GlbLoader.Load(path, () => _nextId++);
        Scene.AddObject(root);
        return new ObjectHandle(root, null);
    }

    /// <summary>Raycast the scene and return the nearest object whose mesh bounds the ray hits
    /// (or null). Build the ray from a mouse pixel via <see cref="Camera.ScreenRay"/>. Pass
    /// <paramref name="ignore"/> to skip an object (e.g. a pick-marker).</summary>
    public SimObject? Pick(Ray ray, SimObject? ignore = null)
    {
        SimObject? best = null;
        float bestT = float.MaxValue;
        PickWalk(Scene.Root, ray, ignore, ref best, ref bestT);
        return best;
    }

    private static void PickWalk(SimObject o, in Ray ray, SimObject? ignore, ref SimObject? best, ref float bestT)
    {
        if (o != ignore)
        {
            var comps = o.Components;
            for (int i = 0; i < comps.Count; i++)
                if (comps[i] is MeshComponent mc && RayHitsMesh(ray, o.WorldMatrix, mc.Mesh, out float t) && t < bestT)
                {
                    bestT = t;
                    best = o;
                }
        }
        var ch = o.Children;
        for (int i = 0; i < ch.Count; i++) PickWalk(ch[i], ray, ignore, ref best, ref bestT);
    }

    // Test the ray against the mesh's local AABB transformed into a world-space AABB (loose for
    // rotated meshes, but cheap and good enough for click selection).
    private static bool RayHitsMesh(in Ray ray, in Matrix4x4 world, Mesh mesh, out float t)
    {
        var min = mesh.BoundsMin;
        var max = mesh.BoundsMax;
        var wmin = new Vector3(float.MaxValue);
        var wmax = new Vector3(float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            var corner = new Vector3((c & 1) == 0 ? min.X : max.X,
                                     (c & 2) == 0 ? min.Y : max.Y,
                                     (c & 4) == 0 ? min.Z : max.Z);
            var w = Vector3.Transform(corner, world);
            wmin = Vector3.Min(wmin, w);
            wmax = Vector3.Max(wmax, w);
        }
        return RayAabb(ray, wmin, wmax, out t);
    }

    private static bool RayAabb(in Ray ray, Vector3 min, Vector3 max, out float t)
    {
        t = 0f;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            float o = Axis(ray.Origin, a), d = Axis(ray.Direction, a);
            float mn = Axis(min, a), mx = Axis(max, a);
            if (MathF.Abs(d) < 1e-8f) { if (o < mn || o > mx) return false; }
            else
            {
                float inv = 1f / d;
                float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
        }
        if (tmax < 0f) return false;
        t = tmin >= 0f ? tmin : tmax;
        return true;
    }

    private static float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    /// <summary>Set the scene's primary sun (light 0). Additional lights are scene nodes — see
    /// <see cref="AddPointLight"/> / <see cref="AddDirectionalLight"/>.</summary>
    public void Add(DirectionalLight light) => Environment.Sun = light;

    /// <summary>Add a point light as a scene node; returns a handle so it can be moved/parented.</summary>
    public ObjectHandle AddPointLight(Vector3 position, Color color, float intensity = 1f, float range = 20f)
    {
        var obj = new SimObject(_nextId++, "point_light");
        obj.Transform.Position = position;
        obj.AddComponent(new LightComponent
        {
            Type = LightType.Point,
            Color = new Vector3(color.RFloat, color.GFloat, color.BFloat),
            Intensity = intensity,
            Range = range,
        });
        Scene.AddObject(obj);
        return new ObjectHandle(obj, null);
    }

    /// <summary>Add a camera-facing 2D sprite anchored at a 3D world position (Phase 8). Colour
    /// only for now (flat quad); returns a handle so it can be moved/parented. Width/height are in
    /// world units.</summary>
    public ObjectHandle AddBillboard(Vector3 position, Color color, float width, float height)
    {
        var mat = Materials.Unlit(color);   // flat colour via the shared white texture
        var obj = new SimObject(_nextId++, "billboard");
        obj.Transform.Position = position;
        obj.AddComponent(new BillboardComponent(mat, width, height));
        Scene.AddObject(obj);
        return new ObjectHandle(obj, null);
    }

    /// <summary>Add an extra directional light as a scene node (beyond the environment sun).</summary>
    public ObjectHandle AddDirectionalLight(Vector3 direction, Color color, float intensity = 1f)
    {
        var obj = new SimObject(_nextId++, "dir_light");
        obj.AddComponent(new LightComponent
        {
            Type = LightType.Directional,
            Color = new Vector3(color.RFloat, color.GFloat, color.BFloat),
            Intensity = intensity,
            Direction = direction,
        });
        Scene.AddObject(obj);
        return new ObjectHandle(obj, null);
    }
}

/// <summary>
/// Fluent handle over a freshly-added object. Attaching the mesh is deferred until
/// <see cref="Material"/> is called (a <see cref="MeshComponent"/> needs its material name at
/// construction). Call <see cref="Material"/> to make a primitive render.
/// </summary>
public sealed class ObjectHandle
{
    public SimObject Object { get; }
    private Mesh? _pendingMesh;

    internal ObjectHandle(SimObject obj, Mesh? pendingMesh)
    {
        Object = obj;
        _pendingMesh = pendingMesh;
    }

    public ObjectHandle At(float x, float y, float z)
    {
        Object.Transform.Position = new Vector3(x, y, z);
        return this;
    }

    public ObjectHandle At(Vector3 position)
    {
        Object.Transform.Position = position;
        return this;
    }

    public ObjectHandle Scale(float uniform)
    {
        Object.Transform.Scale = new Vector3(uniform);
        return this;
    }

    public ObjectHandle Scale(float x, float y, float z)
    {
        Object.Transform.Scale = new Vector3(x, y, z);
        return this;
    }

    public ObjectHandle RotateEuler(float x, float y, float z)
    {
        Object.Transform.Rotation = new Vector3(x, y, z);
        return this;
    }

    /// <summary>Attach the pending mesh with this material. No-op if there is no pending mesh.</summary>
    public ObjectHandle Material(string materialName)
    {
        if (_pendingMesh != null)
        {
            Object.AddComponent(new MeshComponent(_pendingMesh, materialName));
            _pendingMesh = null;
        }
        return this;
    }

    public ObjectHandle Add(IBehavior behavior)
    {
        Object.AddBehavior(behavior);
        return this;
    }

    public ObjectHandle Add(IComponent component)
    {
        Object.AddComponent(component);
        return this;
    }
}
