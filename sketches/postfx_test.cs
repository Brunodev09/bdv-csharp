#:project ../src/BdvEngine/BdvEngine.csproj
// 3D post-processing gate.
//
//   dotnet run sketches/postfx_test.cs -- --shot /tmp/pfx.png --frames 20
//   dotnet run sketches/postfx_test.cs -- --shot /tmp/pfx_off.png --frames 20 --off
//   dotnet run sketches/postfx_test.cs -- --shot /tmp/pfx_none.png --frames 20 --tonemap none
//
// A fixed camera on a scene lit hard enough to push pixels past 1.0 in the HDR buffer: a strong
// point light close to white spheres, plus a bright sun. Everything is static and the camera never
// moves, so two runs differing only in a post-fx flag are pixel-comparable and the harness can
// attribute any difference to that flag alone.
//
// Flags exist for every knob so tools/check_postfx.py can isolate one at a time.
using BdvEngine;
using System;
using System.Globalization;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool off = Has("--off");

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 48, near: 0.2f, far: 120f);
    w.Camera.Position = new Vector3(0, 2.0f, 7.2f);
    w.Camera.Target = new Vector3(0, 1.05f, 0);

    w.Environment.Sky = new Vector3(0.06f, 0.07f, 0.11f);
    w.Environment.Ambient = new Vector3(0.13f, 0.13f, 0.17f);
    w.Add(new DirectionalLight(new Vector3(-0.4f, -0.9f, -0.35f), new Vector3(0.5f, 0.5f, 0.55f)));
    w.Environment.Shadows.Distance = 20f;

    // ── post-fx configuration, every knob overridable from the command line ──
    var fx = w.Environment.PostFx;
    fx.Enabled    = !off;
    fx.Exposure   = Num("--exposure", 1.15f);
    fx.Tonemap    = Arg("--tonemap") switch
    {
        "none"     => TonemapMode.None,
        "reinhard" => TonemapMode.Reinhard,
        _          => TonemapMode.Aces,
    };
    fx.Contrast   = Num("--contrast", 1f);
    fx.Saturation = Num("--saturation", 1f);
    fx.Vignette   = Num("--vignette", 0f);
    fx.Gamma      = Num("--gamma", 2.2f);

    fx.Bloom.Enabled   = !Has("--nobloom");
    fx.Bloom.Threshold = Num("--threshold", 1.0f);
    fx.Bloom.Intensity = Num("--bloom", 0.9f);
    fx.Bloom.Iterations = (int)Num("--iterations", 3);

    w.Add(Primitives.Plane(60)).Material(Materials.Standard("ground", new Color(40, 42, 50)));

    // Three white spheres in a row, sitting right under a bright point light. White + close + a
    // high-intensity light is what drives the lit result above 1.0, which is the only reason a
    // threshold at 1.0 has anything to find.
    string chalk = Materials.Standard("chalk", new Color(250, 248, 244));
    for (int i = -1; i <= 1; i++)
        w.Add(Primitives.Sphere(28, 20)).At(i * 2.1f, 1.0f, 0).Scale(0.85f).Material(chalk);

    // Bright enough that the sphere highlights clear 1.0 in the HDR buffer -- which is what gives
    // the threshold something to find -- while the rest of the frame stays in range.
    w.AddPointLight(new Vector3(0, 2.0f, 1.8f), new Color(255, 238, 210), intensity: 18f, range: 6f);

    // Saturated blocks: the grading checks need colour to work on, and a greyscale-only scene
    // would let a broken saturation term pass unnoticed.
    w.Add(Primitives.Cube()).At(-4.4f, 0.7f, -1.2f).Scale(1.1f)
     .Material(Materials.Standard("crimson", new Color(220, 40, 45)));
    w.Add(Primitives.Cube()).At(4.4f, 0.7f, -1.2f).Scale(1.1f)
     .Material(Materials.Standard("azure", new Color(40, 110, 230)));

    Console.WriteLine($"POSTFX enabled={fx.Enabled} tonemap={fx.Tonemap} exposure={fx.Exposure:F2} " +
                      $"bloom={(fx.Bloom.Enabled ? fx.Bloom.Intensity : 0f):F2} " +
                      $"threshold={fx.Bloom.Threshold:F2} vignette={fx.Vignette:F2} " +
                      $"saturation={fx.Saturation:F2}");
});

bool Has(string flag) => Array.IndexOf(cli, flag) >= 0;

string? Arg(string flag)
{
    int i = Array.IndexOf(cli, flag);
    return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null;
}

float Num(string flag, float fallback)
    => float.TryParse(Arg(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
