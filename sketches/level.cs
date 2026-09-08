#:project ../src/BdvEngine/BdvEngine.csproj
// The whole point of Phase 1: the level is a FILE, and this is all the C# it takes to run it.
// Edit sketches/levels/handwritten.scene.json while this is running — it reloads live.
//   dotnet run sketches/level.cs
//   dotnet run sketches/level.cs -- --shot /tmp/level.png --frames 60
using BdvEngine;
using System.Numerics;

HotReloadableScene level = null!;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 1.2f, 0), distance: 15, yaw: 0.6f, pitch: 0.38f));
        level = new HotReloadableScene(w, "sketches/levels/handwritten.scene.json");
    },
    update: (w, dt) => level.Tick()
);
