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

## 3D collision and the character controller

Colliders join the physics world automatically. Distinct from the 2D `ColliderComponent`, which is
unchanged.

```csharp
obj.AddComponent(new BoxCollider(Vector3.One));           // size is LOCAL: the transform scales it
obj.AddComponent(new SphereCollider(radius: 0.5f));
obj.AddComponent(new CapsuleCollider(0.35f, 1.8f, new Vector3(0, 0.9f, 0)));  // Y-aligned
terrainObj.AddComponent(new TerrainCollider(heightmapTerrain));
// every collider: .IsTrigger, .Layer, .Enabled, .Center
```

A capsule + `CharacterController` walks, slides, climbs and falls:

```csharp
var capsule = new CapsuleCollider(0.35f, 1.8f, new Vector3(0, 0.9f, 0));
player.AddComponent(capsule);
var cc = new CharacterController(capsule);
player.AddComponent(cc);

// each frame — feet sit at the object's origin:
cc.Move(new Vector3(inputX, 0, inputZ) * 5f, dt);
if (cc.IsGrounded && jump) cc.Jump(6f);
// cc.IsGrounded / .GroundNormal / .VerticalVelocity / .HitWall
// tune: Gravity, SlopeLimitDegrees, StepOffset, SkinWidth, CollisionMask
```

Queries:

```csharp
PhysicsWorld.Raycast(origin, dir, maxDist, out RayHit hit, layerMask, ignore);
PhysicsWorld.OverlapSphere(center, radius);
PhysicsWorld.OverlapCapsule(a, b, radius);
PhysicsWorld.GroundHeight(from, searchDown, out float y, out Vector3 normal);
PhysicsWorld.Clear();        // on a level swap, or the old level keeps colliding
```

**Collider size is LOCAL and multiplied by the object's scale**, exactly like Unity. A unit cube
scaled 40× wants `new BoxCollider(Vector3.One)`, not `Vector3(40,1,40)` — writing the size twice
gives you a collider 40× too big.

**Boxes are world-axis-aligned**: a collider follows position and scale but *ignores rotation*.
Fine for walls, crates and props; a tilted ramp needs a sphere/capsule approximation. Oriented
boxes aren't in v1.

**Detaching a `SimObject` does not unregister its colliders** — nothing calls `Unload` on removal.
Call `PhysicsWorld.Unregister(collider)`, or `Clear()` between levels.

Broadphase is a linear scan with AABB rejection, which is genuinely right for a few hundred
colliders. Swap in a spatial hash when a profile says so; the query API won't change.

---

## Transparency

A material is alpha-blended when its colour has `A < 255` — **inferred automatically at
construction**, so translucent things sort correctly without anyone remembering to say so.

```csharp
// Inferred: alpha 224 -> BlendMode.Alpha
Materials3D.Solid("water", new Color(34, 84, 132, 224));

// Transparency that lives in the TEXTURE (foliage cards, glass with an opaque tint) can't be
// inferred from the colour — say it explicitly:
MaterialManager.TryPeek("foliage", out var m);
m.Blend = BlendMode.Alpha;
m.CastShadows = false;
```

Alpha-blended geometry is drawn **after everything opaque, sorted far-to-near, with depth writes
off**. All three are needed: without the sort two panes composite differently depending on which
was added to the scene first; with depth writes on, the nearest transparent surface rejects
everything behind it — the bug where water hides the sea floor.

**Transparent materials don't cast shadows by default** (`CastShadows`). The depth pass has no
alpha testing, so a translucent surface would cast the solid silhouette of its geometry — water
throwing a hard black rectangle across the sea bed.

**Transparent geometry isn't instanced.** A batch draws in one call and therefore in one order,
which is the opposite of what sorting needs.

**Sorting is per object, by distance to its origin**, not per triangle. Right for water planes,
glass and foliage cards; two large interpenetrating transparent meshes will still show artefacts.

One trap worth knowing: modelling a "pane" as a thin **box** and setting `DoubleSided` draws its
front *and* back face, so every pane blends twice. Leave culling on for closed shapes — a box
always has a face pointing at you from either side.

---

## Sky and fog

Both **off by default** — turning the sky on replaces `Environment.Sky` as the background, and fog
is an art choice, not a fix. With them off, rendering is exactly what it was.

```csharp
var sky = w.Environment.SkyGradient;
sky.Enabled = true;
sky.Horizon = new Vector3(0.92f, 0.52f, 0.30f);   // colour at the horizon
sky.Zenith  = new Vector3(0.10f, 0.16f, 0.38f);   // straight up
sky.Ground  = new Vector3(0.14f, 0.12f, 0.13f);   // below the horizon
sky.SunGlow = 1.4f;                                // 0 disables the glow

var fog = w.Environment.Fog;
fog.Enabled = true;
fog.Density = 0.0075f;      // visibility ends around 2.5 / Density world units
fog.UseSkyColor = true;     // blend toward the sky in the view direction
fog.Color = new Vector3(0.6f, 0.7f, 0.85f);   // used when UseSkyColor is off, or the sky is
```

The sun glow tracks `Environment.Sun` automatically, so a day/night cycle that moves the sun moves
the glow with it — drive `Horizon`/`Zenith` from the same clock and you get a sunset.

**Fog blends toward the sky in the direction you're looking**, not toward a flat colour, so distant
geometry dissolves into the actual horizon instead of into a grey that only matches from one angle.
That needs the sky enabled; with it off, fog falls back to `fog.Color`.

`Density` is exponential-squared: doubling it roughly halves visibility. A linear ramp was rejected
because it has a visible edge where it starts, which reads as a wall of haze rather than distance.

**Fog does not apply to `Materials.Unlit`.** Unlit means "raw colour, no scene lighting" — it's for
debug helpers and UI-ish 3D, which shouldn't dissolve into the distance.

---

## Culling and instancing

Both on by default; both change cost, never the picture.

```csharp
w.Environment.Culling = true;     // skip what the camera (and the sun) can't see
w.Environment.Instancing = true;  // collapse repeated (mesh, material) into one draw call
```

**Instancing needs a SHARED mesh.** Batching is by `(Mesh, Material)`, so two objects only merge
if they hold the *same* `Mesh` instance. `Primitives.Cube()` and friends return a **shared** mesh
per spec for exactly this reason — a loop calling them gets one GPU buffer and one draw call, not
841 of each. `Mesh.Cube()` / `Mesh.Sphere()` / `Mesh.Plane()` still return a fresh, privately
owned mesh when you want one (and `Primitives.ClearShared()` drops the cache).

Prefabs and `.scene.json` share meshes automatically, so instanced content is the default there.

Measured on the Valheim island (97 trees, 90 rocks, terrain, water, player): **770 -> 16 draw
calls**. On a 202-pine prefab forest: **607 -> 8**.

**What doesn't instance**, and falls back to one draw each: skinned meshes (each needs its own
joint palette), materials with a `CustomShader` (their program has no per-instance attributes),
and batches under 4 copies (below that, filling the buffer costs more than it saves).

The camera and the sun cull with **separate frustums**. Culling the shadow pass with the camera's
would delete shadows cast by objects behind you — which is most of them with a low sun.

Skinned meshes are culled with padded bounds, because a mesh's stored bounds are its *bind* pose
and a raised arm reaches past them.

---

## Shadows

The sun casts shadows by default — static meshes, skinned characters, and prefab instances alike.
Nothing to opt into; anything that draws also casts.

```csharp
var sh = w.Environment.Shadows;
sh.Enabled  = true;     // off costs nothing: no depth pass, no extra samplers
sh.Distance = 45f;      // half-extent of the shadowed area around the camera's target
sh.Resolution = 2048;   // shadow map edge; 4096 for sharper edges over a big Distance
sh.Bias = 0.0016f;      // raise to kill acne (stripes), lower if shadows detach from feet
sh.Strength = 0.75f;    // 1 = pitch black in shadow, 0 = no shadow at all
sh.SoftnessTexels = 1.2f;
```

**`Distance` is the dial that matters.** It trades coverage for sharpness: 2048 over `Distance: 40`
gives ~2cm texels; stretching to 400 for a whole island makes them 20cm and edges go chunky.
Shadows simply stop beyond it. Size it to what the camera actually sees, not to the world.

**Only the sun casts.** Point lights don't — that needs cube maps and a much larger budget.

**Two symptoms and their fixes:** stripey self-shadowing (*acne*) means `Bias` is too low; a shadow
detached from its object's feet (*peter-panning*) means it's too high.

---

## Skinned animation (rigged `.glb`)

Model → rig → animate in Blender → export `.glb` → it loads, deforms and plays. The loader reads
`skins` (JOINTS_0 / WEIGHTS_0 / inverse bind matrices) and `animations`, builds the skeleton out of
ordinary scene nodes, and attaches an `Animator` to the model root.

```csharp
var hero = w.Load("assets/hero.glb").At(0, 0, 0);
var anim = hero.Object.GetComponent<Animator>()!;   // present when the .glb has clips

anim.Play("Idle");                    // cut
anim.CrossFade("Walk", 0.2f);         // blend over 0.2s — safe to call every frame
anim.Play("Attack", loop: false);     // one-shot; anim.Finished goes true at the end
anim.Speed = 1.5f;                    // negative plays backwards
anim.Seek(0.5f);                      // pose a frame (scrubbing, headless captures)
foreach (var name in anim.ClipNames) { }
```

Because joints are just `SimObject`s, everything else already works on them: parent a sword to a
hand node, read a bone's world matrix for an IK target, or move a joint by hand.

**Making a rig that works:**

- **glTF only, never FBX.** Y-up, metres, apply transforms before export, one `.glb` per asset,
  textures embedded.
- **Max 64 joints per skin** (`Skin.MaxJoints`, matched by the shaders' `MAX_JOINTS`). Over that
  the loader throws with a clear message rather than rendering garbage — split the mesh or raise
  both together.
- **Max 4 influences per vertex.** glTF's JOINTS_0/WEIGHTS_0 hold four; set your exporter's limit
  to 4 or the extra ones are dropped silently by the exporter, not by us.
- Weights need not sum to exactly 1 — the shader normalises. A vertex with *no* weights falls back
  to identity rather than collapsing to the origin.

**Not supported yet** (each fails loudly or degrades predictably, never silently): morph targets;
CUBICSPLINE interpolation (parsed, then sampled as LINEAR); animation of anything but node
translation/rotation/scale. There are no skeletal *shadows* either, because there are no shadows.

To generate a rigged test asset without Blender:
`python3 tools/make_test_rig.py sketches/assets/bendy.glb`

---

## Prefabs (`.prefab.json`) — compose once, instance many

A prefab is a **single node plus the materials it needs**, in the same schema as a scene node.
There is no new format and no new type. A scene node that carries `"prefab"` expands from that
file at load, and saves back as just the path plus its transform — so **editing the one file
changes every instance**.

```csharp
World.Instantiate("prefabs/pine.prefab.json").At(x, 0, z).Scale(1.2f);  // from code
World.SavePrefab("prefabs/pine.prefab.json", node);                      // node -> reusable asset
```

```jsonc
// prefabs/pine.prefab.json
{
  "version": 1,
  "materials": [ { "name": "bark", "shading": "Lit", "color": "#5C3F28" } ],
  "node": { "name": "pine", "children": [ { "name": "trunk", "mesh": {"primitive":"cube"}, ... } ] }
}
```

```jsonc
// ...instanced from a scene file
{ "name": "pine_7", "prefab": "prefabs/pine.prefab.json", "position": {"x":15,"y":0,"z":-3} }
```

**Keep the prefab root's transform neutral.** An instance's transform *replaces* the root's, so if
the root carries a scale (especially a non-uniform one), every instance that sets its own scale
throws that away and the children — authored against it — come out distorted. Put the geometry in
children and leave the root an unscaled container. This is why Unity prefab roots are empty
GameObjects, and it is the single easiest way to get a prefab wrong.

**Other rules:**

- An instance persists **only its name and transform**. Anything else you change on one is lost on
  the next save. To make an instance genuinely its own thing, `SimObject.Unpack()` (or the
  editor's **Unpack** button) severs the link so it serialises in full.
- A prefab carries **its own materials**, so a scene that instances it doesn't have to declare
  them.
- The file is read and cached once; every instance after that is built from cached JSON, and all
  instances of the same primitive share one GPU buffer. 200 pines cost one file read and 3 meshes.
- Prefabs can nest. A cycle (a prefab instancing itself, directly or transitively) is detected and
  reported rather than blowing the stack.
- `SceneSerializer.ClearPrefabCache()` forces a re-read after you edit a prefab file at runtime.

---

## Material palettes and tunables

Two ways to stop a recompile: a shared `materials.json`, and `[Tunable]` static fields.

```csharp
MaterialLibrary.Load("materials.json");                     // register or RETUNE in place
MaterialLibrary.Save("materials.json", new[]{ "bark", "leaf" });
var live = new HotReloadableMaterials("materials.json");    // + file watcher
live.Tick();                                                 // from Update
```

Scene files already carry the materials they use, which keeps them self-contained. A **library** is
for the other case: a palette shared by several scenes, where retuning `bark` should change it
everywhere. Same JSON shape, so a block moves between the two by copy-paste, and a bare
`[ ... ]` array works too.

Loading **updates in place** rather than replacing, so every mesh already holding a material picks
up the change — that's what makes hot reload work at all.

```csharp
static class Config
{
    [Tunable(0f, 240f, Group = "World")]  public static float DayLength = 120f;
    [Tunable(1f, 30f,  Group = "Player")] public static float WalkSpeed = 8f;
    [Tunable(Group = "Player")]           public static bool  CanSprint = true;
}

Tunables.RegisterAll(typeof(MyGame).Assembly);   // once, at startup
Tunables.Load("tuning.json");                    // apply saved values
// ...they now appear in the editor's Tunables panel, grouped, with sliders.
```

**The field must be `static`, and cannot be `const` or `readonly`.** A const is substituted into
every call site at compile time, so there is no storage left to change; the registry rejects it
with a message rather than appearing to work. `static` is the fix.

Per-object values don't belong here — put them on a component and the inspector picks them up for
free. `[Tunable]` is for the loose constants that aren't attached to anything: day length, sea
level, walk speed, spawn density.

A **partial `tuning.json` is fine** — missing keys keep their code defaults, so adding a new knob
doesn't require updating the file first.

---

## The in-game editor (F1)

Every game built on the engine hosts a scene editor — press **F1**. It is a mode, not a separate
app: click an object, drag a value, see it immediately, press Save. No compile in that loop.

```bash
dotnet run sketches/level.cs -- --editor      # open it at startup (F1 toggles either way)
```

- **Hierarchy** (left) — the scene tree. Icons: `+` loaded scene file, `#` mesh, `*` light,
  `=` billboard, `@` imported model, `.` empty node.
- **Inspector** (right) — Environment (sky/ambient/sun), then the selected node: name, Transform,
  and one collapsible section per component/behavior. **Widgets are generated by reflection over
  public fields** (`Inspector.DrawFields`), so *adding a public field to a behavior gives it a
  slider with no UI code*. `[Range(min,max)]` turns a drag into a slider;
  `[HideInInspector]` hides a field (it still saves).
- **Click any object** in the viewport to select it; drag the **red/green/blue gizmo** handles to
  move it along X/Y/Z. Camera orbit yields while a panel or handle has the mouse.
- **Save** writes to the path in the toolbar — the file you loaded, or a new one you type
  (which bakes the whole world). **Reload** re-reads from disk.
- **Duplicate / Delete / Add child** on the selected node. Duplicate is literally
  serialise-then-deserialise, so a copy contains exactly what a save would keep.

From code, for scripting or tests:

```csharp
SceneEditor.Active?.Select(node);     // jump the inspector to a node
SceneEditor.Active?.Save(world);      // what the Save button calls
SceneEditor.Active?.Duplicate(world);
```

**Caveats.** There is no undo — nothing is written until you press Save, so quitting without
saving is the undo. Deleting is likewise only permanent once saved. A field stored in a *private*
field can't be shown or edited; make it public if you want to tune it (that's also what makes it
serialise). Rotate/scale gizmos aren't in yet — use the Transform fields.

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
