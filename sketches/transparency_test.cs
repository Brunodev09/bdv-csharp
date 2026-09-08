#:project ../src/BdvEngine/BdvEngine.csproj
// Transparency gate.
//
//   dotnet run sketches/transparency_test.cs -- --shot /tmp/tr_a.png --frames 40
//   dotnet run sketches/transparency_test.cs -- --shot /tmp/tr_b.png --frames 40 --reverse
//   dotnet run sketches/transparency_test.cs -- --shot /tmp/tr_back.png --frames 40 --behind
//
// The decisive test is --reverse: the same three overlapping panes, added to the scene in the
// OPPOSITE order. Sorted correctly the two images are identical, because draw order is decided by
// distance rather than by whoever was added first. Unsorted they differ, which is the bug.
//
// --behind puts the camera on the far side, checking the sort follows the viewer rather than a
// fixed axis. An opaque cube sits behind the panes throughout: it must stay visible, which only
// holds if transparent geometry leaves depth writes off.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool reverse = Array.IndexOf(cli, "--reverse") >= 0;
bool behind = Array.IndexOf(cli, "--behind") >= 0;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 50, near: 0.3f, far: 200f);
    w.Camera.Position = behind ? new Vector3(0, 3.5f, -14f) : new Vector3(0, 3.5f, 14f);
    w.Camera.Target = new Vector3(0, 2.2f, 0);
    w.Environment.Sky = new Vector3(0.52f, 0.62f, 0.76f);
    w.Environment.Ambient = new Vector3(0.44f, 0.45f, 0.50f);
    w.Add(new DirectionalLight(new Vector3(0.4f, -0.85f, 0.35f)));
    w.Environment.Shadows.Distance = 20f;

    w.Add(Primitives.Plane(60)).Material(Materials.Standard("ground", new Color(120, 132, 112)));

    // Opaque marker BEHIND every pane. If transparent geometry wrote depth, whichever pane drew
    // first would reject this and it would vanish.
    w.Add(Primitives.Cube()).At(0, 1.2f, -4.5f).Scale(2.4f)
     .Material(Materials.Standard("marker", new Color(210, 170, 60)));

    // Three translucent panes at different depths. Alpha < 255 in the colour is enough — the
    // material infers BlendMode.Alpha from it.
    // Offset diagonally so each pane is visible alone AND in overlap: in the overlaps the NEARER
    // colour must dominate, which is the thing sorting decides.
    var panes = new (float x, float z, string name, Color color)[]
    {
        (-2.2f, -2.0f, "far",  new Color(230,  60,  60, 140)),
        ( 0.0f,  0.0f, "mid",  new Color( 60, 215,  85, 140)),
        ( 2.2f,  2.0f, "near", new Color( 60, 110, 240, 140)),
    };
    if (reverse) Array.Reverse(panes);

    foreach (var (x, z, name, color) in panes)
    {
        // Unlit: the pane's colour IS its colour, so the blend order reads directly instead of
        // being muddied by three ambient-only layers desaturating toward grey.
        var mat = Materials.Standard(name, color);
        MaterialManager.TryPeek(name, out var m);
        m.Shading = MaterialShading.Unlit;

        // NOT double-sided. These panes are thin BOXES, so they already have a face pointing at
        // the viewer from either side; turning culling off would draw the front AND back of each,
        // double-blending every pane and leaving almost nothing of the background visible.
        w.Add(Primitives.Cube()).At(x, 2.2f, z).Scale(5.5f, 4.0f, 0.08f).Material(mat);
    }

    MaterialManager.TryPeek("near", out var probe);
    Console.WriteLine($"[tr] insertion={(reverse ? "near-first" : "far-first")} camera={(behind ? "behind" : "front")} "
                    + $"| inferred blend={probe.Blend} castShadows={probe.CastShadows}");
});
