#:project ../src/BdvEngine/BdvEngine.csproj
// Load and frame any .glb, with a ground plane so it casts a shadow.
//
//   dotnet run sketches/view_model.cs -- --model "/path/to/thing.glb" --shot /tmp/model.png
//
// Auto-frames the camera on the model's actual world bounds, because a downloaded asset can be in
// centimetres, metres or arbitrary units and guessing a camera distance wastes a run every time.
using BdvEngine;
using System;
using System.Diagnostics;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
int mi = Array.IndexOf(cli, "--model");
string path = mi >= 0 && mi + 1 < cli.Length ? cli[mi + 1] : "sketches/assets/bendy.glb";

Sketch.Run(setup: w =>
{
    w.Environment.Sky = new Vector3(0.52f, 0.60f, 0.72f);
    w.Environment.Ambient = new Vector3(0.36f, 0.37f, 0.42f);
    w.Add(new DirectionalLight(new Vector3(0.55f, -0.75f, 0.38f)));

    var sw = Stopwatch.StartNew();
    var model = w.Load(path);
    Console.WriteLine($"[view] loaded in {sw.ElapsedMilliseconds} ms");

    // Bounds must come from world matrices, which only exist after a rebake.
    w.Scene.RebakeMatrices();
    var (min, max) = WorldBounds(model.Object);
    var size = max - min;
    var centre = (min + max) * 0.5f;
    float radius = MathF.Max(size.Length() * 0.5f, 0.001f);
    Console.WriteLine($"[view] bounds {min:F2} .. {max:F2}  (size {size:F2})");

    // Ground at the model's feet, sized to it, so the shadow has somewhere to land.
    w.Add(Primitives.Plane(radius * 8f)).At(centre.X, min.Y, centre.Z)
     .Material(Materials.Standard("ground", new Color(122, 130, 118)));

    w.Camera.Perspective(fovDegrees: 42f, near: radius * 0.01f, far: radius * 40f);
    w.Camera.AddControls(new OrbitControls(
        target: new Vector3(centre.X, centre.Y, centre.Z),
        distance: radius * 2.6f, yaw: 0.55f, pitch: 0.12f)
    { MinDistance = radius * 0.2f, MaxDistance = radius * 20f });

    // Shadow texel budget follows the model's size, or a human-sized asset gets a shadow map
    // covering a football pitch and the edges turn to mush.
    w.Environment.Shadows.Distance = radius * 1.8f;
    w.Environment.Shadows.Bias = 0.0022f;

    Console.WriteLine($"[view] {CountMeshes(model.Object)} mesh components, "
                    + $"shadow distance {w.Environment.Shadows.Distance:F2}");
});

static (Vector3 min, Vector3 max) WorldBounds(SimObject root)
{
    var min = new Vector3(float.MaxValue);
    var max = new Vector3(float.MinValue);
    Walk(root);
    if (min.X > max.X) { min = Vector3.Zero; max = Vector3.One; }   // nothing with geometry
    return (min, max);

    void Walk(SimObject o)
    {
        foreach (var c in o.Components)
        {
            Mesh? m = c switch
            {
                MeshComponent mc => mc.Mesh,
                SkinnedMeshComponent smc => smc.Mesh,
                _ => null,
            };
            if (m == null) continue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? m.BoundsMin.X : m.BoundsMax.X,
                    (i & 2) == 0 ? m.BoundsMin.Y : m.BoundsMax.Y,
                    (i & 4) == 0 ? m.BoundsMin.Z : m.BoundsMax.Z);
                var p = Vector3.Transform(corner, o.WorldMatrix);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        foreach (var ch in o.Children) Walk(ch);
    }
}

static int CountMeshes(SimObject o)
{
    int n = 0;
    foreach (var c in o.Components) if (c is MeshComponent or SkinnedMeshComponent) n++;
    foreach (var ch in o.Children) n += CountMeshes(ch);
    return n;
}
