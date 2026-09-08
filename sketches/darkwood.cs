#:project ../src/BdvEngine/BdvEngine.csproj
// Darkwood-style prototype: top-down, flat sprites + pseudo-3D (tall, y-sorted trunks/walls that
// reach up the screen), deferred 2D lighting with wall shadow-casting, and a day/night cycle.
//   dotnet run sketches/darkwood.cs                                   (WASD to move)
//   DW_PHASE=0.25 dotnet run sketches/darkwood.cs -- --shot day.png   (0.25 = midday)
//   DW_PHASE=0.80 dotnet run sketches/darkwood.cs -- --shot night.png (0.80 = deep night)
using BdvEngine;
using System;
using System.Numerics;
using System.Collections.Generic;

const int   TILE = 40;
const int   TX = 54, TY = 38;
const float WORLD_W = TX * TILE, WORLD_H = TY * TILE;
const float CYCLE = 40f;                       // seconds per full day/night

var rng      = new Random(7);
var player   = new Vector2(WORLD_W * 0.5f, WORLD_H * 0.5f);
var campfire = new Vector2(WORLD_W * 0.5f + 130, WORLD_H * 0.5f - 30);
var trees    = new List<Vector2>();
var lamps    = new List<Vector2>();
var enemies  = new List<Vector2>();
var wallTiles = new HashSet<(int, int)>();
var occ      = new byte[TX * TY * 4];
var facing   = new Vector2(0, -1);   // vision-cone direction (follows the mouse)
double t = 0;

float phase0 = float.TryParse(Environment.GetEnvironmentVariable("DW_PHASE"), out var pp) ? pp : 0.22f;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Orthographic();
        w.Camera.Zoom = 1.15f;
        w.Environment.Sky = new Vector3(0.01f, 0.012f, 0.02f);

        BuildRuin(14, 12, 9, 6);
        for (int i = 0; i < 85; i++)
        {
            var p = new Vector2((float)rng.NextDouble() * WORLD_W, (float)rng.NextDouble() * WORLD_H);
            if (Vector2.Distance(p, player) < 110 || Vector2.Distance(p, campfire) < 80) continue;
            trees.Add(p);
        }
        lamps.Add(new Vector2(WORLD_W * 0.5f - 210, WORLD_H * 0.5f - 90));
        lamps.Add(new Vector2(WORLD_W * 0.5f + 250, WORLD_H * 0.5f + 150));
        enemies.Add(new Vector2(WORLD_W * 0.5f + 330, WORLD_H * 0.5f - 250));
        enemies.Add(new Vector2(WORLD_W * 0.5f - 360, WORLD_H * 0.5f + 260));

        BuildOccluder();
        t = phase0 * CYCLE;
    },

    update: (w, dt) =>
    {
        t += dt;
        float sp = 165f * (float)dt;
        if (InputManager.IsKeyDown(Key.W)) player.Y -= sp;
        if (InputManager.IsKeyDown(Key.S)) player.Y += sp;
        if (InputManager.IsKeyDown(Key.A)) player.X -= sp;
        if (InputManager.IsKeyDown(Key.D)) player.X += sp;
        player.X = Math.Clamp(player.X, 24, WORLD_W - 24);
        player.Y = Math.Clamp(player.Y, 24, WORLD_H - 24);
        for (int i = 0; i < enemies.Count; i++)
        {
            var d = player - enemies[i];
            if (d.Length() > 2) enemies[i] += Vector2.Normalize(d) * 20f * (float)dt;
        }
        w.Camera.X = player.X;
        w.Camera.Y = player.Y;

        // Aim the vision cone at the mouse (screen → world through the camera).
        var mouse = InputManager.GetMousePosition();
        var mw = w.Camera.ScreenToWorld(mouse.X, mouse.Y, Gfx.WindowWidth, Gfx.WindowHeight);
        var aim = mw - player;
        if (aim.LengthSquared() > 1f) facing = Vector2.Normalize(aim);
    },

    // draw = the scene (all solid sprites, so the light multiply darkens them). The engine flushes
    // this before the hud pass runs.
    draw: w =>
    {
        int vw = Gfx.WindowWidth, vh = Gfx.WindowHeight;
        float hw = vw / 2f / w.Camera.Zoom, hh = vh / 2f / w.Camera.Zoom;
        float minX = w.Camera.X - hw, maxX = w.Camera.X + hw, minY = w.Camera.Y - hh, maxY = w.Camera.Y + hh;

        int gx0 = Math.Max(0, (int)(minX / TILE)), gx1 = Math.Min(TX - 1, (int)(maxX / TILE) + 1);
        int gy0 = Math.Max(0, (int)(minY / TILE)), gy1 = Math.Min(TY - 1, (int)(maxY / TILE) + 1);
        for (int ty = gy0; ty <= gy1; ty++)
            for (int tx = gx0; tx <= gx1; tx++)
            {
                int n = (tx * 7 + ty * 13) % 5;
                SpriteBatcher.DrawSolid(tx * TILE, ty * TILE, TILE + 1, TILE + 1,
                    new Color((byte)(26 + n * 3), (byte)(30 + n * 3), (byte)(23 + n * 2)), SpriteLayer.Ground);
            }

        DrawFire(campfire, (float)t);
        DrawWalls();
        foreach (var tr in trees) DrawTree(tr);
        foreach (var lp in lamps) DrawLamp(lp);
        foreach (var e in enemies) DrawEnemy(e);
        DrawPlayer(player);
    },

    // hud runs AFTER the engine flushed the scene → the light multiply lands on those pixels.
    hud: w =>
    {
        int vw = Gfx.WindowWidth, vh = Gfx.WindowHeight;
        var proj = w.Camera.GetProjection(vw, vh);
        float hw = vw / 2f / w.Camera.Zoom, hh = vh / 2f / w.Camera.Zoom;

        float ph = (float)((t / CYCLE) % 1.0);
        float daylight = Math.Clamp(MathF.Sin(ph * MathF.Tau), 0f, 1f);
        float ambient = 0.05f + 0.62f * daylight;           // grim overcast day .. near-black night
        Lighting.Begin(ambient);

        float torch = 1f + 0.05f * MathF.Sin((float)t * 12f);
        // Vision CONE aimed at the mouse (coneCos 0.55 ≈ a ~113° cone), plus a small personal glow.
        Lighting.AddSpot(player.X, player.Y, 430 * torch, 1.0f, 0.85f, 0.55f, facing.X, facing.Y, 0.55f);
        Lighting.AddLight(player.X, player.Y, 95, 0.5f, 0.42f, 0.30f);                    // personal space
        float fire = 1f + 0.14f * MathF.Sin((float)t * 18f);
        Lighting.AddLight(campfire.X, campfire.Y - 6, 320 * fire, 1.2f, 0.66f, 0.30f);   // campfire
        foreach (var lp in lamps) Lighting.AddLight(lp.X, lp.Y - 30, 175, 0.95f, 0.78f, 0.52f);

        Lighting.Render(proj, w.Camera.X - hw, w.Camera.Y - hh, w.Camera.X + hw, w.Camera.Y + hh);
    }
);

// ---------------------------------------------------------------- helpers

void BuildRuin(int ox, int oy, int wt, int ht)
{
    for (int x = 0; x < wt; x++)
        for (int y = 0; y < ht; y++)
        {
            bool edge = x == 0 || x == wt - 1 || y == 0 || y == ht - 1;
            if (!edge) continue;
            if (y == ht - 1 && x == wt / 2) continue;   // doorway
            if (x == wt - 1 && y > ht / 2) continue;     // ruined corner
            wallTiles.Add((ox + x, oy + y));
        }
}

void BuildOccluder()
{
    for (int i = 0; i < occ.Length; i += 4) { occ[i] = 0; occ[i + 1] = 255; occ[i + 2] = 0; occ[i + 3] = 255; }
    void Mark(int tx, int ty) { if (tx < 0 || ty < 0 || tx >= TX || ty >= TY) return; occ[(ty * TX + tx) * 4] = 255; }
    foreach (var (tx, ty) in wallTiles) Mark(tx, ty);
    foreach (var tr in trees) Mark((int)(tr.X / TILE), (int)(tr.Y / TILE));
    Lighting.SetOccluder(occ, TX, TY, (int)WORLD_W, (int)WORLD_H);
}

// A blocky "round" canopy from stacked solid rows (pixel-art foliage).
void Blob(float cx, float baseY, float w, float h, Color c, float sortY)
{
    int rows = 5;
    for (int i = 0; i < rows; i++)
    {
        float f = MathF.Sin((i + 0.5f) / rows * MathF.PI);   // widest in the middle → rounded
        float rw = w * (0.45f + 0.55f * f);
        float ry = baseY - h + i * (h / rows);
        SpriteBatcher.DrawSolid(cx - rw / 2, ry, rw, h / rows + 1, c, SpriteLayer.Object, sortY);
    }
}

void DrawTree(Vector2 p)
{
    const float trunkH = 64, trunkW = 15;
    SpriteBatcher.DrawSolid(p.X - trunkW / 2, p.Y - trunkH, trunkW, trunkH, new Color(50, 37, 27), SpriteLayer.Object, p.Y);
    SpriteBatcher.DrawSolid(p.X - trunkW / 2, p.Y - trunkH, 4, trunkH, new Color(66, 50, 36), SpriteLayer.Object, p.Y); // rim
    Blob(p.X, p.Y - trunkH + 6, 96, 82, new Color(22, 38, 25), p.Y);   // back foliage
    Blob(p.X - 6, p.Y - trunkH - 4, 70, 62, new Color(32, 52, 34), p.Y); // front foliage
}

void DrawWalls()
{
    const float H = 56;
    foreach (var (tx, ty) in wallTiles)
    {
        float x = tx * TILE, baseY = (ty + 1) * TILE;
        SpriteBatcher.DrawSolid(x, baseY - H, TILE, H, new Color(54, 54, 60), SpriteLayer.Object, baseY);      // face
        SpriteBatcher.DrawSolid(x, baseY - H - 7, TILE, 9, new Color(78, 78, 86), SpriteLayer.Object, baseY);  // cap
        SpriteBatcher.DrawSolid(x, baseY - H, TILE, 4, new Color(30, 30, 34), SpriteLayer.Object, baseY);      // base shade
    }
}

void DrawLamp(Vector2 p)
{
    SpriteBatcher.DrawSolid(p.X - 2, p.Y - 46, 4, 46, new Color(38, 34, 28), SpriteLayer.Object, p.Y);
    SpriteBatcher.DrawSolid(p.X - 5, p.Y - 54, 10, 12, new Color(255, 216, 140), SpriteLayer.Object, p.Y);     // lantern
}

void DrawFire(Vector2 p, float time)
{
    SpriteBatcher.DrawSolid(p.X - 15, p.Y - 4, 30, 8, new Color(36, 27, 20), SpriteLayer.Object, p.Y);          // logs
    float f = 1f + 0.18f * MathF.Sin(time * 20f);
    SpriteBatcher.DrawSolid(p.X - 10, p.Y - 26 * f, 20, 26 * f, new Color(230, 110, 36), SpriteLayer.Object, p.Y);
    SpriteBatcher.DrawSolid(p.X - 6, p.Y - 20 * f, 12, 20 * f, new Color(255, 200, 90), SpriteLayer.Object, p.Y);
    SpriteBatcher.DrawSolid(p.X - 3, p.Y - 12 * f, 6, 12 * f, new Color(255, 245, 190), SpriteLayer.Object, p.Y);
}

void DrawEnemy(Vector2 p)
{
    SpriteBatcher.DrawSolid(p.X - 12, p.Y - 4, 24, 8, new Color(0, 0, 0, 130), SpriteLayer.Object, p.Y - 1);    // shadow
    SpriteBatcher.DrawSolid(p.X - 7, p.Y - 30, 14, 30, new Color(8, 7, 11), SpriteLayer.Object, p.Y);           // body
    SpriteBatcher.DrawSolid(p.X - 6, p.Y - 38, 12, 10, new Color(16, 9, 13), SpriteLayer.Object, p.Y);          // head
    SpriteBatcher.DrawSolid(p.X - 3, p.Y - 34, 2, 2, new Color(200, 45, 45), SpriteLayer.Object, p.Y + 1);      // eyes
    SpriteBatcher.DrawSolid(p.X + 2, p.Y - 34, 2, 2, new Color(200, 45, 45), SpriteLayer.Object, p.Y + 1);
}

void DrawPlayer(Vector2 p)
{
    SpriteBatcher.DrawSolid(p.X - 13, p.Y - 4, 26, 8, new Color(0, 0, 0, 140), SpriteLayer.Object, p.Y - 1);    // shadow
    SpriteBatcher.DrawSolid(p.X - 8, p.Y - 24, 16, 24, new Color(60, 64, 80), SpriteLayer.Object, p.Y);         // cloak
    SpriteBatcher.DrawSolid(p.X - 8, p.Y - 24, 16, 6, new Color(78, 84, 100), SpriteLayer.Object, p.Y);         // shoulders
    SpriteBatcher.DrawSolid(p.X - 6, p.Y - 33, 12, 11, new Color(206, 176, 146), SpriteLayer.Object, p.Y);      // head
    SpriteBatcher.DrawSolid(p.X - 6, p.Y - 33, 12, 4, new Color(48, 44, 52), SpriteLayer.Object, p.Y);          // hood brim
}
