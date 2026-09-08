#:project ../src/BdvEngine/BdvEngine.csproj
// Phase 1, second gate: editing the .scene.json on disk reloads the level live, and a BROKEN
// save keeps the last-good scene instead of blanking it.
//
//   dotnet run sketches/scene_hotreload.cs -- --shot /tmp/hotreload.png --frames 200
//
// Drives the real path — FileSystemWatcher, debounce, main-thread swap — by rewriting the file
// from the update loop and asserting what the live scene looks like afterwards.
using BdvEngine;
using System;
using System.IO;
using System.Numerics;

const string Path = "/tmp/bdv_hotreload.scene.json";

string SceneFile(float x, string hex) => $$"""
{
  "version": 1,
  "environment": { "sky": "#7089B5", "ambient": "#4C4C5B",
                   "sun": { "direction": {"x":-0.5,"y":-1,"z":-0.35}, "color": "#F2EDDB" } },
  "materials": [
    { "name": "ground", "shading": "Lit", "color": "#4E6450" },
    { "name": "box",    "shading": "Lit", "color": "{{hex}}" }
  ],
  "nodes": [
    { "name": "ground", "mesh": { "primitive": "plane", "size": 20 }, "material": "ground" },
    { "name": "box", "position": {"x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":0.5,"z":0},
      "mesh": { "primitive": "cube" }, "material": "box" }
  ]
}
""";

HotReloadableScene level = null!;
double t = 0;
int stage = 0;
bool survivedBadSave = false, sawReload = false, movedAndRecoloured = false;

File.WriteAllText(Path, SceneFile(-2f, "#D2483C"));

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 0.6f, 0), distance: 9, yaw: 0.6f, pitch: 0.45f));
        w.Add(new DirectionalLight(new Vector3(-0.5f, -1f, -0.35f)));
        w.AddPointLight(new Vector3(3, 4, 3), Color.White, intensity: 4, range: 18);

        level = new HotReloadableScene(w, Path);
        Console.WriteLine($"[test] initial load: box at x={BoxX()}");
    },
    update: (w, dt) =>
    {
        t += dt;
        level.Tick();
        if (level.ReloadedThisFrame) sawReload = true;

        // 0.6s — a malformed save (mid-keystroke). The level must survive it.
        if (stage == 0 && t > 0.6)
        {
            stage = 1;
            Console.WriteLine("[test] writing MALFORMED file...");
            File.WriteAllText(Path, "{ \"nodes\": [ { \"name\": broken,,, ");
        }
        // 1.4s — did it survive? Then write a real edit: move + recolour the box.
        else if (stage == 1 && t > 1.4)
        {
            stage = 2;
            survivedBadSave = BoxX() is > -2.01f and < -1.99f;
            Console.WriteLine($"[test] after malformed save: box at x={BoxX()} -> " +
                              (survivedBadSave ? "SURVIVED" : "LOST THE SCENE"));
            Console.WriteLine("[test] writing VALID edit (x=-2 -> 2.5, red -> blue)...");
            File.WriteAllText(Path, SceneFile(2.5f, "#3C78D2"));
        }
        // 2.2s — the edit should be live.
        else if (stage == 2 && t > 2.2)
        {
            stage = 3;
            float x = BoxX();
            bool blue = MaterialManager.TryPeek("box", out var m) && m.Color.B > m.Color.R;
            movedAndRecoloured = x is > 2.49f and < 2.51f && blue;
            Console.WriteLine($"[test] after valid edit: box at x={x}, blue={blue}");

            Console.WriteLine(new string('-', 66));
            bool pass = survivedBadSave && sawReload && movedAndRecoloured;
            Console.WriteLine(pass ? "HOT RELOAD PASS — bad save survived, good save applied live"
                                   : "HOT RELOAD FAIL — " +
                                     $"survivedBadSave={survivedBadSave} sawReload={sawReload} " +
                                     $"movedAndRecoloured={movedAndRecoloured}");
            Console.WriteLine(new string('-', 66));
        }
    }
);

float BoxX() => level.Root.GetObjectByName("box")?.Transform.Position.X ?? float.NaN;
