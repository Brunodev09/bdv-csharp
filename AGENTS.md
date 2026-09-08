# BdvEngine — AI Prototyping Cheatsheet

Everything you need to prototype a 2D or 3D scene in **BdvEngine** (C# / .NET 10 / OpenGL). This
engine is *not* in your training data — this file **is** the API. Read it, then write a sketch.

## The loop (this is how you "see" your result)

A whole prototype is **one `.cs` file**. No project, no boilerplate.

```bash
# Interactive window (drag to orbit / arrow keys / wheel zoom):
dotnet run sketches/mything.cs

# Headless: render a frame to a PNG and exit — then LOOK at the PNG and iterate:
dotnet run sketches/mything.cs -- --shot out.png
#   -- --frames 60     capture at frame 60 (default 30; lets animation/settling happen)
#   -- --size 1600x900 window size
```

`--shot out.png` is your browser preview: write sketch → run with `--shot` → open the PNG → fix → repeat.

## Sketch skeleton (copy this)

```csharp
#:project ../src/BdvEngine/BdvEngine.csproj      // path from sketches/ to the engine project
using BdvEngine;
using System.Numerics;

Sketch.Run(
    setup: w =>
    {
        // build the scene ONCE: camera, lights, objects
    },
    update: (w, dt) =>
    {
        // per-frame logic (dt = seconds). optional.
    },
    draw: w =>
    {
        // immediate-mode 2D drawing (Draw.* / SpriteBatcher.*). optional; used for 2D sketches.
    }
);
```

`w` is a **World** (the whole scene). `Sketch.Run` also accepts `title:`, `width:`, `height:`.

---

## Camera

`w.Camera` is one camera; ortho vs perspective is a mode.

```csharp
// 3D:
w.Camera.Perspective(fovDegrees: 60);          // (optional near:, far:)
w.Camera.Position = new Vector3(4, 4, 7);
w.Camera.LookAt(Vector3.Zero);
w.Camera.AddControls(new OrbitControls(target: Vector3.Zero, distance: 8, yaw: 0.7f, pitch: 0.5f));

// 2D:
w.Camera.Orthographic();
w.Camera.X = 640; w.Camera.Y = 360; w.Camera.Zoom = 1f;   // pan + zoom

// Handy (vw/vh = viewport size; in a Sketch update use w-agnostic values or a full Game for
// ViewportWidth/Height — sketches most often just use OrbitControls and skip manual rays):
Ray r      = w.Camera.ScreenRay(px, py, vw, vh);                        // screen pixel → 3D pick ray
Vector2 s  = w.Camera.WorldToScreen(worldVec3, vw, vh, out bool front); // anchor 2D UI to a 3D point
Vector2 wp = w.Camera.ScreenToWorld(px, py, vw, vh);                    // 2D screen → world
```

## Building a 3D scene

```csharp
// Lights (Environment.Sun is light 0; add point/dir lights as scene nodes):
w.Add(new DirectionalLight(new Vector3(-0.5f, -1f, -0.35f)));      // the sun
w.AddPointLight(new Vector3(3, 4, 2), Color.White, intensity: 8, range: 14);
w.Environment.Sky = new Vector3(0.45f, 0.55f, 0.70f);              // background
w.Environment.Ambient = new Vector3(0.3f, 0.3f, 0.35f);

// Objects — fluent: Add(primitive) → place → material. Returns an ObjectHandle.
w.Add(Primitives.Cube()).At(0, 0.5f, 0).Material(Materials.Standard(Color.Orange));
w.Add(Primitives.Sphere()).At(2, 0.6f, 0).Scale(1.2f)
   .Material(Materials.Pbr(Color.Yellow, metallic: 1f, roughness: 0.25f));
w.Add(Primitives.Plane(20)).Material(Materials.Standard(Color.Green));   // ground

// Helpers:
w.Add(GridHelper.Create(size: 20, divisions: 20));   // ground grid
w.Add(AxesHelper.Create(length: 2));                 // X=red Y=green Z=blue

// Load a real model (.glb):
w.Load("assets/hero.glb").At(0, 0, 0);

// Camera-facing sprite anchored at a 3D point (health bars, markers):
w.AddBillboard(new Vector3(0, 2, 0), Color.Red, width: 0.6f, height: 0.1f);

// Click-picking:
SimObject? hit = w.Pick(w.Camera.ScreenRay(px, py, vw, vh));
```

**Primitives:** `Primitives.Cube()`, `Primitives.Sphere(segments, rings)`, `Primitives.Plane(size)`.
**Materials:** `Materials.Standard(color)` (lit), `Materials.Unlit(color)` (flat), `Materials.Pbr(color, metallic, roughness)`.

**ObjectHandle** (returned by `Add`): `.At(x,y,z)` / `.At(Vector3)` / `.Scale(f)` / `.Scale(x,y,z)` /
`.RotateEuler(x,y,z)` / `.Material(name)` / `.Add(behavior)` / `.Object` (the underlying `SimObject`).

Animate by keeping the handle and editing its transform in `update`:
```csharp
ObjectHandle cube = null!;
Sketch.Run(w => cube = w.Add(Primitives.Cube()).Material(Materials.Standard(Color.Red)),
           (w, dt) => cube.Object.Transform.Rotation += new Vector3(0, (float)dt, 0));
```

## 2D drawing (in the `draw:` lambda, ortho camera)

```csharp
Draw.Rect(x, y, w, h, Color.Blue);           // filled rect (world coords under the camera)
Draw.RectOutline(x, y, w, h, color);
Draw.Circle(cx, cy, radius, color);
Draw.Line(x1, y1, x2, y2, color);
SpriteBatcher.DrawSolid(x, y, w, h, color, SpriteLayer.Ground);   // batched quad
```

## Input (in `update:`)

```csharp
using Silk.NET.Input;                          // for Key
InputManager.IsKeyDown(Key.W)                  // held
InputManager.GetMousePosition()                // Vector2 (screen px)
InputManager.IsLeftDown                        // mouse button
InputManager.ConsumeWheelDelta()               // float
```

## Colors

`Color.White/Black/Red/Green/Blue/Yellow/Orange/Cyan/Magenta/Purple/Gray`, `new Color(r,g,b)` /
`new Color(r,g,b,a)` (0–255 bytes), `Color.FromFloats(r,g,b)` (0–1).

---

## Full example — 3D

```csharp
#:project ../src/BdvEngine/BdvEngine.csproj
using BdvEngine; using System.Numerics;
ObjectHandle cube = null!;
Sketch.Run(
    w => {
        w.Camera.Perspective(55);
        w.Camera.AddControls(new OrbitControls(new Vector3(0, .6f, 0), distance: 7));
        w.Add(new DirectionalLight(new Vector3(-.5f, -1, -.35f)));
        w.AddPointLight(new Vector3(3, 3, 2), Color.White, 6, 14);
        w.Add(GridHelper.Create(20, 20));
        w.Add(Primitives.Plane(20)).Material(Materials.Standard(Color.Green));
        w.Add(Primitives.Sphere()).At(-1.6f, .6f, 0).Material(Materials.Pbr(Color.Yellow, 1f, .25f));
        cube = w.Add(Primitives.Cube()).At(1.2f, .5f, 0).Material(Materials.Standard(Color.Orange));
    },
    (w, dt) => cube.Object.Transform.Rotation += new Vector3(0, (float)dt, 0)
);
```

## Full example — 2D

```csharp
#:project ../src/BdvEngine/BdvEngine.csproj
using BdvEngine; using System; using System.Numerics;
double t = 0;
Sketch.Run(
    setup:  w => { w.Camera.Orthographic(); w.Camera.X = 640; w.Camera.Y = 360; },
    update: (w, dt) => t += dt,
    draw:   w => {
        for (int i = 0; i < 9; i++) {
            float h = 130 + 95 * MathF.Sin((float)t * 1.5f + i);
            Draw.Rect(190 + i * 105, 520 - h, 70, h, Color.Cyan);
        }
    }
);
```

---

## Scenes as data (`.scene.json`)

A level can live in a **file** instead of in C#, so it can be tuned without recompiling and edited
directly by you. Procedural content (terrain, scatter) stays in code; what goes in the file is the
**placed, named, tuned** content.

```csharp
w.LoadScene("levels/forest.scene.json");     // in setup / Init — returns the container SimObject
w.SaveScene("levels/forest.scene.json");     // bake the whole world out to a file
w.SaveScene(path, container);                 // save just one loaded level back

var level = new HotReloadableScene(w, path); // + file watcher: edits to the file reload it live
level.Tick();                                 // call from update; level.Root is the container
level.Save();                                 // write the live scene back
```

The format — every field optional, sensible defaults:

```jsonc
{
  "version": 1,
  "environment": { "sky": "#7089B5", "ambient": "#4C4C5B",
                   "sun": { "direction": {"x":-0.5,"y":-1,"z":-0.35}, "color": "#F2EDDB" } },
  "materials": [
    { "name": "bark", "shading": "Lit", "color": "#4A3524" },          // Lit | Unlit | Pbr
    { "name": "gold", "shading": "Pbr", "color": "#DCBE5A", "metallic": 1, "roughness": 0.25 },
    { "name": "leaf", "shading": "Lit", "color": "#2F5A32", "texture": "assets/leaf.png",
      "doubleSided": true }
  ],
  "nodes": [
    { "name": "ground", "mesh": { "primitive": "plane", "size": 20 }, "material": "bark" },

    { "name": "pine", "position": {"x":12,"y":0,"z":-8}, "scale": {"x":1,"y":1.4,"z":1},
      "mesh": { "primitive": "cube" }, "material": "bark",
      "behaviors": [ { "type": "rotation", "name": "spin", "rotation": {"x":0,"y":0.8,"z":0} } ],
      "children": [
        { "name": "canopy", "position": {"x":0,"y":3,"z":0},
          "mesh": { "primitive": "sphere", "segments": 16, "rings": 12 }, "material": "leaf" }
      ] },

    { "name": "hero", "model": "assets/hero.glb", "position": {"x":0,"y":2,"z":0} },

    { "name": "lamp", "position": {"x":3,"y":4,"z":2},
      "light": { "type": "Point", "color": "#FFFFFF", "intensity": 8, "range": 14 } },

    { "name": "hp", "position": {"x":0,"y":2.2,"z":0},
      "billboard": { "material": "leaf", "width": 0.9, "height": 0.14 } }
  ]
}
```

**Rules that matter when you write one by hand:**

- **Vectors** are `{"x":..,"y":..,"z":..}` (arrays `[x,y,z]` are also accepted on read).
  **Colours** are `"#RRGGBB"` / `"#RRGGBBAA"`. Rotation is Euler radians; use `"quaternion"`
  (`{x,y,z,w}`) instead for gimbal-free orientation.
- **Every material a node references must be declared** in the `materials` block, or the node
  loads without its mesh (and says so).
- `"mesh"` primitives: `cube`, `sphere` (`segments`, `rings`), `plane` (`size`). Use `"model"`
  for a `.glb` instead — its children are re-imported on load, not stored in the file.
- `components` / `behaviors` go through the same builder registry as everything else, so `"type"`
  is the registered name (`collider`, `sprite`, `rotation`, `rigidBody`, `keyboardMovement`, ...).
- **Comments and trailing commas are allowed.** Unknown keys are ignored.
- A malformed file **keeps the last-good scene** and prints the error — it never blanks the level.

**What can't round-trip** (both are reported loudly, never silently wrong): a mesh assembled by
hand from vertices rather than via `Mesh.Cube/Sphere/Plane` (e.g. `HeightmapTerrain`), and a
texture generated at runtime rather than loaded from a file. Both are procedural — keep them in
code and let the file hold what you place around them.

---

## Notes / gotchas

- **Working directory:** if your sketch loads files (`w.Load("x.glb")`, textures), paths are relative
  to where you run `dotnet`. Run from the folder that contains those assets.
- **2D needs `w.Camera.Orthographic()`** in setup, or the immediate `draw` renders through a
  perspective projection (garbage). 3D uses `Perspective()` (the default).
- **PBR metals look muted** without an environment map (no IBL yet) — that's expected; use point
  lights near metallic objects, or `Materials.Standard` for punchier flat-lit color.
- **One camera, one World** — `ortho` vs `perspective` is just `w.Camera.Mode`, not two systems.
- For a **full game** (not a sketch): subclass `BdvEngine.Game`, override `Init` / `Update` /
  `Render(Shader)` (2D immediate) / `OnHud` (screen UI), boot with `new Engine(new MyGame(), cfg).Run()`.
  You get `World`, `Camera`, `ViewportWidth/Height`. `Sketch.Run` is just a thin wrapper over that.
