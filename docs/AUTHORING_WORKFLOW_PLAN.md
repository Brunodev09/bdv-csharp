# Authoring Workflow Plan — "Why Unity is easier, and how we get there"

**Status:** Phases 1 and 2 landed (see §6). Phases 3–4 are draft.
**Follows:** `UNIFIED_3D_PLAN.md` (Phases 0–8, landed).
**Goal:** Close the gap between "BdvEngine can render a 3D scene" and "BdvEngine is a nicer
place to *build* a 3D game than Unity" — for a solo author working with an AI agent.

---

## 1. TL;DR

The unified-engine plan worked. `World` / `Scene` / `SimObject` / one `Camera` / material→shader
dispatch / `.glb` loading / lights-as-nodes / orbit controls / picking / billboards all shipped.
Rendering a 3D scene is no longer the problem.

Building a *game* still is, for two separate reasons that get confused with each other:

1. **Missing engine features** — no skeletal animation, no shadows, no 3D collision. These are
   real and blocking, and they're listed in §9. They are ordinary work: known algorithms,
   bounded scope.
2. **Missing authoring workflow** — and this is the one that actually explains the Unity gap.
   **A level in BdvEngine exists only as C# source.** `ValheimGame.cs` is 456 lines of
   `BuildTerrain(); ScatterTrees(); ScatterRocks();`. Moving one rock is a recompile.

You could build every feature in §9 and building a game would *still* feel worse than Unity,
because Unity's advantage was never the feature list — **it's that the scene is data and the
inspector edits it live**. Unity's C# recompile is slow too; it just isn't in the common path,
because 90% of iteration is tuning values, and tuning values never touches the compiler.

**This plan makes the scene a file.** That single change unlocks the inspector, prefabs, live
tuning, and — the reason it matters here specifically — it lets Claude author and edit levels
directly, which it can never do in Unity's GUI.

**The strategic bet:** do *not* rebuild Unity's editor. Unity's GUI is optimised for a human
dragging things with a mouse, and an AI agent cannot drive it at all. This engine's asymmetric
advantage is that it is already AI-legible — `AGENTS.md`, one-file sketches, `--shot out.png` as
a headless verification loop. Scene-as-data serves *both* audiences with the same artifact: a
file you tune in an inspector and I write directly.

---

## 2. The problem: what Unity actually gives you

Four things, none of which are features in the rendering sense:

1. **The scene is data, not code.** You never write `new GameObject()` to place a tree. You place
   it; it serialises to a file. Consequence: *anything* can edit a level — the editor, a
   generator script, a diff, an AI agent.
2. **Everything is inspectable and mutable while running.** Play mode + inspector. Change a
   value, see it now. This is the whole ballgame: it collapses the tune→compile→restart loop
   from ~30s to ~0s, and it's why Unity tolerates a slow compiler.
3. **Prefabs.** Compose an object once, instance it a thousand times, edit the source, all
   instances update.
4. **Inspector UI is generated, never written.** A public field on a MonoBehaviour becomes a
   slider for free. Nobody writes tuning UI.

### Concrete friction, from our own examples

- **A level is source code.** `Examples/Valheim/ValheimGame.cs` (456 lines) builds its world in
  `BuildTerrain()` / `BuildWater()` / `ScatterTrees()` / `ScatterRocks()` / `BuildPlayer()`.
  There is no artifact representing "the forest island" — only the program that generates it.
  Retuning tree density is an edit-compile-run cycle.
- **Materials are unnameable by default.** `Materials.Standard(Color)` auto-generates
  `__std_{n}` (`Core/Materials.cs:15`), so a material has no stable identity across runs. Nothing
  can reference "the bark material" to retune it. (The stable overload exists —
  `Materials.Standard(string, Color)` at `Core/Materials.cs:19` — it's just not the default path.)
- **Object identity is positional and slightly wrong.** `World.Add` does
  `new SimObject(_nextId++, $"obj_{_nextId}")` (`Core/World.cs:25`) — arguments evaluate left to
  right, so the object with id `1` is named `"obj_2"`. Harmless today; it becomes a correctness
  bug the moment names are identity in a scene file.
- **Tuning UI is hand-written or absent.** ImGui is wired up (`Core/Engine.cs:142`) and drives
  exactly one thing: the FPS/draw-call overlay (`Core/Engine.cs:253`). Every other tunable is a
  literal in C#.
- **There is no scene serialiser**, in either direction. `Save/SaveManager.cs` is game-state
  slots, not level data.

---

## 3. What already exists (the seams — this is why Phase 1 is assembly, not invention)

The deserialisation spine is **already written**, because the component/behavior system was
designed for JSON from the start:

| Piece | Where | What it gives us |
|---|---|---|
| `Transform.SetFromJson` | `Utils/Transform.cs:57` | position/rotation/scale from JSON |
| `IComponentData.SetFromJson` + `IComponentBuilder` | `Components/Component.cs:5-26` | typed component construction |
| `ComponentManager.ExtractComponent(JsonElement)` | `Components/Component.cs:56` | `"type"` → component, with a clear error |
| `BehaviorManager.ExtractBehavior(JsonElement)` | `Behaviors/Behavior.cs:52` | same for behaviors |
| `Registrations.RegisterDefaults()` | `Registrations.cs` | the builder registry, already populated |
| `SimObject` tree + `AddChild` | `World/SimObject.cs:36` | the hierarchy to deserialise *into* |

So the node-level work is done. What's missing is the **tree walk**, the **serialiser going the
other way**, and a **mesh/material reference scheme**.

The hot-reload pattern is also already proven, in the GUI:

- `Gui/UiNode.cs` — a flat, all-optional, type-discriminated JSON schema. The precedent to copy.
- `Gui/UiLoader.cs` — stateless `Load`/`Build`, safe to call repeatedly.
- `Gui/HotReloadableUi.cs` — `FileSystemWatcher` sets a `volatile bool _dirty` on a background
  thread; `Tick()` rebuilds on the main thread with a 200ms debounce; **a broken save keeps the
  last-good tree and prints the error** (`HotReloadableUi.cs:122-127`). That error-handling
  choice is the difference between hot reload being delightful and being a footgun. Copy it
  exactly.

And the verification loop exists: `EngineConfig.CapturePath` / `CaptureFrame` →
`dotnet run sketch.cs -- --shot out.png` renders N frames headless and exits.

---

## 4. Design principles

1. **The scene is a file.** C# generates *procedural* content (terrain, scatter); the file holds
   *authored* content (the placed, tuned, named things). A generator's output can be baked to a
   file and then hand-edited — that's the Valheim path.
2. **Round-trip or it doesn't count.** Load → edit → save must be lossless and produce a *stable,
   diffable* file. Key ordering fixed, floats formatted invariantly. If saving a file you didn't
   touch produces a diff, the format is wrong.
3. **Never lose the author's work.** A broken file keeps the last-good scene and prints an error
   (the `HotReloadableUi` rule). Save writes to a temp file and renames.
4. **Generated inspector UI, never hand-written.** Reflect over public fields. Adding a tunable
   to a behavior must cost zero UI code — this is the property that makes the whole thing pay off.
5. **Code and data are peers, not layers.** A scene file can reference a behavior by type name;
   a C# game can load a scene file and then keep building on it programmatically. Neither owns
   the other.
6. **Editor is a mode, not an application.** No separate editor binary, no project window, no
   asset database. An ImGui overlay toggled with F1 inside the running game. The game *is* the
   editor.
7. **Optimise for two authors.** Every artifact this plan creates must be equally editable by a
   human with a mouse and by an agent with a text editor. This rules out binary formats and
   rules in JSON with comments allowed.

---

## 5. Target authoring workflow

### Before (today, `ValheimGame.cs` shape)

```csharp
private void ScatterTrees()
{
    for (int i = 0; i < 400; i++)
    {
        var pos = /* noise sample */;
        var trunk = new SimObject(_nextId++, "trunk");
        trunk.Transform.Position = pos;
        trunk.Transform.Scale = new Vector3(0.3f, 4f, 0.3f);   // ← retune = recompile
        trunk.AddComponent(new MeshComponent(_cube, "bark"));
        var canopy = new SimObject(_nextId++, "canopy");
        // ...3 more objects, AddChild ceremony, _treeColliders.Add(...)
        World.Add(trunk);
    }
}
```

Every constant in there is a recompile away from being tuned.

### After (target)

```jsonc
// levels/forest.scene.json — hand-editable, agent-editable, diffable
{
  "environment": { "sky": "#6E8CB4", "ambient": "#4D4D59",
                   "sun": { "direction": [-0.5, -1, -0.35], "color": "#FFF2D9" } },
  "materials": {
    "bark":   { "shading": "lit", "color": "#4A3524" },
    "canopy": { "shading": "lit", "color": "#2F5A32", "doubleSided": true }
  },
  "nodes": [
    { "name": "player", "model": "assets/models/hero.glb", "position": [0, 2, 0],
      "behaviors": [ { "type": "keyboardMovement", "speed": 6.0 } ] },

    { "name": "pine_01", "prefab": "prefabs/pine.prefab.json", "position": [12, 0, -8] },
    { "name": "pine_02", "prefab": "prefabs/pine.prefab.json", "position": [15, 0, -3],
      "scale": [1.2, 1.4, 1.2] }
  ]
}
```

```csharp
public sealed class ValheimGame : Game
{
    public override void Init()
    {
        BuildTerrain();                              // procedural stays procedural
        World.LoadScene("levels/forest.scene.json"); // authored content is data
    }
}
```

**The loop this produces:** run the game → press F1 → click a pine → drag its scale slider →
watch it change → press Save → the JSON on disk updates → git diff shows exactly what changed.
No compile anywhere in that sentence. And I can edit the same file directly when you'd rather
describe a change than click it.

---

## 6. The phased plan

Each phase leaves the tree building and every existing game running. Phases 1–2 are the ones that
matter; 3–4 are cheap follow-ons that fall out of the same machinery.

### Phase 1 — Scene as data (the keystone) — ✅ LANDED

**Goal:** `World.LoadScene(path)` and `World.SaveScene(path)`, round-tripping losslessly, with
hot reload.

**Schema** (`SceneNode`, mirroring `Gui/UiNode.cs` — flat, all-optional, defaults everywhere):

```
name, position[3], rotation[3] | quaternion[4], scale[3],
mesh: { primitive: "cube"|"sphere"|"plane", segments?, rings?, size? }
model: "path.glb"          // mutually exclusive with mesh
prefab: "path.prefab.json" // Phase 3; expands in place
material: "name"           // key into the file's "materials" block
components: [ { type, ... } ]
behaviors:  [ { type, ... } ]
children: [ ...recursive... ]
```

**Critical schema decision:** a node references a **mesh *spec*, not a `Mesh`**. `Mesh` is a
live GL buffer; what serialises is "cube" or a `.glb` path. `MeshComponent` currently takes a
constructed `Mesh` (`Core/World.cs:219`), so the loader constructs the mesh from the spec and
caches by spec-key — which also gets us mesh sharing for free (400 pines, one cube buffer,
matching what `ValheimGame` does by hand today).

**Work:**
1. `Save/SceneSerializer.cs` — `Serialize(World) → JSON` and `Deserialize(JSON) → SimObject tree`,
   delegating to the existing `ComponentManager` / `BehaviorManager` / `Transform.SetFromJson`.
2. **Serialisation is the harder half** and needs a new contract: components/behaviors can
   currently only be *built* from JSON, never written back. Add `void WriteJson(Utf8JsonWriter)`
   to `IComponentData` / `IBehaviorData` — a default reflection-based implementation on
   `BaseComponent`/`BaseBehavior` covers every existing type, with an override where a type needs
   custom shape.
3. Materials as data — a `"materials"` block, resolved through `MaterialManager` by name. Make
   `Materials.Standard(string name, Color)` the documented default so materials have stable
   identity (`Core/Materials.cs:19`).
4. `HotReloadableScene`, a direct port of `Gui/HotReloadableUi.cs`: watcher → dirty flag →
   debounced main-thread rebuild → **last-good scene on parse failure**.
5. **Gotcha:** the engine calls `_world.Scene.Load()` exactly once, after `Game.Init()`
   (`Core/Engine.cs`, `OnLoad`). A subtree loaded later — every hot reload — must have `Load()`
   called on it explicitly, or its meshes/textures never upload. Put this in `LoadScene` itself,
   guarded on GL context existing.

**Acceptance — all met:**

| Gate | Result |
|---|---|
| Save → load → save is byte-identical | ✅ 3096 bytes, zero diff (`sketches/scene_roundtrip.cs`) |
| Code-built vs file-loaded frame is identical | ✅ **0 of 3,686,400 pixels differ** |
| A JSON edit applies live, without restarting | ✅ `sketches/scene_hotreload.cs` |
| A malformed save keeps the last-good scene | ✅ same test, asserted |
| Valheim's scatter bakes and reloads | ✅ 190 nodes / 8 materials (`dotnet run -- --bake <path>`) |
| A hand-written file loads from the spec alone | ✅ `sketches/levels/handwritten.scene.json`, first try |

**What shipped**

- `Save/SceneSerializer.cs` — the format, both directions. `Save/SceneJson.cs` — colour/vector
  encoding + the reflection bridge. `Save/HotReloadableScene.cs` — watcher, debounce, last-good.
- `World.LoadScene` / `ReloadScene` / `SaveScene`.
- Seams: `Mesh.Source`, `SimObject.Source`, `SimObject.Behaviors`, `BaseComponent.Data` /
  `BaseBehavior.Data`, `MaterialManager.TryPeek`, and reverse type maps on both builder registries.
- `AGENTS.md` gained a **Scenes as data** section — the step that makes the format usable by an
  agent, pulled forward from Phase 4 because it's what turns this into leverage.

**Deviations from the plan above, and why**

1. **No `WriteJson` on the data interfaces.** The plan proposed adding one to `IComponentData` /
   `IBehaviorData`. That would have serialised the *construction-time* data bag — but components
   copy their data into live fields, and those live fields are exactly what the Phase 2 inspector
   will edit. Writing the bag would silently save pre-edit values. Instead the serialiser reflects
   over **public fields of the live object, unioned with the data bag, live winning**. Zero edits
   to the nine existing data classes, and it round-trips fields their own `SetFromJson` ignores
   (`ColliderComponent.Color` / `DebugDraw` were both already lossy that way).
2. **Type discriminators via default interface members.** `IComponentBuilder.ComponentType` /
   `IBehaviorBuilder.BehaviorType` default to null, so no third-party builder breaks; the nine
   built-ins override them and the managers keep a reverse map.
3. **Mesh specs are stamped in `Mesh.Cube/Sphere/Plane`, not in `Primitives.*`.** A game calling
   `Mesh.Cube()` directly (Valheim does) then serialises for free, with no game-side change.
4. **Vectors are written as `{"x":..,"y":..,"z":..}`, not `[x,y,z]`.** The object form is what
   every existing `SetFromJson` already parses, so components round-trip through the builder
   registry unchanged. Arrays are still accepted on read. Terser, but it would have silently
   loaded `RotationBehavior` as zero — caught by the round-trip gate before it shipped.

**Known limitations, both reported loudly rather than silently wrong**

- A mesh assembled by hand from vertices (`HeightmapTerrain`, `GridHelper`) has no spec and can't
  be written; the node saves without it and the serialiser says so. Correct — that content is
  procedural and belongs in code.
- A **texture generated at runtime** has the same problem: Valheim's terrain material names
  `terrain_tex`, which isn't a file, so a reload logs `no loader for extension ''`. Same rule,
  same fix: keep generated assets in code.
- Materials named by `Materials.Standard(Color)` serialise under their auto-generated `__std_N`
  name. Use the `Materials.Standard(name, color)` overload for anything you intend to retune.

### Phase 2 — Editor overlay (the payoff) — ✅ LANDED

**Goal:** Unity's core loop — click object, change value, see it live, save — as an ImGui overlay
inside the running game. ImGui.NET is already referenced and already composited
(`Core/Engine.cs:223`), so this is pure additive UI.

**Work:**
1. **Hierarchy panel** — walk `Scene.Root`, tree view, select. ~80 lines.
2. **Inspector panel** — for the selected `SimObject`: transform widgets, then **reflect over
   each component's and behavior's public fields** and emit the matching ImGui widget by type
   (`float`→`DragFloat`, `Vector3`→`DragFloat3`, `Color`→`ColorEdit4`, `bool`→`Checkbox`,
   `enum`→`Combo`). This is Unity's actual trick and it's ~200 lines. **This is the single
   highest-leverage file in the plan** — after it exists, every tunable anyone ever adds gets UI
   for free, forever.
   - Optional refinement: a `[Range(min,max)]` attribute to get sliders instead of drags.
3. **Click-to-select** — already done. `World.Pick(Camera.ScreenRay(...))` at `Core/World.cs:49`.
   Just wire the mouse to it and skip the click when ImGui wants the mouse
   (`ImGui.GetIO().WantCaptureMouse`).
4. **Transform gizmo** — translate first (three axis handles, drag along a screen-projected
   axis). Rotate/scale later; translate covers most of the value.
5. **Save button** — calls Phase 1's serialiser. Also: duplicate, delete, reparent, "add child".
6. **Environment panel** — sky, ambient, sun direction/colour as live sliders. Cheap, and
   day/night tuning is currently a recompile.

**Acceptance — met.** `sketches/editor_persist.cs` drives the editor's own code paths and then
re-reads the file off disk: a gizmo move, a Scale field, a generated behavior field, a material
colour + roughness, and a Duplicate (with its child subtree) all survive the save. Every Phase 1
gate still passes.

**What shipped**

- `Editor/Inspector.cs` — reflection over public fields → ImGui widgets, plus `[Range]` and
  `[HideInInspector]`. It reads the exact field set the serialiser writes, so anything editable is
  anything persistable. **This is the file that makes the editor pay off**: a new public field on a
  behavior gets a slider for free, forever.
- `Editor/SceneEditor.cs` — hierarchy, inspector, environment, click-to-select, a translate gizmo,
  Save/Reload, Duplicate/Delete/Add-child. Public `Select` / `Save` / `Duplicate` so it's
  scriptable and testable without a mouse; `SceneEditor.Active` reaches the running one.
- `EngineConfig.Editor` (on by default) + `EditorVisible` / `--editor`. The editor is hosted by the
  engine, so **every game gets F1** — "the game is the editor" only works if it's always there.

**Two bugs the build surfaced, both structural rather than cosmetic**

1. **The camera fought the gizmo.** `OrbitControls` reads `InputManager.IsLeftDown` directly, so
   dragging a handle also orbited. Fixed with `InputManager.UiWantsMouse` / `UiWantsKeyboard`,
   published by the engine each frame from ImGui's capture flags plus the editor's drag state, and
   respected by `OrbitControls` — which keeps tracking the cursor while yielding, so releasing a
   panel doesn't snap the camera by the accumulated delta. Any future controller should read the
   same flag.
2. **F1 never fired.** `InputManager.WasKeyPressed` is cleared by `EndFrame()` at the end of the
   update tick, and the editor draws in the render pass — the press flag was always gone. The
   editor now tracks its own key edge.

**Deviations and scope calls**

- `RotationBehavior` kept its rotation in a **private** field, so the inspector could edit it and
  nothing would move. Made public. This is the general rule the inspector imposes and it is a good
  one: *state you want to tune must be a public field* — which is also exactly what makes it
  serialise.
- Live-object fields and their data-bag twins are deduped, live winning — the same precedence the
  serialiser uses, so what you edit is what gets written.
- ASCII-only icons: ImGui's default font atlas has no glyphs beyond it and anything else draws
  as `?`.
- Held to the plan's scope: no docking, no undo, no multi-select, no play/pause. Rotate/scale
  gizmos deferred — translate covers most of the value and the Transform fields cover the rest.

### Phase 3 — Prefabs

**Goal:** compose once, instance many.

A `.prefab.json` is *the same schema as a scene node subtree* — no new format. Instancing =
deserialise + reparent + apply the instance's transform. `"prefab": "path"` on a node expands in
place at load.

**Scope call:** v1 has **no override propagation** (edit the prefab file → instances update on
next load, not live). Unity's override system is a large chunk of its complexity budget and you
will not miss it at this scale.

**Acceptance:** `pine.prefab.json` replaces the 4-object cube-and-sphere stack in
`ScatterTrees()`; 400 instances load; editing the prefab file changes all of them next run.

### Phase 4 — Everything else becomes data

Once Phases 1–2 exist, the pattern generalises for near-zero marginal cost:

- **`materials.json`** — hot-reloaded, so retuning a colour stops being a recompile.
- **A `[Tunable]` static registry** — mark any static config field, get it in the inspector and
  persisted to a `tuning.json`. Covers the game-logic constants that aren't per-object
  (`DayLength`, `WaterLevel`, movement speeds).
- **`AGENTS.md` update** — document the scene format so I can author levels directly. This is
  the step that converts the whole plan into leverage on *our* workflow specifically; without it
  the format exists but I won't reliably use it.

---

## 7. Cross-cutting concerns & risks

- **Reflection cost.** The inspector reflects per-frame over the *selected* object only (one
  object, ~10 fields) — irrelevant. Do **not** let reflection touch the serialiser's hot path if
  scenes get large; cache `FieldInfo[]` per type in a static dictionary.
- **Round-trip fidelity is the whole risk.** If save-then-load isn't lossless, the editor eats
  your work and you'll stop trusting it — which kills the plan. Gate Phase 1 on an automated
  round-trip test (`Examples/ColonySim.AITests/` shows the pattern exists): load → save → load →
  save, assert byte-identical on the second pair.
- **Float formatting.** `InvariantCulture`, round-trip format (`"R"` or `G9`). A scene file that
  churns in git because of `0.30000001` is a scene file nobody diffs.
- **Procedural vs authored is a real boundary, not a temporary one.** Terrain and scatter should
  *stay* code. The plan is not "serialise everything" — it's "serialise the things you tune by
  hand." Offer a `World.SaveScene()` bake step so generated content can cross the line
  deliberately.
- **Don't build a project window / asset database.** Paths are paths. GUID-based asset references
  solve a rename problem you do not have at this scale, and they'd make the files unreadable to
  both authors.
- **2D games must not regress.** ColonySim/HexStrategy build their worlds from their own data
  files already; `LoadScene` is additive and they simply never call it.
- **Scope discipline.** Phase 2 is *not* a Unity editor. No docking layouts, no undo stack (v1),
  no multi-select, no play/pause/step. Resist all of it until the core loop is used daily.

---

## 8. Recommended sequencing

**Phase 1 → Phase 2** is one vertical slice and should land together — a serialiser with no
inspector is half a feature (you can't tune), and an inspector with no serialiser is worse than
half (you tune, then lose it). Ship them as one thing: *edit a tree in the running game, save,
relaunch, it persisted*.

Then, by value: **Phase 3 (prefabs)** → **Phase 4 (materials/tunables as data + AGENTS.md)**.

**Interleave the engine gaps from §9 by need, not by list.** The honest ordering: **skinned glTF
animation** is worth more than all of Phase 3–4 combined, because it's the difference between
"can and cannot ship a character." If a choice has to be made, it goes first.

---

## 9. Appendix — engine feature gaps vs Unity

Separate from workflow. Ranked by whether they block shipping a 3D game.

### Blocking

1. **Skeletal animation.** `Core/GlbLoader.cs:13` states it: *"NOT yet: skins, skeletal / morph
   animation."* Model → rig → animate in Blender → export `.glb` → the engine keeps the bind-pose
   mesh and discards the rest. `Animation/Anim.cs` is 59 lines of procedural sine/ease helpers —
   no clip, no keyframe, no sampler, no skin matrix palette anywhere in the tree. **Every
   animated character is currently impossible.** Needs: glTF `skins` + `animations` parsing, a
   joint matrix palette uniform, a skinning vertex shader, and a clip player with crossfade.
2. **Shadows.** `grep -ril shadow` hits only `Graphics/Lighting.cs` and two sprite shaders — the
   *2D* occluder system. The 3D path has none. Biggest single visual-quality gap: nothing is
   grounded. Start with one shadow map for the sun.
3. **3D collision + character controller.** `Utils/Collision.cs` is `RectRect`/`CircleCircle`/
   `LineRect` — 2D. `Behaviors/RigidBodyBehavior.cs` has `Vx, Vy` — 2D. No 3D collider, no
   capsule sweep, no move-and-slide, no triggers. The cost is visible: `ValheimGame` hand-rolls
   terrain grounding and keeps a `List<Vector4>` of tree spheres purely to stop camera clipping.

### Bounded, but you'll hit them fast

4. **No frustum culling, no instancing.** `MeshRenderer.Render` calls `Collect(scene.Root)` and
   draws everything, one `mesh.Draw()` per object; no `DrawElementsInstanced` anywhere in the
   engine. A 400-pine forest is 1600 draw calls of the same two meshes. (Culling was listed under
   the previous plan's Phase 2; it didn't land.)
5. **Skybox + fog.** `Environment.Sky` is a clear colour. A gradient/cubemap sky plus distance
   fog is ~150 lines and buys more perceived quality per line than anything else here.
6. **No transparency sort.** Billboards get a special late pass with `DepthMask(false)`; general
   alpha-blended materials have no back-to-front queue. Water, glass, and foliage cards will all
   render wrong.
7. **Lighting ceiling.** 8 forward lights (`Core/MeshShader.cs:44`), unshadowed, no IBL — which
   is why `AGENTS.md` has to warn that PBR metals look muted.

### Later

3D particles (`Graphics/ParticleEmitter.cs` is 2D), post-processing on the 3D path
(`Graphics/PostFx/Bloom.cs` is 2D screen-space), spatialised audio (`Audio/AudioManager.cs` is
stereo pan only), navmesh/pathfinding (nothing), LOD, input action maps.

### Explicitly not doing

Asset store, addressables, DOTS/ECS, the Animator state-machine graph UI, multi-platform build
farm. A code-driven animation state machine is *better* than Unity's graph for this workflow —
it diffs, it reviews, and an agent can write it.

### Blender-side conventions (cheap, high return)

Half of "Unity imports assets so easily" is Unity having opinions and enforcing them. Adopt now,
before there are many assets: **glTF only, never FBX**; Y-up; metres; apply transforms before
export; one `.glb` per asset; textures embedded. Worth a small Blender export operator so it's
one click and impossible to get wrong.

---

## 10. Open questions

1. **Scene format — JSON or a terser DSL?** JSON reuses `Gui/UiNode.cs`'s proven pattern and the
   whole `System.Text.Json` spine that already exists, but it's verbose for 400 scattered trees.
   Recommendation: JSON, and don't serialise scatter — keep it procedural (§7).
2. **Undo in the editor.** v1 skips it. Is "save often, git is your undo" acceptable, or does the
   inspector need a command stack before you'd trust it?
3. **How far does "the game is the editor" go?** Phase 2 assumes the overlay ships inside the
   game binary behind F1. Alternative: a dedicated `Examples/Editor` host that loads a scene file
   and nothing else. The in-game version is simpler and always available; the separate host keeps
   editor code out of a shipped build. Recommendation: in-game now, `#if DEBUG` later if it ever
   matters.
4. **Skinning before or after the workflow slice?** §8 sequences workflow first, but skinning is
   the higher-value single feature. If a real character is imminent, invert it.
5. **Does `SimObject` need stable persistent IDs?** Names are identity in the proposed format,
   which is fine for authored content but weak for cross-file references (prefab overrides, save
   files pointing at level objects). Defer until something actually needs a cross-reference.
