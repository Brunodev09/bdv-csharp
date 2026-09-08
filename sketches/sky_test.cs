#:project ../src/BdvEngine/BdvEngine.csproj
// Skybox + fog gate.
//
//   dotnet run sketches/sky_test.cs -- --shot /tmp/sky.png       --frames 40
//   dotnet run sketches/sky_test.cs -- --shot /tmp/sky_off.png   --frames 40 --off
//   dotnet run sketches/sky_test.cs -- --shot /tmp/sky_dusk.png  --frames 40 --dusk
//
// Pillars marching into the distance, so fog has a depth range to work over and the sky has a
// horizon to meet. --off is the reference (flat clear, no fog); --dusk moves the sun to the
// horizon to check the glow tracks it.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool off = Array.IndexOf(cli, "--off") >= 0;
bool dusk = Array.IndexOf(cli, "--dusk") >= 0;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 55, near: 0.3f, far: 600f);
    w.Camera.Position = new Vector3(0, 3.2f, 12f);
    w.Camera.Target = new Vector3(0, 3.0f, -40f);

    // Sun low and to the side at dusk, high and behind the camera at midday.
    var sunTravel = dusk ? new Vector3(-0.62f, -0.10f, -0.78f) : new Vector3(0.45f, -0.80f, -0.40f);
    w.Add(new DirectionalLight(sunTravel, dusk ? new Vector3(1.0f, 0.62f, 0.34f)
                                               : new Vector3(1.0f, 0.97f, 0.90f)));
    w.Environment.Ambient = dusk ? new Vector3(0.20f, 0.19f, 0.26f) : new Vector3(0.34f, 0.35f, 0.40f);

    // The flat clear colour, used when the gradient is off — and what the gradient replaces.
    w.Environment.Sky = new Vector3(0.55f, 0.66f, 0.82f);

    var sky = w.Environment.SkyGradient;
    sky.Enabled = !off;
    if (dusk)
    {
        sky.Horizon = new Vector3(0.92f, 0.52f, 0.30f);
        sky.Zenith = new Vector3(0.10f, 0.16f, 0.38f);
        sky.Ground = new Vector3(0.14f, 0.12f, 0.13f);
        sky.SunGlow = 1.4f;
    }

    var fog = w.Environment.Fog;
    fog.Enabled = !off;
    fog.Density = 0.0075f;      // fades out around 330 units, matching the pillar run

    w.Environment.Shadows.Distance = 55f;

    w.Add(Primitives.Plane(1200)).Material(Materials.Standard("ground", new Color(104, 118, 96)));

    // Pillars receding to ~400 units, so the fog ramp is visible across their length rather than
    // being a single step somewhere off screen.
    var rng = new SeededRng(11);
    string stone = Materials.Standard("stone", new Color(178, 172, 160));
    string roof = Materials.Standard("roof", new Color(150, 92, 74));
    for (int i = 0; i < 60; i++)
    {
        float z = -6f - i * 6.5f;
        float h = 4f + (float)rng.Next() * 3f;
        foreach (float x in new[] { -5.5f, 5.5f })
        {
            w.Add(Primitives.Cube()).At(x, h * 0.5f, z).Scale(1.1f, h, 1.1f).Material(stone);
            w.Add(Primitives.Cube()).At(x, h + 0.35f, z).Scale(2.0f, 0.7f, 2.0f).Material(roof);
        }
    }

    Console.WriteLine($"[sky] gradient={sky.Enabled} fog={fog.Enabled} density={fog.Density} dusk={dusk}");
});
