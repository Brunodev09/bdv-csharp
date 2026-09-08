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

    /// <summary>Path of the most recent <see cref="LoadScene"/>, and the container it produced —
    /// what the editor's Save button writes back to. Null until a scene file is loaded (a purely
    /// code-built world has nowhere to save to until you name a file).</summary>
    public string? LoadedScenePath { get; private set; }
    public SimObject? LoadedSceneRoot { get; private set; }

    /// <summary>Hand out the next object id — for code building objects outside the fluent Add
    /// helpers (the editor duplicating a node, a prefab instancing itself).</summary>
    public int NextId() => _nextId++;

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
        root.Source = path;   // so SceneSerializer can write the node back as a model reference
        root.SourceKind = AssetKind.Model;
        Scene.AddObject(root);
        return new ObjectHandle(root, null);
    }

    /// <summary>Instance a <c>.prefab.json</c> into this world — compose once, place many:
    /// <code>
    /// for (int i = 0; i &lt; 400; i++)
    ///     World.Instantiate("prefabs/pine.prefab.json").At(x, groundY, z).Scale(0.8f + rng);
    /// </code>
    /// The prefab file is read and its materials registered once; every instance after that is
    /// built from the cached JSON. Because an instance saves back as just its path plus its
    /// transform, editing the prefab file changes all of them. Call <see cref="SimObject.Unpack"/>
    /// on one to sever the link and make it an ordinary node.</summary>
    public ObjectHandle Instantiate(string prefabPath)
    {
        var node = SceneSerializer.Instantiate(prefabPath, NextId);
        Scene.AddObject(node);
        if (Gfx.Gl != null) node.Load();   // Load is idempotent, so Init-time calls are safe too
        return new ObjectHandle(node, null);
    }

    /// <summary>Write a node out as a reusable <c>.prefab.json</c> — the editor's "Save as prefab".</summary>
    public void SavePrefab(string path, SimObject node) => SceneSerializer.SavePrefab(path, node);

    /// <summary>Load a <c>.scene.json</c> into this world — the authored half of a level as data
    /// (see <see cref="SceneSerializer"/>). The file's nodes land under a single container object
    /// which is returned, so a reload is one child swapped rather than a whole-scene rebuild:
    /// <code>
    /// World.LoadScene("levels/forest.scene.json");   // in Init(), after the GL context exists
    /// </code>
    /// The file's <c>environment</c> and <c>materials</c> blocks are applied to this world.
    /// Call from <see cref="Game.Init"/> or later; the container is loaded immediately so meshes
    /// and textures upload even though the engine's one-shot <c>Scene.Load()</c> has passed.</summary>
    public SimObject LoadScene(string path)
    {
        var container = SceneSerializer.Load(this, path, () => _nextId++);
        Scene.AddObject(container);
        container.Load();   // Engine calls Scene.Load() once, after Init — later subtrees load here.
        LoadedScenePath = path;
        LoadedSceneRoot = container;
        return container;
    }

    /// <summary>Replace a previously loaded scene container with a fresh load of the same file —
    /// the hot-reload swap. Returns the new container; the old one is detached.</summary>
    public SimObject ReloadScene(string path, SimObject previous)
    {
        var fresh = SceneSerializer.Load(this, path, () => _nextId++);
        Scene.RemoveObject(previous);
        Scene.AddObject(fresh);
        fresh.Load();
        LoadedScenePath = path;
        LoadedSceneRoot = fresh;
        return fresh;
    }

    /// <summary>Write this world out as a <c>.scene.json</c>. With no <paramref name="root"/> the
    /// whole world is saved — the "bake" path that turns generated content into an editable file.
    /// Pass the container from <see cref="LoadScene"/> to save just that level back.</summary>
    public void SaveScene(string path, SimObject? root = null) => SceneSerializer.Save(this, path, root);

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
            {
                // LOD objects pick against level 0: the most detailed level has the largest bounds,
                // so a click can't miss because the object happens to be showing a coarser one.
                Mesh? m = comps[i] switch
                {
                    MeshComponent mc => mc.Mesh,
                    LodComponent { Levels.Count: > 0 } lod => lod.Levels[0].Mesh,
                    _ => null,
                };
                if (m != null && RayHitsMesh(ray, o.WorldMatrix, m, out float t) && t < bestT)
                {
                    bestT = t;
                    best = o;
                }
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
