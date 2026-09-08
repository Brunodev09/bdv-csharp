#:project ../src/BdvEngine/BdvEngine.csproj
// Alpha-tested (cutout) shadow casting for foliage.
//
//   dotnet run sketches/cutout_test.cs -- --shot /tmp/cutout.png --frames 40
//   dotnet run sketches/cutout_test.cs -- --shot /tmp/cutout_off.png --frames 40 --solid
//
// A quad with a holed texture must cast a HOLED shadow, not the rectangle it is modelled as.
// --solid renders the same quad as a plain opaque material for comparison.
//
// The CARD looks identical in both runs, and that is exactly the bug. GL blending is on globally,
// so an alpha-0 texel blends away in the colour pass whether or not the material is a cutout — the
// card reads as a leaf either way. Only the depth pass differs: without alpha testing it writes
// the whole quad, so a leaf-shaped card casts a rectangular shadow. Run
// tools/check_cutout.py for the measured version.
//
// The texture is generated in code — a ring with four gaps — so the test needs no binary asset and
// the expected hole pattern is known exactly.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool solid = Array.IndexOf(cli, "--solid") >= 0;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 45, near: 0.3f, far: 120f);
    // Fixed camera looking down at the ground, so the shadow fills the frame and both runs match.
    w.Camera.Position = new Vector3(0, 11f, 11f);
    w.Camera.Target = new Vector3(0, 0.4f, 0);
    w.Environment.Sky = new Vector3(0.52f, 0.62f, 0.76f);
    w.Environment.Ambient = new Vector3(0.42f, 0.43f, 0.48f);
    // Sun almost straight down, so the card's shadow lands right under it and reads clearly.
    w.Add(new DirectionalLight(new Vector3(0.12f, -1f, 0.16f)));
    w.Environment.Shadows.Distance = 12f;
    w.Environment.Shadows.Resolution = 2048;

    w.Add(Primitives.Plane(40)).Material(Materials.Standard("ground", new Color(150, 158, 140)));

    // ── a texture with real holes: a ring, cut by four radial gaps ──
    const int N = 128;
    var px = new byte[N * N * 4];
    for (int y = 0; y < N; y++)
    for (int x = 0; x < N; x++)
    {
        float u = (x + 0.5f) / N * 2f - 1f;
        float v = (y + 0.5f) / N * 2f - 1f;
        float r = MathF.Sqrt(u * u + v * v);
        float a = MathF.Atan2(v, u);
        // Ring between r 0.35 and 0.9, with four gaps where the angle is near a multiple of 90 deg.
        bool inRing = r > 0.35f && r < 0.90f;
        bool inGap = MathF.Abs(MathF.Cos(a * 2f)) > 0.93f;
        bool opaque = inRing && !inGap;

        int o = (y * N + x) * 4;
        px[o + 0] = 60; px[o + 1] = 150; px[o + 2] = 70;
        px[o + 3] = (byte)(opaque ? 255 : 0);
    }
    var tex = Texture.CreateBlank("leafTex", N, N);
    tex.UploadRgba(N, N, px);
    TextureManager.Register("leafTex", tex);

    var mat = new Material("leaf", "leafTex", Color.White);
    if (!solid)
    {
        mat.Blend = BlendMode.Cutout;    // must be explicit: the transparency is in the TEXTURE
        mat.AlphaCutoff = 0.5f;
    }
    mat.DoubleSided = true;              // a flat card seen from either side
    MaterialManager.Register(mat);

    // A flat card lying almost horizontally, a little above the ground.
    var card = w.Add(Primitives.Plane(6f)).At(0, 2.2f, 0).Material("leaf");
    card.Object.Name = "card";

    Console.WriteLine($"[cut] blend={mat.Blend} cutoff={mat.EffectiveCutoff} castShadows={mat.CastShadows}");
});
