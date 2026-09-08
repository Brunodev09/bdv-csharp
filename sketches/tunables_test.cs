#:project ../src/BdvEngine/BdvEngine.csproj
// Phase 4 gate: materials.json and the [Tunable] registry.
//
//   dotnet run sketches/tunables_test.cs -- --shot /tmp/tunables.png --frames 40 --editor
//
// Checks the loop both features exist for: change a value, save it, and have the change survive
// a reload — without a recompile. Also checks the two things that must FAIL loudly rather than
// appear to work: a const can't be tuned, and an unsupported field type has no widget.
using BdvEngine;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

const string TuningPath = "/tmp/bdv_tuning.json";
const string MatsPath = "/tmp/bdv_materials.json";

int stage = 0;
double t = 0;
var results = new System.Collections.Generic.List<(string, bool, string)>();

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 50);
        w.Camera.AddControls(new OrbitControls(new Vector3(0, 1f, 0), distance: 9f, yaw: 0.7f, pitch: 0.35f));
        w.Add(new DirectionalLight(new Vector3(0.5f, -0.8f, 0.4f)));
        w.Environment.Ambient = new Vector3(0.36f, 0.37f, 0.42f);

        w.Add(Primitives.Plane(30)).Material(Materials.Standard("ground", new Color(118, 130, 110)));
        w.Add(Primitives.Cube()).At(-1.6f, 0.6f, 0).Scale(1.2f)
         .Material(Materials.Standard("crate", new Color(200, 130, 70)));
        w.Add(Primitives.Sphere(24, 16)).At(1.6f, 0.7f, 0).Scale(1.4f)
         .Material(Materials.Standard("orb", new Color(90, 150, 200)));

        File.Delete(TuningPath);
        Tunables.Clear();
        Tunables.Register(typeof(Config));

        Console.WriteLine($"[tune] registered {Tunables.All.Count} knobs: "
                        + string.Join(", ", Tunables.All.Select(x => x.Key)));
    },
    update: (w, dt) =>
    {
        t += dt;
        if (stage != 0 || t < 0.4) return;
        stage = 1;

        // ── registry rejects what it can't drive ──
        var keys = Tunables.All.Select(x => x.Key).ToHashSet();
        Check("registers 5 usable knobs", Tunables.All.Count == 5, $"{Tunables.All.Count} registered");
        Check("rejects const", !keys.Contains("Config.Gravity"), "Gravity is const");
        Check("rejects readonly", !keys.Contains("Config.Locked"), "Locked is readonly");
        Check("rejects unsupported type", !keys.Contains("Config.NotSupported"), "int[] has no widget");
        Check("ignores unmarked fields", !keys.Contains("Config.Untouched"), "no [Tunable]");

        // ── tunables round-trip ──
        Config.DayLength = 45.5f;
        Config.WalkSpeed = 13.25f;
        Config.CanSprint = false;
        Config.TeamColor = new Color(12, 200, 90);
        Tunables.Save(TuningPath);

        Config.DayLength = 0f;                    // clobber, then reload
        Config.WalkSpeed = 0f;
        Config.CanSprint = true;
        Config.TeamColor = Color.White;
        Tunables.Load(TuningPath);

        Check("tunables survive save+load",
              MathF.Abs(Config.DayLength - 45.5f) < 1e-4f
              && MathF.Abs(Config.WalkSpeed - 13.25f) < 1e-4f
              && !Config.CanSprint
              && Config.TeamColor.G == 200,
              $"DayLength={Config.DayLength} Walk={Config.WalkSpeed} Sprint={Config.CanSprint} "
              + $"Team={Config.TeamColor.R},{Config.TeamColor.G},{Config.TeamColor.B}");

        // A partial file must leave unlisted knobs at their code default.
        File.WriteAllText(TuningPath, "{ \"Config.WalkSpeed\": 3.5 }");
        Config.DayLength = 77f;
        Tunables.Load(TuningPath);
        Check("partial file leaves others alone",
              MathF.Abs(Config.WalkSpeed - 3.5f) < 1e-4f && MathF.Abs(Config.DayLength - 77f) < 1e-4f,
              $"Walk={Config.WalkSpeed} DayLength={Config.DayLength} (untouched)");

        // ── materials round-trip: retuning the FILE must retune the LIVE material ──
        MaterialLibrary.Save(MatsPath, new[] { "ground", "crate", "orb" });
        var before = MaterialManager.TryPeek("crate", out var crate) ? crate.Color : Color.White;

        // Edit the FILE, exactly as a person or an agent would.
        File.WriteAllText(MatsPath, File.ReadAllText(MatsPath).Replace(ToHex(before), "#28C85A"));
        MaterialLibrary.Load(MatsPath);

        MaterialManager.TryPeek("crate", out var after);
        Check("material edit retunes the LIVE material",
              after.Color.R == 0x28 && after.Color.G == 0xC8 && after.Color.B == 0x5A,
              $"{ToHex(before)} -> {ToHex(after.Color)} (same Material instance: "
              + $"{ReferenceEquals(crate, after)})");

        Console.WriteLine(new string('-', 70));
        bool all = true;
        foreach (var (name, pass, detail) in results)
        {
            Console.WriteLine($"  {(pass ? "ok  " : "FAIL")} {name,-36} {detail}");
            all &= pass;
        }
        Console.WriteLine(all ? "PHASE 4 PASS — materials and tunables round-trip without a recompile"
                              : "PHASE 4 FAIL");
        Console.WriteLine(new string('-', 70));
    }
);

void Check(string name, bool pass, string detail) => results.Add((name, pass, detail));

static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

// The knobs. Static, not const — a const is substituted at every call site during compilation, so
// there is no storage left to change at runtime.
static class Config
{
    [Tunable(0f, 240f, Group = "World")] public static float DayLength = 120f;
    [Tunable(-10f, 10f, Group = "World")] public static float WaterLevel = 0f;
    [Tunable(1f, 30f, Group = "Player")] public static float WalkSpeed = 8f;
    [Tunable(Group = "Player")] public static bool CanSprint = true;
    [Tunable(Group = "Player")] public static Color TeamColor = new(70, 96, 150);

    // Must be rejected with a clear message, not silently ignored.
    [Tunable] public const float Gravity = -22f;
    [Tunable] public static readonly float Locked = 1f;
    [Tunable] public static int[] NotSupported = new int[4];

    public static float Untouched = 999f;   // no attribute: must not appear
}
