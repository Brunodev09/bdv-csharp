#:project ../src/BdvEngine/BdvEngine.csproj
// Shadow gate. Static casters, a skinned caster, and a ground plane to receive them.
//
//   dotnet run sketches/shadow_test.cs -- --shot /tmp/shadow_on.png  --frames 40
//   dotnet run sketches/shadow_test.cs -- --shot /tmp/shadow_off.png --frames 40 --noshadow
//
// Shadows are a GPU-side effect, so the gate is a pixel diff between those two runs: the ground
// must get materially darker in places, and no pixel may get brighter. The animation is frozen
// at a fixed pose so the only difference between the two runs is the shadow pass itself.
using BdvEngine;
using System;
using System.Numerics;

bool shadowsOff = Array.IndexOf(Environment.GetCommandLineArgs(), "--noshadow") >= 0;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 48, near: 0.3f, far: 200f);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 1.0f, 0), distance: 16f,
                                               yaw: 0.85f, pitch: 0.42f));
        w.Environment.Sky = new Vector3(0.50f, 0.60f, 0.74f);
        w.Environment.Ambient = new Vector3(0.30f, 0.31f, 0.36f);
        // A low sun aimed so shadows fall TOWARD the camera. With the sun behind the viewer the
        // shadows land behind their casters and you can't see whether they're right. A shallow
        // angle is also the worst case for shadow acne, so this doubles as the stress test.
        w.Add(new DirectionalLight(new Vector3(0.72f, -0.52f, 0.46f)));

        w.Environment.Shadows.Enabled = !shadowsOff;
        w.Environment.Shadows.Distance = 14f;      // tight box: this scene is small, so spend the texels here

        // Receiver.
        w.Add(Primitives.Plane(40)).Material(Materials.Standard("ground", new Color(120, 132, 112)));

        // Static casters at different heights — a floating sphere makes a detached shadow, which
        // is the clearest check that the depth comparison is using real depth and not a silhouette.
        w.Add(Primitives.Cube()).At(-3.2f, 0.75f, 0.6f).Scale(1.5f)
         .Material(Materials.Standard("crate", new Color(196, 122, 64)));
        w.Add(Primitives.Sphere(28, 20)).At(0.4f, 2.4f, -2.2f).Scale(1.3f)
         .Material(Materials.Standard("orb", new Color(96, 148, 196)));
        w.Add(Primitives.Cube()).At(3.0f, 0.4f, 1.8f).Scale(0.8f, 0.8f, 3.2f)
         .Material(Materials.Standard("beam", new Color(150, 150, 158)));

        // Skinned caster: must cast its POSED shadow, not its bind pose.
        var rig = w.Load("sketches/assets/bendy.glb").At(0.2f, 0f, 2.6f).Scale(1.4f);
        var anim = rig.Object.GetComponent<Animator>();
        // Seek alone isn't enough: the Animator keeps advancing every frame afterwards, and the
        // two runs don't tick at identical rates, so the capsule would be posed slightly
        // differently in each and the pixel diff would blame that on shadows. Speed 0 freezes it.
        if (anim != null) { anim.Play("Bend"); anim.Seek(0.5f); anim.Speed = 0f; }
        else Console.Error.WriteLine("[shadow] rigged asset missing — run tools/make_test_rig.py");

        Console.WriteLine($"[shadow] shadows {(shadowsOff ? "OFF" : "ON")}, " +
                          $"res {w.Environment.Shadows.Resolution}, distance {w.Environment.Shadows.Distance}");
    }
);
