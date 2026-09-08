#:project ../src/BdvEngine/BdvEngine.csproj
// Phase 2 acceptance gate: an edit made in the editor SURVIVES a save.
//
//   dotnet run sketches/editor_persist.cs -- --shot /tmp/editor_persist.png --frames 160 --editor
//
// Drives the exact code paths the editor's buttons call (SceneEditor.Save / .Duplicate) and the
// exact mutations its widgets perform (transform fields, material colour, a generated behavior
// field), then re-reads the file off disk and asserts. Automating an ImGui drag isn't practical;
// the persistence path is the part that can actually be wrong, and this covers it end to end.
using BdvEngine;
using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

const string Src = "sketches/levels/handwritten.scene.json";
const string Work = "/tmp/bdv_editor_persist.scene.json";

File.Copy(Src, Work, overwrite: true);

SceneEditor ed = null!;
SimObject level = null!;
double t = 0;
int stage = 0;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 1.2f, 0), distance: 15, yaw: 0.6f, pitch: 0.38f));
        level = w.LoadScene(Work);
        ed = SceneEditor.Active ?? throw new InvalidOperationException("editor disabled");
        ed.Select(level.GetObjectByName("idol"));
    },
    update: (w, dt) =>
    {
        t += dt;

        // 0.4s — make the edits an author would make through the inspector + gizmo.
        if (stage == 0 && t > 0.4)
        {
            stage = 1;
            var idol = ed.Selected!;
            idol.Transform.Position = new Vector3(3.25f, 2.5f, -1.5f);      // gizmo drag
            idol.Transform.Scale = new Vector3(1.6f, 1.6f, 1.6f);            // Scale field
            idol.GetBehavior<RotationBehavior>()!.Rotation = new Vector3(0, 2.4f, 0);  // generated widget
            MaterialManager.TryPeek("gold", out var gold);
            gold.Color = new Color(40, 220, 160);                            // material colour picker
            gold.Roughness = 0.8f;

            ed.Select(level.GetObjectByName("pine_b"));
            ed.Duplicate(w);                                                 // Duplicate button
            ed.Selected!.Transform.Position = new Vector3(-7f, 0, 3f);

            Console.WriteLine("[test] edits applied; saving...");
            if (!ed.Save(w)) Console.WriteLine("[test] SAVE RETURNED FALSE");
            ed.Select(level.GetObjectByName("idol"));
        }
        // 1.0s — read the file back off disk and check the edits are in it.
        else if (stage == 1 && t > 1.0)
        {
            stage = 2;
            Verify();
        }
    }
);

static void Verify()
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Work), new JsonDocumentOptions
    { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    var root = doc.RootElement;

    var idol = FindNode(root, "idol");
    var copy = FindNode(root, "pine_b_copy");
    var gold = FindMaterial(root, "gold");

    bool movedIdol  = idol is not null && Near(Vec(idol.Value, "position"), new Vector3(3.25f, 2.5f, -1.5f));
    bool scaledIdol = idol is not null && Near(Vec(idol.Value, "scale"), new Vector3(1.6f, 1.6f, 1.6f));
    bool spinSaved  = idol is not null
                      && idol.Value.TryGetProperty("behaviors", out var bs)
                      && Near(Vec(bs[0], "rotation"), new Vector3(0, 2.4f, 0));
    bool colourSaved = gold is not null
                       && gold.Value.GetProperty("color").GetString() == "#28DCA0"
                       && Math.Abs(gold.Value.GetProperty("roughness").GetSingle() - 0.8f) < 1e-4f;
    bool dupSaved = copy is not null
                    && Near(Vec(copy.Value, "position"), new Vector3(-7f, 0, 3f))
                    && copy.Value.TryGetProperty("children", out var ch) && ch.GetArrayLength() == 1;

    Console.WriteLine(new string('-', 68));
    Console.WriteLine($"  gizmo move persisted     : {movedIdol}");
    Console.WriteLine($"  scale field persisted    : {scaledIdol}");
    Console.WriteLine($"  behavior field persisted : {spinSaved}");
    Console.WriteLine($"  material edits persisted : {colourSaved}");
    Console.WriteLine($"  duplicate persisted      : {dupSaved} (with its child subtree)");
    bool pass = movedIdol && scaledIdol && spinSaved && colourSaved && dupSaved;
    Console.WriteLine(pass ? "EDITOR PERSISTENCE PASS — every edit survived the save"
                           : "EDITOR PERSISTENCE FAIL");
    Console.WriteLine(new string('-', 68));
}

static JsonElement? FindNode(JsonElement root, string name)
{
    foreach (var n in root.GetProperty("nodes").EnumerateArray())
    {
        var hit = Walk(n, name);
        if (hit is not null) return hit;
    }
    return null;

    static JsonElement? Walk(JsonElement n, string name)
    {
        if (n.TryGetProperty("name", out var nm) && nm.GetString() == name) return n;
        if (!n.TryGetProperty("children", out var ch)) return null;
        foreach (var c in ch.EnumerateArray())
        {
            var hit = Walk(c, name);
            if (hit is not null) return hit;
        }
        return null;
    }
}

static JsonElement? FindMaterial(JsonElement root, string name)
{
    foreach (var m in root.GetProperty("materials").EnumerateArray())
        if (m.GetProperty("name").GetString() == name) return m;
    return null;
}

static Vector3 Vec(JsonElement node, string key)
{
    if (!node.TryGetProperty(key, out var v)) return Vector3.Zero;
    return new Vector3(
        v.TryGetProperty("x", out var x) ? x.GetSingle() : 0,
        v.TryGetProperty("y", out var y) ? y.GetSingle() : 0,
        v.TryGetProperty("z", out var z) ? z.GetSingle() : 0);
}

static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
