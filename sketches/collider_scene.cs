#:project ../src/BdvEngine/BdvEngine.csproj
// Colliders in the scene format: a level's collision survives save → load → save, and still works.
//
//   dotnet run sketches/collider_scene.cs -- --shot /tmp/collider_scene.png --frames 30
//
// Byte-identical round-trip is only half of it. A collider that round-trips perfectly but sits in
// the wrong place, at the wrong size, or on the wrong layer would pass that check and fail the
// game — so this reloads the file into a cleared PhysicsWorld and then QUERIES it: rays that must
// hit, rays that must miss, and sizes that must have picked up the node's scale.
//
// The last check is the one that motivated SimObject.Unload: reloading a scene must REPLACE its
// colliders, not add a second set on top of the first.
using BdvEngine;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

const string A = "/tmp/bdv_collider_a.json";
const string B = "/tmp/bdv_collider_b.json";

int checks = 0, failed = 0;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 55);
    w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 0.8f, 0), distance: 11, yaw: 0.8f, pitch: 0.42f));
    w.Environment.Sky = new Vector3(0.46f, 0.56f, 0.71f);
    w.Environment.Ambient = new Vector3(0.32f, 0.33f, 0.38f);
    w.Add(new DirectionalLight(new Vector3(-0.5f, -1f, -0.35f)));
    w.Environment.Shadows.Distance = 24f;

    string ground = Materials.Standard("ground", new Color(78, 100, 80));
    string crateM = Materials.Standard("crate", new Color(206, 128, 62));
    string glassM = Materials.Standard("glass", new Color(120, 200, 220));
    string woodM  = Materials.Standard("wood", new Color(150, 116, 78));

    w.Add(Primitives.Plane(24)).Material(ground);

    // ── a crate, scaled 2x. Collider size stays 1: sizes are LOCAL and pick up node scale, so the
    //    world collider must come out 2 units across. Getting this wrong is the classic authoring
    //    bug the format has to not have.
    var crate = w.Add(Primitives.Cube()).At(-3f, 1f, 0).Scale(2f).Material(crateM);
    crate.Object.Name = "crate";
    crate.Object.AddComponent(new BoxCollider(Vector3.One));

    // ── a trigger pickup: reported by overlap queries, never blocks a ray
    var pickup = w.Add(Primitives.Sphere(16, 12)).At(0f, 1f, 0).Scale(0.6f).Material(glassM);
    pickup.Object.Name = "pickup";
    pickup.Object.AddComponent(new SphereCollider(1f) { IsTrigger = true });

    // ── a capsule on its own layer, so a mask can single it out
    var post = w.Add(Primitives.Cube()).At(3f, 0.9f, 0).Scale(0.4f, 1.8f, 0.4f).Material(woodM);
    post.Object.Name = "post";
    post.Object.AddComponent(new CapsuleCollider(0.4f, 1.8f) { Layer = 4 });

    // ── a compound body: two boxes on one node, exercising the "colliders" array
    var table = new SimObject(9200, "table");
    table.Transform.Position = new Vector3(0, 0, -3.5f);
    table.AddComponent(new MeshComponent(Primitives.Cube().Mesh, woodM));
    table.Transform.Scale = new Vector3(2f, 0.1f, 1f);
    table.AddComponent(new BoxCollider(Vector3.One, new Vector3(0, 1f, 0)));
    table.AddComponent(new BoxCollider(new Vector3(0.1f, 1f, 0.1f), new Vector3(-0.9f, 0.5f, 0.4f)));
    w.Add(table);

    int builtInCode = PhysicsWorld.Colliders.Count;

    // ── the gate ────────────────────────────────────────────────────────────
    w.SaveScene(A);

    // Clear first: we want to count what the FILE produced, not what the file produced plus the
    // code-built originals that are still registered.
    PhysicsWorld.Clear();
    var loaded = w.LoadScene(A);
    w.SaveScene(B, loaded);

    Check("round-trip is byte-identical", File.ReadAllText(A) == File.ReadAllText(B),
          $"{new FileInfo(A).Length} bytes");
    Check("every collider came back", PhysicsWorld.Colliders.Count == builtInCode,
          $"{PhysicsWorld.Colliders.Count} loaded vs {builtInCode} built in code");

    // ── the shapes are right, in world space ────────────────────────────────
    var box = Find<BoxCollider>("crate");
    if (box != null)
    {
        var b = box.WorldBounds;
        var size = b.Max - b.Min;
        Check("box size picks up node scale", Near(size.X, 2f) && Near(size.Y, 2f) && Near(size.Z, 2f),
              $"world size {size.X:F2} x {size.Y:F2} x {size.Z:F2} (local 1 x scale 2)");
        Check("box sits where the node does", Near(b.Min.Y + 1f, 1f), $"centre y {(b.Min.Y + b.Max.Y) / 2:F2}");
    }
    else Check("box collider survived", false, "no BoxCollider under 'crate'");

    var cap = Find<CapsuleCollider>("post");
    Check("capsule kept radius/height/layer",
          cap != null && Near(cap.Radius, 0.4f) && Near(cap.Height, 1.8f) && cap.Layer == 4,
          cap == null ? "missing" : $"r={cap.Radius:F2} h={cap.Height:F2} layer={cap.Layer}");

    var tableNode = loaded.GetObjectByName("table");
    int tableShapes = tableNode?.Components.OfType<Collider>().Count() ?? 0;
    Check("compound body kept both shapes", tableShapes == 2, $"{tableShapes} colliders on 'table'");

    // ── and they actually answer queries ────────────────────────────────────
    // Straight down onto the crate: top face is at y=2, so from y=6 that's 4 units.
    bool hitCrate = PhysicsWorld.Raycast(new Vector3(-3, 6, 0), -Vector3.UnitY, 20f, out var crateHit);
    Check("ray hits the loaded crate", hitCrate && Near(crateHit.Distance, 4f, 0.05f),
          hitCrate ? $"hit at {crateHit.Distance:F2} (expected 4.00)" : "no hit");

    // The pickup is a trigger: solid queries must pass straight through it.
    bool blocked = PhysicsWorld.Raycast(new Vector3(0, 6, 0), -Vector3.UnitY, 20f, out _);
    Check("trigger does not block a solid ray", !blocked, blocked ? "it blocked" : "passed through");

    bool sawTrigger = PhysicsWorld.Raycast(new Vector3(0, 6, 0), -Vector3.UnitY, 20f, out var trigHit,
                                           layerMask: ~0, ignore: null, includeTriggers: true);
    Check("trigger IS found when asked for", sawTrigger && trigHit.Collider.IsTrigger,
          sawTrigger ? "found with includeTriggers" : "missed");

    // Layer 4 is the post alone, so a masked ray at the crate's x must find nothing.
    bool wrongLayer = PhysicsWorld.Raycast(new Vector3(-3, 6, 0), -Vector3.UnitY, 20f, out _, layerMask: 4);
    bool rightLayer = PhysicsWorld.Raycast(new Vector3(3, 6, 0), -Vector3.UnitY, 20f, out _, layerMask: 4);
    Check("layer mask filters", !wrongLayer && rightLayer,
          $"crate on mask 4: {(wrongLayer ? "hit (wrong)" : "miss")}, post on mask 4: {(rightLayer ? "hit" : "miss (wrong)")}");

    // ── reload must REPLACE the collider set, not stack a second one on it ──
    int before = PhysicsWorld.Colliders.Count;
    w.ReloadScene(A, loaded);
    int after = PhysicsWorld.Colliders.Count;
    Check("reload replaces colliders, doesn't stack", after == before,
          $"{before} before -> {after} after (a leak would read {before * 2})");

    Console.WriteLine(new string('-', 70));
    Console.WriteLine(failed == 0
        ? $"COLLIDER SCENE PASS — {checks} checks, collision survives the file"
        : $"COLLIDER SCENE FAIL — {failed} of {checks} checks failed");
    Console.WriteLine(new string('-', 70));

    T? Find<T>(string node) where T : Collider
        => loaded.GetObjectByName(node)?.Components.OfType<T>().FirstOrDefault();
});

void Check(string name, bool ok, string detail)
{
    checks++;
    if (!ok) failed++;
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-42} {detail}");
}

static bool Near(float a, float b, float eps = 0.001f) => MathF.Abs(a - b) <= eps;
