#:project ../src/BdvEngine/BdvEngine.csproj
// Loads the island baked out of ValheimGame:
//   cd src/Examples/Valheim && dotnet run -- --bake /tmp/island.scene.json
//   dotnet run sketches/island_load.cs -- --shot /tmp/island.png
//
// The terrain is deliberately ABSENT: it's a procedural heightmap regenerated from noise every
// run, so it stays in code and the serialiser says so out loud rather than faking it. What the
// file carries is the AUTHORED content — 97 trees, 90 rocks, the player — which is exactly the
// procedural/authored line this format is meant to draw.
using BdvEngine;
using System.Numerics;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 45f, near: 0.3f, far: 480f);
    w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 6, 0), distance: 110, yaw: 0.8f, pitch: 0.35f));
    w.LoadScene("/tmp/island.scene.json");
});
