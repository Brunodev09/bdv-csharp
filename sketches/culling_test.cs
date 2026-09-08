#:project ../src/BdvEngine/BdvEngine.csproj
// Frustum culling + instancing gate.
//
//   dotnet run sketches/culling_test.cs -- --shot /tmp/cull_on.png  --frames 40
//   dotnet run sketches/culling_test.cs -- --shot /tmp/cull_off.png --frames 40 --naive
//
// The whole point of both optimisations is that they change the COST, never the PICTURE. So the
// gate is a pixel diff between the optimised and naive paths plus a draw-call count: the images
// must be byte-identical and the call count must drop.
//
// Reports GLStats draw calls so an outer script can compare the two runs.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool naive = Array.IndexOf(cli, "--naive") >= 0;
bool noCull = naive || Array.IndexOf(cli, "--no-cull") >= 0;
bool noInst = naive || Array.IndexOf(cli, "--no-inst") >= 0;
int frames = 0;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55, near: 0.3f, far: 300f);
        // A fixed camera (no orbit controls) so both runs frame the scene identically.
        w.Camera.Position = new Vector3(0, 14f, 46f);
        w.Camera.Target = new Vector3(0, 3f, 0);
        w.Environment.Sky = new Vector3(0.50f, 0.60f, 0.74f);
        w.Environment.Ambient = new Vector3(0.34f, 0.35f, 0.40f);
        w.Add(new DirectionalLight(new Vector3(0.6f, -0.7f, 0.4f)));
        w.Environment.Shadows.Distance = 40f;

        w.Environment.Culling = !noCull;
        w.Environment.Instancing = !noInst;

        w.Add(Primitives.Plane(400)).Material(Materials.Standard("ground", new Color(116, 130, 108)));

        // A grid of pillars stretching well past the camera's frustum in every direction, so
        // culling has plenty to reject — and repeated so instancing has plenty to batch.
        var rng = new SeededRng(99);
        string trunk = Materials.Standard("trunk", new Color(150, 108, 70));
        string leaf = Materials.Standard("leaf", new Color(58, 108, 60));
        int n = 0;
        for (int gx = -14; gx <= 14; gx++)
        for (int gz = -14; gz <= 14; gz++)
        {
            float x = gx * 7f + ((float)rng.Next() - 0.5f) * 2.5f;
            float z = gz * 7f + ((float)rng.Next() - 0.5f) * 2.5f;
            float s = 0.8f + (float)rng.Next() * 0.7f;

            w.Add(Primitives.Cube()).At(x, 1.6f * s, z).Scale(0.5f * s, 3.2f * s, 0.5f * s).Material(trunk);
            w.Add(Primitives.Sphere(14, 10)).At(x, 4.2f * s, z).Scale(2.2f * s).Material(leaf);
            n += 2;
        }

        Console.WriteLine($"[cull] {n} objects, culling={w.Environment.Culling} instancing={w.Environment.Instancing}");
    },
    update: (w, dt) =>
    {
        // Report once the scene has settled, from a frame the capture will also see.
        if (++frames == 30)
            Console.WriteLine($"[cull] DRAWCALLS={GLStats.DrawCalls}");
    }
);
