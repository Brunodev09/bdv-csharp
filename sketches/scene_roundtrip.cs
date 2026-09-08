#:project ../src/BdvEngine/BdvEngine.csproj
// Phase 1 acceptance gate: a scene survives save → load → save byte-for-byte.
//
//   dotnet run sketches/scene_roundtrip.cs -- --shot /tmp/roundtrip.png
//
// Builds a scene in code, writes it to A, loads A back into a fresh container, writes THAT to B,
// and compares. The rendered frame shows only the reloaded copy — so the PNG is the visual half of
// the proof (the file rebuilt the scene) and the A==B diff is the data half (nothing was lost).
using BdvEngine;
using System;
using System.IO;
using System.Numerics;

const string A = "/tmp/bdv_scene_a.json";
const string B = "/tmp/bdv_scene_b.json";

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 0.6f, 0), distance: 9, yaw: 0.7f, pitch: 0.45f));
        w.Environment.Sky = new Vector3(0.45f, 0.55f, 0.70f);
        w.Environment.Ambient = new Vector3(0.30f, 0.30f, 0.36f);
        w.Add(new DirectionalLight(new Vector3(-0.5f, -1f, -0.35f)));

        // ── a scene exercising every corner of the format ──
        w.Add(Primitives.Plane(20)).Material(Materials.Standard("ground", new Color(78, 100, 80)));

        // PBR material + non-default sphere tessellation
        w.Add(Primitives.Sphere(20, 14)).At(-2.2f, 0.6f, 0)
         .Material(Materials.Pbr(new Color(220, 190, 90), metallic: 1f, roughness: 0.25f));

        // behavior through the builder registry + non-uniform scale
        bool spin = Array.IndexOf(Environment.GetCommandLineArgs(), "--static") < 0;
        w.Add(Primitives.Cube()).At(0f, 0.5f, 0).Scale(1f, 1.6f, 1f)
         .Material(Materials.Standard("crate", new Color(210, 120, 60)))
         .Add(new RotationBehavior(new RotationBehaviorData
             { Name = "spin", Rotation = new Vector3(0, spin ? 0.8f : 0f, 0) }));

        // a child node (hierarchy) + quaternion orientation
        var parent = w.Add(Primitives.Cube()).At(2.4f, 0.5f, 0)
                      .Material(Materials.Standard("stone", new Color(150, 150, 158)));
        parent.Object.Transform.LookRotation(new Vector3(1, 0, 1));
        var child = new SimObject(9001, "child_marker");
        child.Transform.Position = new Vector3(0, 1.4f, 0);
        child.AddComponent(new MeshComponent(Primitives.Sphere(12, 8).Mesh,
                                             Materials.Standard("marker", Color.Cyan)));
        parent.Object.AddChild(child);

        // a component whose SetFromJson ignores some of its own public fields (Color, DebugDraw)
        parent.Object.AddComponent(new ColliderComponent(new ColliderComponentData
        {
            Name = "hitbox", Shape = ColliderShape.Circle, Radius = 1.5f,
            IsStatic = true, Color = Color.Magenta, DebugDraw = false,
        }));

        // an LOD group: three levels with different meshes, plus a cull distance
        var lodObj = new SimObject(9100, "lod_bush");
        lodObj.Transform.Position = new Vector3(-4.4f, 0.5f, 1.2f);
        Materials.Standard("bushFar", new Color(58, 108, 66));
        var lod = new LodComponent { CullDistance = 180f, Hysteresis = 0.15f };
        lod.Add(Primitives.Sphere(20, 14).Mesh, Materials.Standard("bush", new Color(70, 130, 80)), within: 20f);
        lod.Add(Primitives.Sphere(10, 7).Mesh, "bush", within: 60f);
        lod.Add(Primitives.Sphere(5, 4).Mesh, "bushFar", within: 140f);
        lodObj.AddComponent(lod);
        w.Add(lodObj);

        // light + billboard nodes
        w.AddPointLight(new Vector3(3, 3.5f, 2), Color.White, intensity: 6, range: 14);
        w.AddBillboard(new Vector3(0, 2.2f, 0), Color.Red, width: 0.9f, height: 0.14f);

        // ── the gate ──
        w.SaveScene(A);                       // 1. code-built world  → A
        var loaded = w.LoadScene(A);          // 2. A                 → fresh container
        w.SaveScene(B, loaded);               // 3. that container    → B

        // Show only ONE copy. `--keep original` renders the code-built scene, `--keep loaded`
        // (default) the one rebuilt from the file — shoot both and diff the PNGs to prove the
        // round-trip is visually identical, not just byte-identical.
        bool keepOriginal = Array.IndexOf(Environment.GetCommandLineArgs(), "original") >= 0;
        foreach (var n in new System.Collections.Generic.List<SimObject>(w.Scene.Root.Children))
            if ((n == loaded) == keepOriginal) w.Scene.RemoveObject(n);

        Report();
    }
);

static void Report()
{
    string a = File.ReadAllText(A), b = File.ReadAllText(B);
    Console.WriteLine(new string('-', 66));
    if (a == b)
    {
        Console.WriteLine($"ROUND-TRIP PASS — {A} == {B} ({a.Length} bytes, byte-identical)");
    }
    else
    {
        Console.WriteLine($"ROUND-TRIP FAIL — {A} != {B}");
        string[] la = a.Split('\n'), lb = b.Split('\n');
        int shown = 0;
        for (int i = 0; i < Math.Max(la.Length, lb.Length) && shown < 20; i++)
        {
            string x = i < la.Length ? la[i] : "<eof>", y = i < lb.Length ? lb[i] : "<eof>";
            if (x == y) continue;
            Console.WriteLine($"  line {i + 1}:\n    A: {x.Trim()}\n    B: {y.Trim()}");
            shown++;
        }
    }
    Console.WriteLine(new string('-', 66));
}
