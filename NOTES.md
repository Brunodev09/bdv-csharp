# BdvEngine (C#) — Project Notes

A working capture of architectural decisions, performance trade-offs, and
non-obvious quirks for this engine. The codebase explains *what*; this file
explains *why*.

---

## Project layout

```
src/
  BdvEngine/                         # the engine library
    Engine.cs                        # 2D engine entry point
    Engine3D.cs                      # 3D engine entry point
    Game.cs / Game3D                 # base classes consumers extend
    Time.cs                          # global Time.Total / Time.Delta clock
    Graphics/                        # SpriteBatcher (Ground/Object/UIBack/UI), TileMap, Sprite,
                                     #   AnimatedSprite, Material, Texture (UploadRgba/CreateBlank)
    Gl/                              # GL helpers, Shader, GLStats, Gfx (FramebufferW/H, WindowW/H)
    World/                           # Scene, SimObject, Transform
    Components/                      # Sprite/AnimatedSprite/Collision/Camera components
    Behaviors/                       # RigidBody, RayCast, Stateful animation, KeyboardMovement,
                                     #   Wander, Rotation, Pulse
    Animation/                       # Anim.Pulse / PingPong / Ramp / SinWave / Ease.*
    Input/                           # InputManager (Silk-wrapped); engine-owned Key enum
    UI/                              # ImGui-flavored static helpers (legacy; fallback only)
    Gui/                             # Element, Root, Context, Panel, Label, LiveLabel, Image,
                                     #   Button, Slider, Arrow, Checkbox, Scissor, IElementBehavior,
                                     #   PulseOnHoverBehavior
    Text/                            # Font (TTF baking via stb_truetype), FontManager,
                                     #   TextRenderer (Draw + DrawScreen), TextAnim
    Audio/                           # AudioManager, AudioHandle, WavDecoder (OpenAL)
    Save/                            # SaveManager (filesystem JSON)
    Com/                             # Message, MessageBus
    Assets/                          # AssetManager, Registrations
    Utils/                           # Noise, SeededRng

  Examples/                          # standalone consumer programs
    MyGame/                          # animated duck + particles + custom shader + parent/child rotation
    My3DGame/                        # orbit cubes + Phong lighting (still uses ImGui — no 2D overlay yet)
    CollisionGame/                   # rigid body collisions + raycast
    StressGame/                      # 5000 particles + sliders
    TerrainGame/                     # 1024×1024 procedural world (the big one)
    HexStrategyGame/                 # 128×128 hex world + biomes + civs + Gui demo
```

---

## Hard rules / conventions

- **Examples are library consumers**, not CLI subcommands. Each example is its
  own `csproj` with `Program.cs` that constructs an `Engine`/`Engine3D` and
  passes a `Game`/`Game3D` subclass.
- **No `Silk.NET.*` outside the engine.** Examples must never import any Silk
  namespace. The engine wraps what it surfaces — `BdvEngine.Key` mirrors Silk's
  `Key` (same numeric values for identity-cast at the boundary), `MouseContext`
  wraps mouse state, `InputManager` wraps the input loop. All GPU plumbing lives
  behind engine APIs (Sprite, TileMap, SpriteBatcher, Draw, TextRenderer, Gui).
- **ImGui is engine-internal, kept as fallback.** The stats overlay and 3D-only
  examples still use it. Examples on the 2D engine use `BdvEngine.Gui`.
- **Don't pause to ask permission once direction is clear.** Plow through the
  whole task; mid-task confirmations are noise.

---

## Engine lifecycle (2D)

```
Engine.Run
  └─ window options: GL 4.1 Core, ForwardCompatible, VSync, FPS=0
  └─ OnLoad
       AssetManager.Init, AudioManager.Init, Registrations.RegisterDefaults
       Default GL state: Blend = SRC_ALPHA / ONE_MINUS_SRC_ALPHA
       _defaultShader.Use, ImGui controller, _game.Init
  └─ OnUpdate (fires on UpdatesPerSecond=0 = uncapped)
       RigidBodyBehavior.BeginFrame, MessageBus.Update, AudioManager.Update
       Set Game.ViewportWidth/Height from Window.Size
       _game.Update(delta)
  └─ OnRender (uncapped, VSync-bound)
       GLStats.Reset, FPS counter
       Viewport = FramebufferSize (physical, retina-aware)
       Projection = Window.Size (logical world coords)
       _game.Render(shader)               ← TileMap renders here directly
       SpriteBatcher.Flush                ← Ground → Object (Y-sorted) → UI
       Draw.Flush                         ← debug primitives
       ImGui pass (DisplaySize=Size, FramebufferScale=FB/Size)
       Screenshot capture if pending
```

3D engine has the same skeleton with depth test + cull face enabled and a
Phong-style `LitShader`.

### Retina handling (macOS specifics)

- `Viewport = FramebufferSize` — physical pixels. Crisp on retina.
- `Projection = Window.Size` — logical units. World coords stay 1600×900-ish
  regardless of DPI.
- ImGui needs `io.DisplaySize = Size` and `io.DisplayFramebufferScale = FB/Size`
  set **before** `_imgui.Update`, otherwise UI renders at quarter screen.
- `_window.Size` is what the Game subclass should use for things like
  `ScreenToWorld`. The engine sets `Game.ViewportWidth/Height` from
  `Window.Size` in `OnUpdate` and again in `OnRender` — never hardcode 1600×900.

---

## Rendering pipeline

### Sprite-based rendering — `SpriteBatcher`

Single static class with **four** layers:

| Layer | Order | Behavior |
|---|---|---|
| `Ground` | 1st | Per-texture batched, insertion order. One draw per (shader × texture). |
| `Object` | 2nd | Per-quad entries with `sortY`; sorted by Y on flush, run-length batched by texture for stable depth (RimWorld-style feet-on-ground sort). |
| `UIBack` | 3rd | Per-texture batched, insertion order. Reserved for UI backgrounds (panels, button fills, slider tracks) so they sit *behind* `UI`-layer text/images but in front of game objects. |
| `UI`     | 4th | Per-texture batched, insertion order. Text/images/icons that should sit on top of UI backgrounds. |

API:

```csharp
SpriteBatcher.Push(verts, material, worldMatrix, layer, sortY)
SpriteBatcher.DrawTexture(material, srcCol, srcRow, gridCols, gridRows, x, y, w, h, tint, layer, sortY)
SpriteBatcher.DrawTextureUV(material, u0, v0, u1, v1, x, y, w, h, tint, layer, sortY)
SpriteBatcher.DrawSolid(x, y, w, h, color, layer, sortY)   // 1×1 white texture; lazy-init
```

- **Indexed quads.** 4 verts + 6 indices per quad (was 6 verts). Saves ~33%
  vertex shader work and upload bandwidth.
- **Vertex layout**: x, y, z, u, v, r, g, b, a (9 floats / 36 bytes).
- **Deterministic flush order**: `_batchOrder: List<Batch>` preserves insertion
  order so terrain → buildings → humans paint correctly regardless of dict
  iteration semantics.
- **`Push()` quad-detects** input from `Sprite.Load` (BL, TL, TR, TR-dup, BR,
  BL-dup) and pulls the 4 unique corners. Don't push non-quad geometry through
  this path.

### `Sprite` and `AnimatedSprite`

- `Sprite.Layer { get; set; } = SpriteLayer.Ground` — set to `Object` to opt
  into Y-sorted depth (e.g. characters, multi-tile buildings drawn as alpha
  sprites).
- `Sprite.Render` computes `sortY = M42 + Height * M22` (sprite bottom in world
  space) when on the Object layer.
- `AnimatedSprite.UploadFrameUVs` mutates `_vertices` only — no GPU upload.
  All the GL upload happens centrally in the batcher.

### `TileMap` — chunked static VBO renderer

Map is split into `CHUNK_SIZE × CHUNK_SIZE` chunks (default 64). Each chunk
owns its own VAO+VBO+EBO and bakes once on first render. Per-frame work is
just AABB-cull → bind → `glDrawElements`.

```csharp
TileMap map = new(tileSet, w, h, tilePx);
map.LodTileSet = lodSet;     // optional, swapped at low zoom (LodThreshold)
map.SetTile(x, y, idx);      // marks owning chunk dirty (and LOD chunk if exists)
map.Render(camera, viewportW, viewportH);
```

Key properties:

- **Renders directly** — does NOT go through `SpriteBatcher`. TileMap.Render
  binds its own shader (`BatchSpriteShader`) and issues per-chunk draws.
  Order: TileMap.Render runs first (paint order), then SpriteBatcher.Flush
  draws everything else on top.
- **LOD chunks** are a separate parallel array, lazy-baked the first time
  they're rendered. Same chunk indices, different atlas/UVs.
- **Chunk size trade-off**:
  - 32: more granular dirty rebakes, but ~700 draw calls at full zoom-out
  - 64 (current): balanced, ~270 zoom-out
  - 128+: ~70 zoom-out, but rebakes are 16× more expensive
- **Lazy bake but no unload**: chunks stay GPU-resident once baked. For 1024²
  worst case = ~150 MB. Acceptable on unified memory; would need LRU eviction
  for multi-million-tile streaming worlds.

### Render order in TerrainGame

```
TileMap.Render (ground)          ← direct GL draw
TileMap.Render (overlay)         ← direct GL draw
RenderBuildings → SpriteBatcher.DrawTexture (Object layer, sortY = bottom)
RenderHumans → SimObject.Render → Sprite.Render (Object layer)
Selection rectangles via Draw.RectOutline

End of frame:
  SpriteBatcher.Flush  → Ground → Object (Y-sorted) → UI
  Draw.Flush           → debug primitives
  ImGui.Render         → UI panels & stats overlay
```

### Stats overlay

Shown when `EngineConfig.ShowStats = true`. Top-right ImGui window with:

- FPS (color-coded: green ≥55, yellow ≥30, red below)
- Draw calls
- Chunks rendered (only meaningful for TileMap users; 0 in other examples)

---

## Performance notes — what we did and why

### Frame loop is uncapped

`FramesPerSecond = 0`, `UpdatesPerSecond = 0`, `VSync = true`. Frame rate is
governed by VSync (60 Hz typical, 120 Hz on ProMotion).

### Removed per-frame `glGetError`

We polled it per-frame initially. It forces a GPU sync and tanks FPS on macOS.
Removed; relies on shader compile/link errors logged at load time.

### deltaTime-correct movement

All movement and animation must scale by deltaTime. Affected:
`KeyboardMovementBehavior`, `RotationBehavior`, particles, `WanderBehavior`.
Speeds are quoted in **px/sec** not px/frame. UI sliders were rescaled
accordingly (e.g. MyGame Speed `1–20 (px/frame)` → `60–1200 (px/sec)`).

### Behaviors render in `Render`, not `Update`

`RayCastBehavior` originally drew its line/hit during Update; this caused
flicker because Update fires multiple times per frame in some scenarios.
Behaviors now have an `IBehavior.Render(shader)` hook called from
`SimObject.Render`. Visual side-effects belong there.

### Per-entity AABB culling in TerrainGame

- `_buildings` linear-scanned + AABB-tested against the camera view rect
  before queueing into the Object layer.
- Humans (children of `_humanScene.Root`) AABB-tested with a 120-px margin
  before calling `Render`.
- TileMap chunks AABB-tested in `TileMap.Render` (built-in).

Linear scan is fine at 600 buildings + 150 humans. Add a uniform spatial
grid only if entity counts grow past ~10K.

### Architectural trade-offs we considered but didn't ship

- **Single megabuffer for terrain** (one VBO baked for the whole map, drawn
  in 1 call). Probably the optimal path for static procedural terrain on
  unified-memory hardware. Current chunked approach trades 1-call-perfection
  for runtime-edit flexibility. Easy to switch later if terrain stays static.
- **Instanced rendering** for objects. Worth it once entity counts cross
  ~10K. Not now — would be a big SpriteBatcher refactor.
- **Texture arrays** instead of atlas-with-margins. Would fix UV bleed
  under mipmapping. We don't enable mipmaps yet; defer.
- **Spatial index for entity culling**. Linear scan still wins at our entity
  count.
- **Persistent off-screen simulation**. Game-side concern, not engine.
- **Chunk LRU unloading**. Memory budget isn't a constraint at 1K maps.

### Honest perf notes (chunked vs old per-frame batcher)

The old batched-everything-into-one-draw-call path actually ran ~5–25 FPS
*faster* on the M4. Why: the M4's unified memory makes per-frame ~3.5 MB
vertex uploads basically free, and 5 vs 270 draw calls is a small dispatch
delta on Metal.

What chunked actually buys us:

- Constant per-frame CPU cost regardless of map size (no upload that scales
  with visible tile count).
- No GC churn from per-frame `List<float>` growth.
- Headroom for 4K+ tile-sided maps where the per-frame upload becomes real.
- Foundation for partial updates / fog-of-war / threaded bake later.

For *this* game on *this* hardware, the old approach was a defensible choice.
The chunked rewrite is the right architecture once you push past the current
size or want runtime tile editing.

---

## Subsystems

### Audio (`Audio/AudioManager.cs`)

Silk.NET.OpenAL + bundled OpenAL Soft native (osx-arm64/osx-x64/linux-x64/win
all included via `Silk.NET.OpenAL.Soft.Native`).

```csharp
AudioManager.Load("hit", "audio/hit.wav");
AudioManager.Play("hit", new PlayOptions { Volume = 0.6f, Pan = 0.3f, Channel = AudioChannel.Sfx });
AudioManager.PlayMusic("theme.wav", 0.4f);
AudioManager.MasterVolume = 0.8f;   // master / sfx / music gains independent
```

- Lazy-init on first call. Graceful no-op if no audio device.
- WAV decoder supports 8/16-bit PCM mono/stereo only (RIFF parser in
  `WavDecoder.cs`).
- Pan simulated via positional audio on a unit arc.
- `AudioManager.Update()` reaps finished sources; called from `Engine.OnUpdate`.

### Save (`Save/SaveManager.cs`)

Filesystem JSON, synchronous (we're not in IndexedDB anymore).

```csharp
SaveManager.Init("MyGame");           // sets folder under OS user-data
SaveManager.Save("slot1", obj);       // atomic write via .tmp + rename
var s = SaveManager.Load<MyState>("slot1");
SaveManager.Exists("slot1");
SaveManager.List();                   // List<SaveListEntry(Slot, Timestamp, SizeBytes)>
SaveManager.Delete("slot1");
```

- Path: macOS `~/Library/Application Support/<app>/Saves/<slot>.json`,
  Linux `~/.config/<app>/Saves`, Windows `%AppData%/<app>/Saves`.
- Atomic write: stages to `.tmp`, then `Move`. Avoids torn writes on crash.
- Slot names are sanitized to `[A-Za-z0-9._-]`.
- Emits `MESSAGE_SAVE_WRITTEN` / `MESSAGE_SAVE_DELETED` on the message bus.

### Input

`InputManager` static, Silk.NET-backed. Polling API:

```csharp
InputManager.IsKeyDown(Key.W);
InputManager.GetMousePosition();
InputManager.ConsumeWheelDelta();   // returns wheel since last consume; subsequent calls 0
```

Mouse buttons emit `MOUSE_DOWN` / `MOUSE_UP` on the message bus.

### Messages

```csharp
Message.Send("MOUSE_DOWN", sender, context);
Message.Subscribe("MOUSE_DOWN", this);   // requires IMessageHandler.OnMessage
MessageBus.Update(delta);                // dispatches queued messages once per Update
```

`Message.SendCritical` dispatches synchronously (rare, used for time-sensitive
things).

### UI

ImGui-flavored declarative helpers:

```csharp
var panel = UI.Panel(UIAnchor.TopLeft);
UI.Heading(panel, "Title");
UI.Text(panel, "static");
UI.TextLive(panel, () => $"{fps} FPS");   // recomputed each frame
UI.Input(panel, "Seed", value, v => state = v);
UI.Button(panel, "Go", OnClick);
UI.Slider(panel, "Speed", value, 60f, 1200f, v => state = v);
UI.Spacer(panel);
```

### Behaviors

`IBehavior.Update(delta)` and `IBehavior.Render(shader)` are both called from
`SimObject.Update` / `SimObject.Render`. Visual behaviors (raycast line, debug
overlays) draw in `Render` to avoid flicker.

`RigidBodyBehavior.BeginFrame()` is called from `Engine.OnUpdate` before
`_game.Update`. Bodies register in `AllBodies` via `SetOwner` (use
`override`, not `new`).

### Components vs Behaviors (distinction)

- **Component**: data + render hook. Sprites, animated sprites, colliders,
  cameras. Owned per-SimObject.
- **Behavior**: per-tick logic. Movement, rotation, raycasting, AI. Owned
  per-SimObject.

A SimObject typically has one Component (its visual) and 0+ Behaviors.

---

## Examples summary

| Example | What it shows |
|---|---|
| `MyGame` | Animated duck, custom shader (crate), parent/child rotation, particles, all `Draw.*` shapes, UI panel, FPS overlay |
| `My3DGame` | 3D orbiting cube hierarchy with sphere child, Phong lighting, ground plane |
| `CollisionGame` | 7 walls + 3 boxes + 4 balls bouncing, kinematic player, raycast (no flicker) |
| `StressGame` | 5000 particles, sliders, deltaTime-correct |
| `TerrainGame` | 1024×1024 procedural world: heightmap + biomes, rivers carved downhill, beaches, cities with buildings (Y-sorted Object layer), wandering humans, mouse hover/click tile selection, LOD swap at low zoom |

All five build standalone via `dotnet build` of their own csproj. VS Code
launch configs in `.vscode/launch.json` cover all of them.

---

## Known caveats / footguns

- **Don't push non-quad geometry through `SpriteBatcher.Push`.** It assumes
  the 6-vert quad layout from `Sprite.Load` and reads only 4 of those.
- **Mipmaps + atlas = UV bleed.** We don't enable mipmaps yet. If you do,
  switch to per-tile texture arrays or add UV padding to atlases.
- **`Material.Color` on a custom shader path** still applies through the
  default uniform. If you write a custom shader that doesn't sample
  `v_color`, the multiplier won't apply.
- **`AudioManager.MasterVolume` / `SfxVolume` / `MusicVolume` setters
  rescale all active sources to `channel × master`** — they don't preserve
  the per-instance volume you passed at `Play()` time. Use
  `handle.SetVolume()` for per-instance overrides.
- **SaveManager `List()`** scans `*.json` in the saves folder; if a user
  drops a non-save file there it'll appear in the list. Filter at app level
  if it matters.
- **TileMap chunks never unload.** 256 chunks × ~590 KB worst case = ~150
  MB resident on a fully-baked dense map. Acceptable; revisit if maps grow.

---

## Build & run

```bash
# Build everything
dotnet build src/BdvEngine/BdvEngine.csproj -c Release
dotnet build src/Examples/TerrainGame/TerrainGame.csproj -c Release

# Run an example
dotnet run --project src/Examples/TerrainGame/TerrainGame.csproj -c Release

# Or use VS Code: F5 → pick from Run & Debug dropdown
```

Target framework: `net10.0`. Requires:
- ImGui.NET 1.91+
- Silk.NET 2.23 (Input / OpenGL / OpenAL / Windowing / OpenGL.Extensions.ImGui)
- Silk.NET.OpenAL.Soft.Native 1.21+
- StbImageSharp 2.30+
