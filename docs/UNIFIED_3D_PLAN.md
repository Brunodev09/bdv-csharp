# Unified 2D/3D Engine Plan

**Status:** Draft for refinement — no engine code changed yet.
**Goal:** Make BdvEngine treat 2D and 3D as one world (like Three.js / Unity / Godot): one
scene, one node type, one renderer, one camera abstraction, and a friendly authoring API —
so building a 3D game is as easy as `Scene.Add(Primitives.Cube()).At(0, 1, 0)`.

---

## 1. TL;DR

The hard part is already built. BdvEngine has a **dimension-agnostic scene graph** (`Scene →
SimObject → Transform`, with a parent/child world-matrix hierarchy and component composition).
A mesh and a sprite are *already* just components on a `SimObject`.

What makes 3D feel impossible today is that **the engine is forked in two at the boot layer**
(`Engine`/`Game`/`Camera2D`/`DefaultShader` vs `Engine3D`/`Game3D`/`Camera3D`/`LitShader`), and
neither half owns or drives the scene — the *game* does, by hand. Fixing this is **"collapse
the fork and fill in the missing ~30%,"** not "write a 3D engine."

This document is the plan to do that, in phases, each independently shippable, with **zero
breakage** to existing games (ColonySim, HexStrategy, TerrainGame) via backward-compat façades.

---

## 2. The problem: why 3D is hard today

You pick one of two disjoint stacks at boot and are locked in for the whole game:

| | 2D path | 3D path |
|---|---|---|
| Engine | `Engine` (`Engine.cs`) | `Engine3D` (`Engine3D.cs`) |
| Base class | `Game` (`Game.cs`) | `Game3D` (`Engine3D.cs:11`) |
| Camera | `Camera2D` — ortho, `X/Y/Zoom` | `Camera3D` — perspective, unrelated class |
| Shader handed to game | `DefaultShader` | `LitShader` (one shader for *everything*) |
| Render | you override `Render(Shader)` and drive it yourself | same |

`Game` and `Game3D` are unrelated abstract classes that happen to share the same three overrides
(`Init` / `Update(double)` / `Render(Shader)`) with **incompatible contracts** — the `Shader`
handed in is a 2D sprite shader under `Engine` (`Engine.cs:148`) and a lit 3D shader under
`Engine3D` (`Engine3D.cs:142`). There is no way for 3D meshes and 2D sprites/HUD to coexist in
one world except the ImGui overlay both engines composite on top.

That is exactly the fork Three.js/Unity/Godot **don't** have.

### Concrete friction, from our own examples

Evidence from `Examples/My3DGame/My3DGameApp.cs` and `Examples/Valheim/ValheimGame.cs`:

- **The game owns the scene and manually pumps it.** `Engine3D.OnRender` only sets camera/light
  uniforms then calls `_game.Render(shader)` (`Engine3D.cs:134-142`); it never touches a scene.
  Every 3D game instantiates its own `Scene` and calls `_scene.Load()` / `_scene.Update(dt)` /
  `_scene.Render(shader)` by hand.
- **This is actively buggy.** World matrices only rebake inside `SimObject.Update`
  (`SimObject.cs:83-84`), so mutating a `Transform` *after* `_scene.Update` renders one frame
  stale. **My3DGame has this bug** (rotates objects after `_scene.Update`); Valheim happens to
  get the ordering right. Nothing enforces it.
- **No model loading of any kind.** Grepped the engine for gltf/glb/obj/fbx/assimp/LoadModel —
  nothing. Valheim's trees, rocks, and *player* are stacked `Mesh.Cube()`/`Mesh.Sphere()`
  (`ValheimGame.cs:176-247`), each a 3–4-object pile of `new SimObject(_nextId++, "name")` +
  `AddComponent` + `AddChild` ceremony.
- **The camera is bespoke trig in game code.** `Camera3D` is a plain `Position/Target/Up/FOV`
  holder (`Camera3D.cs`), no controller. Valheim hand-writes ~35 lines of spherical math + a
  raymarch to keep the camera out of terrain (`ValheimGame.cs:363-398`) and manually tunes
  `Near=0.3f/Far=480f` to dodge z-fighting (`ValheimGame.cs:59-65`).
- **Minimal material system.** One diffuse texture + tint `Color` + one hard-coded Phong
  `LitShader`. Flat colors need a hack — `Materials3D.Solid` binds a shared 1×1 white texture
  because `LitShader` always multiplies the texel by `u_color` (`Materials3D.cs`). No PBR, no
  per-material shader selection on the 3D path.
- **Lighting is engine-global singletons.** One directional light + one ambient term as scalar
  properties on `Engine3D` (`Engine3D.cs:34-39`). No light entities; no point/spot lights on the
  3D lit path. The `-LightDirection` sign convention is a documented footgun.
- **No frustum culling on the 3D scene.** `SimObject.Render` recurses and draws everything
  (`SimObject.cs:91-96`). (TerrainGame hand-rolled AABB culling on the *2D* side — that machinery
  doesn't exist for 3D.)
- **Euler-only rotations.** `Transform.Rotation` is Euler-XYZ radians (`Transform.cs:18-26`),
  gimbal-prone; Valheim resorts to `Atan2` into `Rotation.Y` to face travel direction.
- **Leaky abstraction to raw GL.** Valheim reaches into `Gfx.Gl.Disable(CullFace)` directly
  (`ValheimGame.cs:72`) for a rendering concern the engine doesn't expose.
- **Surprising `MaterialManager.Get` semantics.** It ref-count *increments on every lookup*
  (`Material.cs:152-158`), which is odd for a "look up my material" call.

> Note: **TerrainGame is not a 3D example** — it's 2.5D on the 2D path (`Camera2D`, tilemaps,
> `AnimatedSpriteComponent`). The only true-3D examples are My3DGame and Valheim.

---

## 3. What already exists (the ~60% that's done)

- **A dimension-agnostic scene graph.** `Scene` → root `SimObject` → children
  (`World/Scene.cs`, `World/SimObject.cs`). `Transform` is **Vector3** position/rotation/scale
  (`Utils/Transform.cs`); world matrices propagate as
  `world = local × parent.world` (`SimObject.cs:83-84`). This *is* Three.js's `Object3D` /
  Unity's `GameObject`.
- **Meshes and sprites are already "just components."** `MeshComponent` (`3d/MeshComponent.cs`)
  and `SpriteComponent` (`Components/SpriteComponent.cs`) both render via `_owner.WorldMatrix`.
  The unification seam is already in the codebase.
- **Component + behavior composition** (`Components/Component.cs`, `Behaviors/`) — including a
  `RayCastBehavior` we can repurpose for 3D picking.
- **Primitives:** `Mesh.Cube()` / `Mesh.Sphere(seg, rings)` / `Mesh.Plane(size)` (`3d/Mesh.cs`).
- **A lit shader** (Blinn-Phong diffuse+specular, `3d/LitShader.cs`).
- **A material system with real bones:** uniform bag with boxing-avoidance, `CustomShader` hook,
  diffuse + normal-map textures, `ReceivesLighting` flag (`Graphics/Material.cs`).
- **A heightmap terrain helper** (`3d/HeightmapTerrain.cs`).
- **A GC-tuned 2D sprite batcher** (`Graphics/SpriteBatcher.cs`) — must be preserved (see §7).

---

## 4. Design principles (the Three.js/Unity model)

1. **One scene, one node type.** Cameras, lights, meshes, and sprites are all objects in the same
   scene graph. Adding one = it renders.
2. **The engine owns and drives the scene.** The engine calls `Load`/`Update`/`Render` on the
   scene — the game never pumps it manually (kills the frame-lag bug and the boilerplate).
3. **Camera is a property, not a class.** Orthographic vs perspective is a mode on one `Camera`.
   A camera is a node in the scene.
4. **The renderer sorts and dispatches by material.** No single hard-coded shader; the material
   picks its shader and render state. Renderer batches 2D quads, draws 3D meshes, depth-sorts,
   and frustum-culls.
5. **Lights are scene nodes.** `DirectionalLight`/`PointLight` are objects; the renderer collects
   them per frame.
6. **A fluent authoring facade over the machinery.** `Scene.Add(...).At(...).Material(...)`,
   `Primitives.*`, `Materials.*` — this is the "Three.js syntax" ergonomics layer.
7. **No permanent compat layer (Q2, decided).** We adjust games around the engine, not vice-versa.
   The old `Engine`/`Game`/`Engine3D`/`Game3D`/`Camera2D`/`Camera3D` are **transitional scaffolding**
   — kept building only while games migrate onto the unified engine, then **deleted**. Final state:
   one engine, no shims. To keep the build green we build the unified engine *alongside* the old
   ones and migrate game-by-game, deleting each old class once nothing references it.

---

## 5. Target authoring API

### Before (today, `My3DGameApp.cs` shape)

```csharp
public sealed class My3DGame : Game3D
{
    private readonly Scene _scene = new();
    private int _nextId = 1;

    public override void Init()
    {
        MaterialManager.Register(new Material("crate", "textures/block.png", Color.White));
        var cube = new SimObject(_nextId++, "cube");
        cube.Transform.Position = new Vector3(0, 0.5f, 0);
        cube.AddComponent(new MeshComponent(Mesh.Cube(), "crate"));
        _scene.AddObject(cube);
        _scene.Load();                                   // must remember; needs GL context
    }

    public override void Update(double dt)
    {
        _scene.Update(dt);                               // must remember
        // ...mutating transforms here renders one frame stale...
        Camera.Position = new Vector3(MathF.Cos(a)*6, 3, MathF.Sin(a)*6);  // hand-rolled orbit
        Camera.Target   = new Vector3(0, 0.5f, 0);
    }

    public override void Render(Shader shader) => _scene.Render(shader);  // must remember
}
```

### After (target)

```csharp
public sealed class My3DGame : Game            // ONE base class; the camera decides 2D vs 3D
{
    public override void Init()
    {
        Camera.Perspective(fov: 60);                        // Camera == World.Camera shorthand
        Camera.Position = new(0, 4, 8);                     //   (or .Orthographic() → 2D)
        Camera.LookAt(Vector3.Zero);
        Camera.AddControls(new OrbitControls());            // mouse orbit/zoom, terrain-aware

        World.Add(new DirectionalLight(dir: new(-.5f, -1, -.3f), color: Color.Warm));

        World.Add(Primitives.Plane(50)).Material(Materials.Standard(Color.Grass));
        World.Add(Primitives.Cube()).At(0, .5f, 0).Material(Materials.Standard(tex: "crate"))
             .Add(new SpinBehavior(speed: 1f));             // composition, unchanged

        World.Load("assets/models/knight.glb").At(2, 0, 0); // glTF model

        World.Add(Sprite2D.HealthBar(hero)).Billboard();    // 2D sprite in the SAME 3D scene
    }
    // No Render() override. No manual _scene.Update. The engine owns the loop.
}
```

The payoff of "no distinction": the last two lines put a glТF model, a 3D sprite billboard, and
(via the existing GUI system) a screen-space HUD into one coherent world.

---

## 6. The phased plan

Each phase is independently shippable and leaves the tree building + all existing games running.

### Critical path — proves the thesis ("add object → it renders in 3D")

#### Phase 0 — One engine
- **Goal:** A single unified engine that subsumes both `Engine` + `Engine3D` (they are ~90%
  copy-pasted — FPS counter, ImGui, screenshot, resize, clear all duplicated).
- **Changes:** New unified engine with a boot config that toggles depth-test/cull and the default
  clear. **Built alongside** the old engines (temporary namespace during transition) so the build
  stays green; the old `Engine`/`Engine3D` are deleted later as games migrate off them (Q2).
- **Slice approach:** the first demo is a **new** example on the unified engine — existing games
  are untouched until their own migration step. My3DGame/Valheim migrate first; the 2D games
  (incl. ColonySim) later.
- **Acceptance:** the new demo runs on the unified engine; all existing games still build + run.

#### Phase 0.5 — Quaternions in Transform
- **Goal:** Add a quaternion rotation path to `Transform` (keep Euler setters for compat).
- **Why:** Euler-XYZ is gimbal-prone; Valheim resorts to `Atan2` into `Rotation.Y` to face
  movement. `Transform.LookAt` / `RotateTowards` become trivial with quaternions.
- **Acceptance:** Existing Euler-driven objects render identically; a quaternion `LookAt` demo
  faces a moving target smoothly.

#### Phase 1 — One camera
- **Goal:** Fold `Camera2D` + `Camera3D` into one `Camera` with a `Projection` mode
  (`Orthographic | Perspective`), `LookAt`, and sane near/far defaults per mode (fixes the
  Valheim z-fighting hand-tuning).
- **Changes:** Camera becomes a node the engine reads from the scene. `Camera2D`/`Camera3D`
  survive as compat shims mapping onto the unified camera.
- **Acceptance:** A 2D game (ortho) and a 3D game (perspective) both drive the same `Camera` type.

#### Phase 2 — Scene-driven renderer
- **Goal:** The engine owns a `Scene`, and walks + renders it each frame. No `Render(Shader)`
  override; no manual `_scene.Update`.
- **Changes:**
  - Engine calls `Scene.Load/Update/Render`; **rebake world matrices right before draw** (kills
    the frame-lag bug).
  - **Two internal lanes, one API:** keep the batched `SpriteBatcher` path for sprite quads;
    add a per-mesh 3D draw path; depth-sort; **frustum-cull** as the tree is walked.
  - Render-state (depth test, cull face, blend) driven per-material, not globally (retires the
    `Gfx.Gl.Disable(CullFace)` leak).
- **Acceptance:** A scene with a lit cube renders with **no** game-side render code; a 10k-object
  scene culls correctly; ColonySim's sprite throughput is unchanged (perf-checked, see §7).

#### Phase 3 — The authoring facade
- **Goal:** The "Three.js syntax" layer. `Scene.Add(mesh)` / `.At(x,y,z)` / `.Material(m)` /
  `.Add(behavior)`, `Primitives.*`, `Materials.Standard(...)`, **auto-assigned object IDs**
  (kill `_nextId++`).
- **Acceptance:** My3DGame rewritten in the new API is < 1/3 the lines and has no manual
  scene-pumping or ID bookkeeping.

> **Milestone after Phase 3:** a single unified engine boots a scene rendering a **lit 3D cube
> and a 2D sprite in the same frame** under `Scene.Add(...)`. This is the recommended first
> vertical slice (§8).

### Power — makes 3D games actually buildable

#### Phase 4 — Real material/shader system
- **Goal:** `StandardMaterial` → shader auto-selected (unlit / lit / PBR-lite). Material carries
  render state (`DoubleSided`, depth flags). Fix `MaterialManager.Get` ref-count semantics.
- **Changes:** Renderer dispatches material→shader instead of one hard-coded `LitShader`. Flat
  colors work without the white-texture hack.
- **Acceptance:** Objects with different materials/shaders render in one scene; a double-sided
  material needs no raw-GL calls.

#### Phase 5 — Model loading (highest single-feature leverage)
- **Goal:** Load `.glb` (binary glTF) into `SimObject` sub-trees + meshes + materials.
- **Decision (Q1): hand-rolled minimal `.glb` loader, zero deps.** glTF is cleanly specced; a
  focused `.glb` reader is a few hundred lines and keeps the engine dependency-free and
  AOT/iOS-safe by construction. **v1 scope:** the binary `.glb` container (JSON chunk + BIN
  chunk), the node hierarchy → `SimObject` tree, indexed mesh primitives
  (POSITION/NORMAL/TEXCOORD_0), and `pbrMetallicRoughness` base-color + textures. **Out of v1**
  (add later behind the same call): skins, skeletal/morph animation, sparse accessors, KHR
  extensions. Pairs with Phase 4's material system + Phase 0.5's mesh format (may need to extend
  `Mesh` beyond the fixed `pos3+normal3+uv2` interleave for tangents/vertex colors later).
- **Acceptance:** `World.Load("knight.glb")` shows a real textured model; Valheim can drop a
  loaded tree model in place of the cube-and-sphere stack.

#### Phase 6 — Lights as scene nodes
- **Goal:** `DirectionalLight` / `PointLight` as `SimObject`s; renderer collects the N nearest
  per draw. Retire the `Engine3D` global-light singletons (kept as a compat shim).
- **Acceptance:** Two point lights + one directional light illuminate a scene; Valheim's
  day/night cycle drives a light node instead of engine globals.

### Niceties — what makes a 3D engine feel *interactive* to build in

#### Phase 7 — Controls + picking + helpers
- **Goal:** `OrbitControls` / `FlyControls` / third-person rig; mouse-ray picking (wire up the
  existing `RayCastBehavior`); `GridHelper` / `AxesHelper`.
- **Acceptance:** Camera controls come from the engine, not game trig; clicking selects an object.

#### Phase 8 — Full 2D/3D interop
- **Goal:** Billboarded sprites, world-space labels via `TextRenderer`, screen-space GUI over 3D.
  The literal "no distinction between 2D and 3D."
- **Acceptance:** A 3D scene shows world-anchored 2D health bars + a screen-space HUD from the
  existing GUI system, all in one frame.

---

## 7. Cross-cutting concerns & risks

- **2D performance is a hard constraint, not an afterthought.** `SpriteBatcher` is GC-tuned
  (batch keys, boxed-uniform avoidance) because ColonySim throws tens of thousands of quads/frame.
  The unified renderer **must keep the batched sprite lane** — routing sprites through per-mesh
  draws would wreck it. Phase 2 keeps two internal lanes under one API. Perf-gate every renderer
  change against a ColonySim zoomed-out frame.
- **No permanent compat layer (Q2).** We migrate games onto the unified engine and **delete** the
  old `Engine`/`Game`/`Engine3D`/`Game3D`/`Camera2D`/`Camera3D` rather than keeping façades. The
  only compat concern is *sequencing*: build the new engine alongside the old, migrate game-by-game
  (My3DGame/Valheim first, ColonySim/HexStrategy/TerrainGame/WorldSim after), and delete each old
  class once unreferenced — so the tree builds at every step but the end state carries no shims.
- **iOS / web-port synergy.** A material→shader-abstracted renderer + a glb loader is exactly the
  layer the iOS/WASM port needs. Hand-rolled glb (Q1) is AOT/iOS-safe by construction. Keep shader
  sources isolated so a GLSL-ES / Metal backend can swap them later.
- **Candide 2.5D synergy.** The existing normal-mapped sprite lighting becomes just another
  `StandardMaterial` variant under the unified material system.
- **Frustum culling correctness.** Objects need bounds (mesh AABB × world matrix). Cheap and
  essential before Valheim-scale worlds.
- **Scope discipline.** PBR-lite, not full PBR/IBL, in Phase 4. Shadows, post-process stack
  reuse (`Graphics/PostFx`), and instancing are explicitly out of scope for this plan (follow-up).

---

## 8. Recommended sequencing

Do **not** land this as one giant PR. Build **Phases 0 → 3 as one vertical slice** first: a
unified engine that boots a `Scene`, one perspective camera, walks the `SimObject` tree, and
renders a **lit 3D cube and a 2D sprite in the same frame** with the fluent `Scene.Add(...)` API.
That one demo proves the entire thesis (no 2D/3D fork); everything after (models, PBR, lights,
controls) is additive.

Then, by value: **Phase 5 (models)** → **Phase 4 (materials)** → **Phase 6 (lights)** →
**Phase 7 (controls/picking)** → **Phase 8 (interop)**.

---

## 9. Open questions (for refinement before coding)

1. ~~glTF dependency~~ — **RESOLVED:** hand-rolled minimal `.glb` loader, zero deps (see Phase 5).
2. ~~Backward-compat surface~~ — **RESOLVED:** no permanent shims; migrate games onto the unified
   engine and delete the old classes (build alongside, migrate game-by-game). See §4.7 / §7.
3. ~~Facade home~~ — **RESOLVED:** Option B, a `World` facade over a pure `Scene` (see §10).
4. **PBR scope:** is Blinn-Phong "lit" + a metallic/roughness "PBR-lite" enough for now, or is
   full PBR wanted up front?
5. **First slice acceptance:** is "lit cube + 2D sprite in one frame under `World.Add`" the demo
   you want to sign off Phase 0–3, or a different proof?

---

## 10. Q3 expanded — where the authoring API lives

### The problem it solves

Today the author's "world" is fragmented across **three** owners:

- **objects** live in the game's own `Scene` — `_scene.AddObject(...)`
- **the camera** lives on the `Game` base class — `Camera`
- **lighting / sky** live on the `Engine3D` host — `Host.LightDirection`, `Host.SkyColor`

So building a scene means touching three unrelated objects, and "the world" exists nowhere as a
single thing. Q3 asks whether the new fluent API should **(A)** just grow on `Scene` and leave
that fragmentation, or **(B)** introduce one type that unifies it.

### Option A — fluent API on `Scene`

`Scene` grows `Add/Load/Pick`; camera stays on `Game`, lights stay on the engine host.

```csharp
Scene.Add(Primitives.Cube()).At(0, 1, 0);   // objects here
Camera.Perspective(60);                       // camera on the base class (elsewhere)
Host.LightDirection = ...;                     // lighting on the engine (elsewhere again)
```

- **Pro:** fewest new types; smallest diff.
- **Con:** the fragmentation stays — the exact split-brain we set out to remove. `Scene` also
  becomes a grab-bag (a node container that also knows about cameras/lights/sky), muddying the
  clean graph that the streaming + serialization systems already treat as pure data.

### Option B — a `World` facade (recommended)

`Scene` stays the pure node-graph. A new `World` composes Scene + Camera + lights + environment
+ picking, and is the *one* object the author touches. `Game` exposes `protected World World`.

```csharp
World.Camera.Perspective(60);
World.Environment.Sky = Color.Dusk;                   // sky / ambient / fog in one place
World.Add(new DirectionalLight(dir: new(-.5f,-1,-.3f)));
World.Add(Primitives.Cube()).At(0, 1, 0).Material(Materials.Standard(Color.Crate));
var hit = World.Pick(World.Camera.ScreenRay(mouse));  // raycast lives here too
World.Load("assets/models/knight.glb").At(2, 0, 0);
```

- **Pro:** one entry point; kills the three-owner fragmentation. `Scene` keeps single
  responsibility (a testable, serializable graph — important, since streaming/save code already
  treats `SimObject` trees as data). Camera controls, environment, lighting, and picking all get
  an obvious home. Sets up **level management for free** — a `World` is a loadable level; swap
  `World`s to change level.
- **Con:** one new concept; must define the boundary crisply — **`Scene` = data (the node graph);
  `World` = the authoring + systems facade that owns a Scene.**

### Why not a literal Three.js `Scene`/`Renderer` split

Three.js makes *you* write the frame loop (`renderer.render(scene, camera)` inside your own
`requestAnimationFrame`). We deliberately decided the **engine owns the loop** (Phase 2), so the
literal split fits worse than it sounds — we don't want the game calling `render()` at all.
`World` is closer to Godot's `SceneTree` / a Unity scene facade than to Three.js's raw split,
which suits an engine-driven loop better.

### Recommendation — ACCEPTED

**Option B (confirmed).** It directly retires the fragmentation that's part of why 3D feels bad today, keeps
`Scene` clean for the streaming/serialization code that depends on it, and gives controls,
environment, lighting, and picking a natural home. The cost is one well-defined concept. Under B,
`Game` exposes `World` (which internally holds a `Scene`); existing games that use `Scene`
directly keep working — `World` wraps, it doesn't replace.
```
