using System.IO;
using System.Numerics;
using BdvEngine;

namespace ValheimLike;

/// <summary>
/// First vertical slice of a Valheim-like survival game on BdvEngine's 3D path.
///
/// What's in this slice:
///  • A procedurally generated island: heightmap terrain (beach → meadow →
///    forest → mountain → snow) carved out of fBM noise with a radial ocean mask.
///  • A translucent water plane at sea level.
///  • Trees instanced from ONE pine.prefab.json — edit that file, every tree changes.
///  • Boulders placed by terrain band + density noise.
///  • A player on a real CharacterController: capsule vs TerrainCollider, with
///    gravity, slopes and step-up. No hand-rolled grounding.
///  • Orbit camera kept out of geometry by a raycast, not a list of spheres.
///  • A day/night cycle driving the sun direction, light colour, ambient and sky.
///
/// Collision layers separate the two questions a tree has to answer:
///   layer 1 (Solid)  — terrain, trunks, rocks: blocks the player AND the camera
///   layer 2 (Canopy) — leaf blobs: blocks the CAMERA only, so a forest stays walkable
///
/// Deliberately NOT here yet: harvesting/inventory, building, enemies, stamina.
/// </summary>
public sealed class ValheimGame : Game
{
    // ---- world constants ----
    private const int   Resolution = 192;   // terrain vertices per side
    private const float CellSize   = 1.1f;  // world units between vertices
    private const float WaterLevel = 0f;
    private const float DayLength  = 120f;  // seconds for a full day/night cycle

    private readonly Noise _heightNoise   = new(1337);
    private readonly Noise _detailNoise   = new(9001);
    private readonly Noise _forestNoise   = new(4242);
    private readonly SeededRng _rng       = new(7);

    private HeightmapTerrain _terrain = null!;

    /// <summary>Blocks the player and the camera.</summary>
    private const int LayerSolid = 1;
    /// <summary>Blocks the camera only — tree canopies, so forests stay walkable.</summary>
    private const int LayerCanopy = 2;

    private const string PinePrefab = "assets/pine.prefab.json";

    // shared meshes (one GPU buffer each, reused across every instance)
    private Mesh _sphere = null!;
    private Mesh _plane = null!;

    private SimObject _player = null!;
    private CharacterController _playerController = null!;
    private int _nextId = 100;

    // ---- player / camera state ----
    private Vector3 _playerPos;
    private float _camYaw = 0f;
    private float _camPitch = 0.42f;
    private float _camDist = 12f;
    private Vector2 _lastMouse;
    private bool _haveMouse;
    private double _elapsed = DayLength * 0.25; // start at midday
    private int _frame;

    public override void Init()
    {
        // The default 0.1 / 1000 near-far is a 10000:1 ratio — across a 200-unit
        // world that starves the depth buffer and makes surfaces z-fight / clip
        // through each other. Tighten it to this world's actual scale.
        Camera.Perspective(fovDegrees: 45f, near: 0.3f, far: 480f);
        Camera.Near = 0.3f;
        Camera.Far = 480f;

        _sphere = Mesh.Sphere(16, 12);
        _plane = Mesh.Plane(1f);

        // Shadows follow the camera, so the map only needs to cover what's on screen — a box
        // spanning the whole 210-unit island would put every texel somewhere nobody is looking.
        World.Environment.Shadows.Distance = 42f;
        World.Environment.Shadows.Bias = 0.0022f;

        // A fresh physics world: this is a static registry, so a re-run would otherwise inherit
        // the previous island's colliders.
        PhysicsWorld.Clear();

        BuildPalette();
        BuildTerrain();
        BuildWater();
        ScatterTrees();
        ScatterRocks();

        // Colliders read their owner's world matrix, which is identity until the first rebake —
        // so spawn queries below would see every collider stacked at the origin without this.
        World.Scene.RebakeMatrices();
        BuildPlayer();

        // No _scene.Load() — the engine loads the World's scene after Init.
        BuildUi();

        MaybeBake();
    }

    /// <summary>Opt-in scene bake (Phase 1 of the authoring-workflow plan): run with
    /// <c>--bake &lt;path&gt;</c> to write the generated island's placed content out as a
    /// <c>.scene.json</c> you can then hand-edit or reload. The terrain itself stays procedural —
    /// it's regenerated from noise every run, so it belongs in code, not in the file.</summary>
    private void MaybeBake()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--bake");
        if (i < 0 || i + 1 >= args.Length) return;
        World.SaveScene(args[i + 1]);
    }

    // ---------------------------------------------------------------- palette
    private static void BuildPalette()
    {
        Materials3D.Solid("trunk",      new Color(96, 64, 38));
        Materials3D.Solid("leaves",     new Color(48, 90, 52));
        Materials3D.Solid("leavesDark", new Color(38, 72, 44));
        Materials3D.Solid("rock",       new Color(122, 120, 116));
        Materials3D.Solid("water",      new Color(34, 84, 132, 224));
        Materials3D.Solid("playerBody", new Color(70, 96, 150));
        Materials3D.Solid("playerHead", new Color(226, 198, 168));
    }

    // ---------------------------------------------------------------- terrain
    private float HeightAt(float wx, float wz)
    {
        // Base elevation from layered value noise.
        float nx = (wx + 1000f) * 0.012f;
        float nz = (wz + 1000f) * 0.012f;
        float e = _heightNoise.Fbm(nx, nz, 5);          // 0..1

        // Radial island mask: high inland, ocean toward the edges.
        float worldHalf = (Resolution - 1) * CellSize * 0.5f;
        float d = MathF.Sqrt(wx * wx + wz * wz) / worldHalf;   // 0 centre .. 1 edge
        float mask = Math.Clamp(1.12f - d * 1.45f, 0f, 1f);

        float hNorm = e * mask;
        return (hNorm - 0.30f) * 42f;                    // sea floor < 0 < mountains
    }

    private Color ColorAt(float wx, float wz, float h)
    {
        // A little per-vertex variation so bands don't look flat.
        float v = _detailNoise.Fbm((wx + 500f) * 0.08f, (wz + 500f) * 0.08f, 3); // 0..1
        int j = (int)((v - 0.5f) * 22);

        Color band;
        if (h < WaterLevel + 0.8f)      band = new Color(196, 180, 132); // sand / beach
        else if (h < 7f)               band = new Color(86, 132, 70);   // meadow
        else if (h < 14f)              band = new Color(54, 92, 56);    // forest
        else if (h < 19f)              band = new Color(116, 112, 106); // rock
        else                           band = new Color(236, 240, 244); // snow

        return new Color(
            (byte)Math.Clamp(band.R + j, 0, 255),
            (byte)Math.Clamp(band.G + j, 0, 255),
            (byte)Math.Clamp(band.B + j, 0, 255));
    }

    private void BuildTerrain()
    {
        _terrain = new HeightmapTerrain(Resolution, CellSize, HeightAt, ColorAt, "terrain");
        // The terrain is a single-sided mesh (top faces only). Mark just its material two-sided so
        // the third-person camera never sees through a ridge — while trees/rocks/player still cull.
        MaterialManager.Get("terrain").DoubleSided = true;
        var obj = _terrain.CreateObject(_nextId++, "terrain");
        // The heightfield IS the collision surface — no separate approximation, and no per-frame
        // SampleHeight in game code any more.
        obj.AddComponent(new TerrainCollider(_terrain) { Layer = LayerSolid });
        World.Scene.AddObject(obj);
    }

    private void BuildWater()
    {
        var water = new SimObject(_nextId++, "water");
        water.Transform.Position = new Vector3(0, WaterLevel, 0);
        water.Transform.Scale = new Vector3(_terrain.WorldSize, 1f, _terrain.WorldSize);
        water.AddComponent(new MeshComponent(_plane, "water"));
        World.Scene.AddObject(water);
    }

    // ---------------------------------------------------------------- props
    private void ScatterTrees()
    {
        EnsurePinePrefab();

        float half = _terrain.WorldSize * 0.5f - 4f;
        int planted = 0;
        const int maxTrees = 340;

        for (float z = -half; z < half && planted < maxTrees; z += 3.2f)
        for (float x = -half; x < half && planted < maxTrees; x += 3.2f)
        {
            float jx = x + ((float)_rng.Next() - 0.5f) * 2.6f;
            float jz = z + ((float)_rng.Next() - 0.5f) * 2.6f;
            float h = _terrain.SampleHeight(jx, jz);
            if (h < 1.2f || h > 13f) continue;           // meadow + forest bands only

            float density = _forestNoise.Fbm((jx + 200f) * 0.05f, (jz + 200f) * 0.05f, 3);
            if (density < 0.52f) continue;
            if (_rng.Next() > density) continue;

            PlantTree(jx, h, jz);
            planted++;
        }
    }

    /// <summary>Write the pine asset if it isn't there yet, so the example is self-contained and
    /// the file is a real artifact you can hand-edit. Delete it and it regenerates; edit it and
    /// every tree on the island changes on the next run.</summary>
    private void EnsurePinePrefab()
    {
        if (File.Exists(PinePrefab)) return;

        // The root is deliberately UNSCALED. An instance's transform replaces the prefab root's,
        // so a root carrying a scale would have its children authored against a squash that every
        // instance then throws away — and they'd come out distorted.
        var root = new SimObject(_nextId++, "pine");

        var trunk = new SimObject(_nextId++, "trunk");
        trunk.Transform.Position = new Vector3(0, 1.4f, 0);
        trunk.Transform.Scale = new Vector3(0.34f, 2.8f, 0.34f);
        trunk.AddComponent(new MeshComponent(Primitives.Cube().Mesh, "trunk"));
        root.AddChild(trunk);

        // Two stacked foliage blobs to suggest a conifer.
        var lower = new SimObject(_nextId++, "canopy_lo");
        lower.Transform.Position = new Vector3(0, 3.0f, 0);
        lower.Transform.Scale = new Vector3(2.4f, 2.2f, 2.4f);
        lower.AddComponent(new MeshComponent(Primitives.Sphere(16, 12).Mesh, "leavesDark"));
        root.AddChild(lower);

        var upper = new SimObject(_nextId++, "canopy_hi");
        upper.Transform.Position = new Vector3(0, 4.4f, 0);
        upper.Transform.Scale = new Vector3(1.7f, 1.8f, 1.7f);
        upper.AddComponent(new MeshComponent(Primitives.Sphere(16, 12).Mesh, "leaves"));
        root.AddChild(upper);

        World.SavePrefab(PinePrefab, root);
    }

    private void PlantTree(float x, float groundY, float z)
    {
        float scale = 0.8f + (float)_rng.Next() * 0.9f;
        var tree = World.Instantiate(PinePrefab).At(x, groundY, z).Scale(scale).Object;
        tree.Name = "tree";

        // Colliders aren't part of the scene format yet, so they're attached here rather than
        // living in the prefab file. Two shapes because a tree answers two different questions:
        // the trunk stops you walking through it, the canopy only stops the camera — otherwise a
        // 2.7-unit leaf ball at ground level would make the whole forest impassable.
        tree.AddComponent(new CapsuleCollider(0.45f, 2.8f, new Vector3(0, 1.4f, 0))
        { Layer = LayerSolid });
        tree.AddComponent(new SphereCollider(2.7f, new Vector3(0, 3.6f, 0))
        { Layer = LayerCanopy });
    }

    private void ScatterRocks()
    {
        float half = _terrain.WorldSize * 0.5f - 4f;
        int placed = 0;
        const int maxRocks = 90;

        while (placed < maxRocks)
        {
            float x = ((float)_rng.Next() - 0.5f) * 2f * half;
            float z = ((float)_rng.Next() - 0.5f) * 2f * half;
            float h = _terrain.SampleHeight(x, z);
            if (h < 1f || h > 20f) continue;

            float s = 0.6f + (float)_rng.Next() * 1.8f;
            var rock = new SimObject(_nextId++, "rock");
            rock.Transform.Position = new Vector3(x, h + s * 0.25f, z);
            rock.Transform.Scale = new Vector3(s, s * 0.7f, s * 0.9f);
            rock.AddComponent(new MeshComponent(_sphere, "rock"));
            rock.AddComponent(new SphereCollider(0.5f) { Layer = LayerSolid });
            World.Scene.AddObject(rock);
            placed++;
        }
    }

    // ---------------------------------------------------------------- player
    private void BuildPlayer()
    {
        var spawn = FindLandSpawn();
        _playerPos = new Vector3(spawn.X, GroundFor(spawn.X, spawn.Y), spawn.Y);

        _player = new SimObject(_nextId++, "player");
        _player.Transform.Position = _playerPos;

        // Feet at the object's origin, so the visual body and the capsule agree.
        var capsule = new CapsuleCollider(0.4f, 1.8f, new Vector3(0, 0.9f, 0)) { Layer = LayerSolid };
        _player.AddComponent(capsule);
        _playerController = new CharacterController(capsule)
        {
            // Solid only: the player walks through canopies, the camera does not.
            CollisionMask = LayerSolid,
            StepOffset = 0.5f,          // generous, so terrain seams and small rocks don't snag
            SlopeLimitDegrees = 52f,    // steeper than this is a mountain face, not a hill
        };
        _player.AddComponent(_playerController);

        var body = new SimObject(_nextId++, "body");
        body.Transform.Position = new Vector3(0, 0.7f, 0);
        body.Transform.Scale = new Vector3(0.6f, 1.2f, 0.42f);
        body.AddComponent(new MeshComponent(Primitives.Cube().Mesh, "playerBody"));
        _player.AddChild(body);

        var head = new SimObject(_nextId++, "head");
        head.Transform.Position = new Vector3(0, 1.6f, 0);
        head.Transform.Scale = new Vector3(0.55f, 0.55f, 0.55f);
        head.AddComponent(new MeshComponent(_sphere, "playerHead"));
        _player.AddChild(head);

        World.Scene.AddObject(_player);
    }

    /// <summary>Surface the player stands on — terrain, but never below the
    /// waterline (so they wade on the surface rather than sink).</summary>
    private float GroundFor(float x, float z)
        => MathF.Max(_terrain.SampleHeight(x, z), WaterLevel);

    /// <summary>Spiral out from the origin to the nearest open meadow clearing —
    /// dry land with land in every direction and no tree crowding the camera —
    /// so the player starts somewhere comfortable, not at a shoreline or buried
    /// in the forest.</summary>
    private Vector2 FindLandSpawn()
    {
        const float minH = 4f, maxH = 9f;
        bool haveFallback = false;
        Vector2 fallback = Vector2.Zero;

        for (float r = 0; r < _terrain.WorldSize * 0.45f; r += 5f)
        for (float a = 0; a < MathF.Tau; a += MathF.PI / 12f)
        {
            float x = MathF.Cos(a) * r, z = MathF.Sin(a) * r;
            float h = _terrain.SampleHeight(x, z);
            if (h < minH || h > maxH) continue;

            // Solid land in all 8 directions (not a shoreline spit).
            bool surrounded = true;
            for (int k = 0; k < 8 && surrounded; k++)
            {
                float aa = k * MathF.PI / 4f;
                if (_terrain.SampleHeight(x + MathF.Cos(aa) * 12f, z + MathF.Sin(aa) * 12f) < 1.5f)
                    surrounded = false;
            }
            if (!surrounded) continue;

            if (!haveFallback) { fallback = new Vector2(x, z); haveFallback = true; }

            // Prefer a clearing: no tree canopy within camera range. The canopy layer already
            // means "things that crowd the camera", so the query is exactly that question.
            // An overlap query counts the canopy's own radius too (up to ~4.6), so this asks for
            // roughly the same breathing room the old centre-distance test did.
            var probe = new Vector3(x, _terrain.SampleHeight(x, z) + 3.5f, z);
            if (PhysicsWorld.OverlapSphere(probe, 4.5f, LayerCanopy).Count == 0)
                return new Vector2(x, z);
        }
        return haveFallback ? fallback : Vector2.Zero;
    }

    // ---------------------------------------------------------------- update
    public override void Update(double deltaTime)
    {
        float dt = (float)deltaTime;
        _elapsed += deltaTime;

        UpdateCameraInput(dt);
        UpdateMovement(dt);   // the CharacterController writes _player.Transform itself
        UpdateDayNight();

        PositionCamera();   // engine pumps World.Scene.Update + rebakes matrices before rendering

        // Headless verify hook: UNIFIED_SHOT=<path.ppm> captures a frame then exits.
        if (System.Environment.GetEnvironmentVariable("UNIFIED_SHOT") is { } shot)
        {
            _frame++;
            if (_frame == 60) Screenshot.PendingPath = shot;
            else if (_frame == 75) System.Environment.Exit(0);
        }
    }

    private void UpdateCameraInput(float dt)
    {
        var mouse = InputManager.GetMousePosition();
        if (!_haveMouse) { _lastMouse = mouse; _haveMouse = true; }
        Vector2 mDelta = mouse - _lastMouse;
        _lastMouse = mouse;

        if (InputManager.IsLeftDown)
        {
            _camYaw   -= mDelta.X * 0.006f;
            _camPitch -= mDelta.Y * 0.006f;
        }
        // Arrow keys also orbit, so it's usable without dragging.
        if (InputManager.IsKeyDown(Key.Left))  _camYaw   += dt * 1.6f;
        if (InputManager.IsKeyDown(Key.Right))  _camYaw  -= dt * 1.6f;
        if (InputManager.IsKeyDown(Key.Up))     _camPitch += dt * 1.2f;
        if (InputManager.IsKeyDown(Key.Down))   _camPitch -= dt * 1.2f;
        _camPitch = Math.Clamp(_camPitch, 0.12f, 1.45f);

        float wheel = InputManager.ConsumeWheelDelta();
        if (wheel != 0) _camDist = Math.Clamp(_camDist - wheel * 1.5f, 4f, 24f);
    }

    private void UpdateMovement(float dt)
    {
        // Forward = the camera's horizontal look direction.
        var fwd = new Vector3(-MathF.Sin(_camYaw), 0, -MathF.Cos(_camYaw));
        var right = new Vector3(fwd.Z, 0, -fwd.X);

        var move = Vector3.Zero;
        if (InputManager.IsKeyDown(Key.W)) move += fwd;
        if (InputManager.IsKeyDown(Key.S)) move -= fwd;
        if (InputManager.IsKeyDown(Key.D)) move += right;
        if (InputManager.IsKeyDown(Key.A)) move -= right;

        float speed = InputManager.IsKeyDown(Key.ShiftLeft) ? 16f : 8f;
        var velocity = move == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(move) * speed;

        if (move != Vector3.Zero)
            _player.Transform.Rotation = new Vector3(0, MathF.Atan2(move.X, move.Z), 0);

        if (InputManager.IsKeyDown(Key.Space)) _playerController.Jump(7.5f);

        // The controller owns position now: gravity, slopes, step-up and collision all happen
        // here instead of the old "snap Y to the heightfield every frame".
        _playerController.Move(velocity, dt);
        _playerPos = _player.Transform.Position;

        // Wading is a game rule, not physics: the terrain continues below sea level, and without
        // this the player would walk along the sea floor.
        float half = _terrain.WorldSize * 0.5f - 2f;
        _playerPos.X = Math.Clamp(_playerPos.X, -half, half);
        _playerPos.Z = Math.Clamp(_playerPos.Z, -half, half);
        if (_playerPos.Y < WaterLevel) _playerPos.Y = WaterLevel;
        _player.Transform.Position = _playerPos;
    }

    private void PositionCamera()
    {
        var target = _playerPos + new Vector3(0, 1.5f, 0);
        float cp = MathF.Cos(_camPitch);
        // Unit vector from the player out to the camera.
        var dir = new Vector3(cp * MathF.Sin(_camYaw), MathF.Sin(_camPitch), cp * MathF.Cos(_camYaw));

        // One raycast from the player outward replaces the old stepped march plus the
        // hand-maintained list of canopy spheres. Solid AND canopy both block: the camera should
        // not sit inside a ridge or a tree, even though the player may walk through leaves.
        // Clearance keeps the surface off the near plane, which otherwise slices open.
        const float clearance = 1.6f;
        float allowed = _camDist;

        if (PhysicsWorld.Raycast(target, dir, _camDist, out var hit,
                                 layerMask: LayerSolid | LayerCanopy, ignore: _playerController.Capsule))
            allowed = MathF.Max(hit.Distance - clearance, 0f);

        allowed = MathF.Max(allowed, 4f);
        var camPos = target + dir * allowed;

        // Final guard: a ray grazing a ridge can still land the camera just under the surface, and
        // back-face culling would render the world see-through from in there.
        float floorY = GroundFor(camPos.X, camPos.Z) + clearance;
        if (camPos.Y < floorY) camPos.Y = floorY;

        Camera.Position = camPos;
        Camera.Target = target;
    }

    // ---------------------------------------------------------------- day/night
    private void UpdateDayNight()
    {
        float t = (float)((_elapsed % DayLength) / DayLength); // 0..1
        float ang = t * MathF.Tau;

        // Sun arcs across the sky; +Y = above horizon.
        var sunPos = Vector3.Normalize(new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0.35f));
        float day = Math.Clamp(sunPos.Y, 0f, 1f);                 // 0 night .. 1 noon
        float horizon = Math.Clamp(1f - MathF.Abs(sunPos.Y) * 3f, 0f, 1f); // sunset glow

        // Sun.Direction is the "travel" direction; the engine flips the sign for the shader.
        var env = World.Environment;
        env.Sun.Direction = -sunPos;

        var noon  = new Vector3(1.0f, 0.97f, 0.88f);
        var dusk  = new Vector3(1.0f, 0.55f, 0.30f);
        var night = new Vector3(0.10f, 0.13f, 0.22f);
        env.Sun.Color = Lerp(Lerp(night, dusk, horizon), noon, day);

        env.Ambient = Lerp(new Vector3(0.10f, 0.12f, 0.20f),
                           new Vector3(0.42f, 0.44f, 0.48f), day);

        var skyDay   = new Vector3(0.45f, 0.66f, 0.92f);
        var skyNight = new Vector3(0.03f, 0.05f, 0.12f);
        var sky = Lerp(skyNight, skyDay, day);
        env.Sky = Lerp(sky, new Vector3(0.85f, 0.45f, 0.28f), horizon * 0.6f);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    // No Render override — the engine walks + renders the World's scene each frame.

    private static void BuildUi()
    {
        var p = UI.Panel(UIAnchor.TopLeft, "Valheim-like");
        UI.Heading(p, "Vertical slice");
        UI.Text(p, "WASD: move   Shift: run   Space: jump");
        UI.Text(p, "Drag mouse / arrows: look");
        UI.Text(p, "Wheel: zoom");
        UI.Text(p, "Day/night cycle: ~120s");
    }
}
