#:project ../src/BdvEngine/BdvEngine.csproj
// LOD gate.
//
//   dotnet run sketches/lod_test.cs -- --shot /tmp/lod_on.png  --frames 40
//   dotnet run sketches/lod_test.cs -- --shot /tmp/lod_off.png --frames 40 --nolod
//
// A long corridor of identical trees receding from the camera. With LOD on, distant ones drop to
// coarser meshes and eventually cull; with it off every tree draws at full detail. The measurement
// is VERTICES DRAWN, which is what LOD actually reduces — draw calls barely move, because
// instancing had already collapsed them.
//
// Also reports how many trees landed on each level, so a wrong threshold shows up as a number
// rather than as a vague impression that the horizon looks chunky.
using BdvEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool noLod = Array.IndexOf(cli, "--nolod") >= 0;

const int Rows = 90;
var lods = new List<LodComponent>();
int frames = 0;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55, near: 0.3f, far: 700f);
        w.Camera.Position = new Vector3(0, 5.5f, 16f);
        w.Camera.Target = new Vector3(0, 3.0f, -60f);
        w.Environment.Sky = new Vector3(0.52f, 0.62f, 0.76f);
        w.Environment.Ambient = new Vector3(0.38f, 0.39f, 0.44f);
        w.Add(new DirectionalLight(new Vector3(0.5f, -0.75f, 0.42f)));
        w.Environment.Shadows.Distance = 40f;

        w.Add(Primitives.Plane(1400)).Material(Materials.Standard("ground", new Color(112, 126, 104)));

        string bark = Materials.Standard("bark", new Color(96, 66, 42));
        string leaf = Materials.Standard("leaf", new Color(52, 104, 58));

        // Three canopy densities. Sphere(24,16) is 425 verts; (10,7) is 88; (5,4) is 30.
        var hi = Primitives.Sphere(24, 16).Mesh;
        var mid = Primitives.Sphere(10, 7).Mesh;
        var lo = Primitives.Sphere(5, 4).Mesh;

        var rng = new SeededRng(5);
        for (int i = 0; i < Rows; i++)
        {
            float z = -4f - i * 7f;
            float s = 0.85f + (float)rng.Next() * 0.5f;
            foreach (float x in new[] { -6f, 6f })
            {
                w.Add(Primitives.Cube()).At(x, 2.0f * s, z).Scale(0.5f * s, 4f * s, 0.5f * s)
                 .Material(bark);

                var canopy = new SimObject(w.NextId(), "canopy");
                canopy.Transform.Position = new Vector3(x, 5.2f * s, z);
                canopy.Transform.Scale = new Vector3(2.6f * s);

                if (noLod)
                {
                    canopy.AddComponent(new MeshComponent(hi, leaf));
                }
                else
                {
                    // Thresholds are PER UNIT OF SCALE and these canopies are scaled ~2.6x, so
                    // the real switch distances are roughly 2.6x these numbers: ~65 / ~156 / ~390,
                    // culling past ~520.
                    var lod = new LodComponent { CullDistance = 200f };
                    lod.Add(hi, leaf, within: 25f);
                    lod.Add(mid, leaf, within: 60f);
                    lod.Add(lo, leaf, within: 150f);
                    canopy.AddComponent(lod);
                    lods.Add(lod);
                }
                w.Add(canopy);
            }
        }

        Console.WriteLine($"[lod] {Rows * 2} canopies, lod={!noLod}");
    },
    update: (w, dt) =>
    {
        if (++frames != 30) return;
        Console.WriteLine($"[lod] VERTS={GLStats.VerticesDrawn} CALLS={GLStats.DrawCalls}");
        if (lods.Count == 0) return;

        var hist = new int[4];   // index 3 = culled
        foreach (var l in lods) hist[l.CurrentLevel < 0 ? 3 : l.CurrentLevel]++;
        Console.WriteLine($"[lod] LEVELS hi={hist[0]} mid={hist[1]} lo={hist[2]} culled={hist[3]}");
    }
);
