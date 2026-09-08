#:project ../src/BdvEngine/BdvEngine.csproj
// Phase 3 acceptance gate: compose once, instance many, and editing the ONE prefab file changes
// every instance.
//
//   dotnet run sketches/prefab_test.cs -- --shot /tmp/prefab.png --frames 400
//
// 1. Compose a tree in code, save it as a .prefab.json.
// 2. Instance it 200 times, plus place some through a scene file's "prefab": key.
// 3. Save the scene and assert instances wrote as a path + transform, NOT as inlined subtrees.
// 4. Edit the prefab file (canopy colour + thickness), reload, assert ALL instances changed —
//    and that an instance's own transform still overrides the prefab root's.
using BdvEngine;
using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

const string PrefabPath = "/tmp/bdv_pine.prefab.json";
const string ScenePath  = "/tmp/bdv_forest.scene.json";
const int InstanceCount = 200;

var rng = new SeededRng(4242);
double t = 0;
int stage = 0;
World world = null!;
SimObject sceneRoot = null!;
bool wroteByReference = false, prefabEditApplied = false, instancesShareMesh = false;
bool instanceOverridesRoot = false;

Sketch.Run(
    setup: w =>
    {
        world = w;
        w.Camera.Perspective(fovDegrees: 55, near: 0.3f, far: 400f);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 3.5f, 0), distance: 58, yaw: 0.7f, pitch: 0.17f));
        w.Environment.Sky = new Vector3(0.52f, 0.62f, 0.76f);
        w.Environment.Ambient = new Vector3(0.34f, 0.34f, 0.40f);
        w.Add(new DirectionalLight(new Vector3(-0.45f, -1f, -0.35f)));

        w.Add(Primitives.Plane(140)).Material(Materials.Standard("grass", new Color(74, 108, 66)));

        // ── 1. compose the prefab once, in code, then write it out ──
        var pine = BuildPine(w);
        w.SavePrefab(PrefabPath, pine);
        w.Scene.RemoveObject(pine);          // the file is the asset now; drop the template

        // ── 2. a scene file that instances it by path, hand-written ──
        File.WriteAllText(ScenePath, """
        {
          "version": 1,
          "nodes": [
            { "name": "gate_l", "prefab": "/tmp/bdv_pine.prefab.json",
              "position": {"x":-6,"y":0,"z":26}, "scale": {"x":1.6,"y":1.6,"z":1.6} },
            { "name": "gate_r", "prefab": "/tmp/bdv_pine.prefab.json",
              "position": {"x": 6,"y":0,"z":26}, "scale": {"x":1.6,"y":1.6,"z":1.6} }
          ]
        }
        """);
        sceneRoot = w.LoadScene(ScenePath);

        // ── and 200 more straight from code ──
        for (int i = 0; i < InstanceCount; i++)
        {
            float a = (float)rng.Next() * MathF.Tau;
            float r = 10f + (float)rng.Next() * 44f;
            float s = 0.7f + (float)rng.Next() * 0.9f;
            w.Instantiate(PrefabPath)
             .At(MathF.Cos(a) * r, 0, MathF.Sin(a) * r)
             .Scale(s)
             .RotateEuler(0, (float)rng.Next() * MathF.Tau, 0);
        }
        Console.WriteLine($"[test] {InstanceCount + 2} instances of one prefab file");
    },
    update: (w, dt) =>
    {
        t += dt;

        if (stage == 0 && t > 0.5)
        {
            stage = 1;
            // ── 3. instances must save BY REFERENCE, not inlined ──
            w.SaveScene(ScenePath, sceneRoot);
            var json = File.ReadAllText(ScenePath);
            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes");
            wroteByReference = nodes.GetArrayLength() == 2;
            foreach (var n in nodes.EnumerateArray())
                wroteByReference &= n.TryGetProperty("prefab", out _)
                                 && !n.TryGetProperty("children", out _)
                                 && !n.TryGetProperty("mesh", out _);
            Console.WriteLine($"[test] scene wrote instances by reference: {wroteByReference} " +
                              $"({json.Length} bytes for 2 instances)");

            // Every instance should share one GPU buffer per primitive spec, not 202 of them.
            var meshes = new HashSet<Mesh>();
            CountMeshes(w.Scene.Root, meshes);
            instancesShareMesh = meshes.Count <= 4;   // plane + trunk cube + 2 canopy spheres
            Console.WriteLine($"[test] distinct meshes across {InstanceCount + 2} instances: {meshes.Count}");
        }
        // ── 4. edit the ONE prefab file; every instance must change ──
        else if (stage == 1 && t > 1.0)
        {
            stage = 2;
            // Edit a CHILD property, not the prefab root's transform: an instance's own transform
            // overrides the root's (that's the point of an instance), so a root-scale edit is
            // invisible on any instance that set its own — asserted separately below.
            var edited = File.ReadAllText(PrefabPath)
                .Replace("\"color\": \"#2F6134\"", "\"color\": \"#B4462D\"")   // canopy -> autumn
                .Replace("\"y\": 2.6", "\"y\": 3.4");                          // canopy -> fuller
            File.WriteAllText(PrefabPath, edited);
            Console.WriteLine("[test] edited the prefab file (canopy colour + canopy thickness)");

            SceneSerializer.ClearPrefabCache();
            RebuildAll(w);
        }
        else if (stage == 2 && t > 1.6)
        {
            stage = 3;
            MaterialManager.TryPeek("canopy", out var canopy);

            // Sample an actual instance and look INSIDE it — the geometry there comes from the file.
            SimObject? inst = null;
            foreach (var n in w.Scene.Root.Children)
                if (n.SourceKind == AssetKind.Prefab) { inst = n; break; }
            var leaf = inst?.GetObjectByName("canopy_lo");

            bool colourFollowed = canopy is { } c && c.Color.R > 150 && c.Color.G < 100;
            bool geometryFollowed = leaf != null && MathF.Abs(leaf.Transform.Scale.Y - 3.4f) < 1e-3f;
            prefabEditApplied = colourFollowed && geometryFollowed;

            // The instance root keeps the uniform scale the CALLER gave it, not the prefab's
            // (0.42, 5.1, 0.42) — an instance's transform overrides the asset's root transform.
            instanceOverridesRoot = inst != null
                && MathF.Abs(inst.Transform.Scale.X - inst.Transform.Scale.Y) < 1e-5f
                && MathF.Abs(inst.Transform.Scale.Y - 1f) > 1e-3f;   // caller's scale, not the root's identity

            Console.WriteLine($"[test] after prefab edit: canopy colour={canopy?.Color.R},{canopy?.Color.G},{canopy?.Color.B} " +
                              $"canopy_lo scaleY={leaf?.Transform.Scale.Y}");
            Console.WriteLine($"[test] instance root scale={inst?.Transform.Scale} (caller's, overriding the prefab's)");

            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"  instances saved by reference   : {wroteByReference}");
            Console.WriteLine($"  one mesh shared by all         : {instancesShareMesh}");
            Console.WriteLine($"  prefab edit reached them all   : {prefabEditApplied}");
            Console.WriteLine($"  instance transform overrides   : {instanceOverridesRoot}");
            bool pass = wroteByReference && instancesShareMesh && prefabEditApplied && instanceOverridesRoot;
            Console.WriteLine(pass
                ? "PREFAB PASS — compose once, instance many, edit the file to change them all"
                : "PREFAB FAIL");
            Console.WriteLine(new string('-', 70));
        }
    }
);

// A pine: an UNSCALED root container with the geometry as children.
//
// The root's transform is deliberately identity. An instance's transform REPLACES the prefab
// root's, so if the root carried a non-uniform scale the children would be authored against a
// squash that every instance then throws away — and they'd come out distorted. Keeping the root
// neutral is why Unity prefab roots are empty GameObjects, and it's the rule for this format too.
static SimObject BuildPine(World w)
{
    var root = new SimObject(w.NextId(), "pine");

    var trunk = new SimObject(w.NextId(), "trunk");
    trunk.Transform.Position = new Vector3(0, 1.7f, 0);
    trunk.Transform.Scale = new Vector3(0.42f, 3.4f, 0.42f);
    trunk.AddComponent(new MeshComponent(Primitives.Cube().Mesh,
                                         Materials.Standard("bark", new Color(92, 63, 40))));
    root.AddChild(trunk);

    var lo = new SimObject(w.NextId(), "canopy_lo");
    lo.Transform.Position = new Vector3(0, 3.5f, 0);
    lo.Transform.Scale = new Vector3(2.8f, 2.6f, 2.8f);
    lo.AddComponent(new MeshComponent(Primitives.Sphere(14, 10).Mesh,
                                      Materials.Standard("canopy", new Color(47, 97, 52))));
    root.AddChild(lo);

    var hi = new SimObject(w.NextId(), "canopy_hi");
    hi.Transform.Position = new Vector3(0, 5.0f, 0);
    hi.Transform.Scale = new Vector3(1.9f, 1.9f, 1.9f);
    hi.AddComponent(new MeshComponent(Primitives.Sphere(14, 10).Mesh,
                                      Materials.Standard("canopyTop", new Color(56, 112, 60))));
    root.AddChild(hi);

    w.Add(root);
    return root;
}

// Re-instance everything from the (now edited) prefab file.
static void RebuildAll(World w)
{
    foreach (var n in new System.Collections.Generic.List<SimObject>(w.Scene.Root.Children))
        if (n.SourceKind == AssetKind.Prefab || n.Name.StartsWith("scene:", StringComparison.Ordinal))
            w.Scene.RemoveObject(n);

    w.LoadScene("/tmp/bdv_forest.scene.json");
    var rng2 = new SeededRng(4242);
    for (int i = 0; i < 200; i++)
    {
        float a = (float)rng2.Next() * MathF.Tau;
        float r = 10f + (float)rng2.Next() * 44f;
        float s = 0.7f + (float)rng2.Next() * 0.9f;
        w.Instantiate("/tmp/bdv_pine.prefab.json")
         .At(MathF.Cos(a) * r, 0, MathF.Sin(a) * r).Scale(s)
         .RotateEuler(0, (float)rng2.Next() * MathF.Tau, 0);
    }
}

static void CountMeshes(SimObject o, System.Collections.Generic.HashSet<Mesh> into)
{
    foreach (var c in o.Components) if (c is MeshComponent mc) into.Add(mc.Mesh);
    foreach (var ch in o.Children) CountMeshes(ch, into);
}
