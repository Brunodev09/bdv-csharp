using System.Numerics;
using BdvEngine;

namespace TerrainGameApp;

public sealed class TerrainGame : Game, IMessageHandler
{
    private const int GRASS_START = 0;
    private const int SAND_1 = 9;
    private const int SAND_2 = 10;
    private const int BEACH = 11;
    private const int WATER = 12;
    private const int MT_START = 13;
    private const int MT_COUNT = 16;
    private const int SNOW_START = 29;
    private const int SNOW_COUNT = 4;
    private const int CHAOS_GND_START = 33;
    private const int CHAOS_GND_COUNT = 7;
    private const int FOREST_TREE_1 = 48;
    private const int FOREST_TREE_2 = 49;
    private const int MAGIC_TREE_1 = 50;
    private const int MAGIC_TREE_2 = 51;
    private const int CHAOS_TREE = 52;
    private const int SNOW_TREE_1 = 53;
    private const int BUSH_START = 64;
    private const int BUSH_COUNT = 16;
    private const int ROAD_TILE = SAND_2;

    private enum Biome { Ocean, Beach, Desert, Grassland, Forest, Mountain, Snow, Chaos, Enchanted }
    private static readonly string[] BiomeNames = { "Ocean", "Beach", "Desert", "Grassland", "Forest", "Mountain", "Snow", "Chaos", "Enchanted" };

    private const int MAP_SIZE = 1024;
    private const int TILE_RENDER_SIZE = 96;
    private const float NOISE_SCALE = 0.006f;
    private const int BLDG_FOOTPRINT = 3;

    private TileSet _tileSet = null!;
    private TileMap _tileMap = null!;
    private TileMap _overlayMap = null!;
    private float[] _heightMap = null!;
    private byte[] _biomeMap = null!;

    private readonly List<(int TileX, int TileY, int Col, int Row)> _buildings = new();
    private readonly HashSet<int> _occupiedTiles = new();
    private readonly HashSet<int> _buildingTiles = new();
    private Scene _humanScene = new();

    private int _seed = 54321;
    private string _seedInput = "54321";
    private int _hoverTileX = -1, _hoverTileY = -1;
    private int _selectedTileX = -1, _selectedTileY = -1;
    private float _camSpeed = 600f;

    public override void Init()
    {
        Camera.X = MAP_SIZE * TILE_RENDER_SIZE / 2f;
        Camera.Y = MAP_SIZE * TILE_RENDER_SIZE / 2f;
        Camera.Zoom = 0.05f;

        _tileSet = new TileSet("terrain_tiles", "textures/terrain.png", 96, 96);
        var lod = new TileSet("terrain_lod", "textures/terrain_lod.png", 16, 16);

        _tileMap = new TileMap(_tileSet, MAP_SIZE, MAP_SIZE, TILE_RENDER_SIZE) { LodTileSet = lod };
        _overlayMap = new TileMap(_tileSet, MAP_SIZE, MAP_SIZE, TILE_RENDER_SIZE);

        MaterialManager.Register(new Material("human_mat", "textures/human_walking.png", Color.White));
        MaterialManager.Register(new Material("buildings_mat", "textures/buildings_tileset.png", Color.White));

        _heightMap = new float[MAP_SIZE * MAP_SIZE];
        _biomeMap = new byte[MAP_SIZE * MAP_SIZE];

        GenerateWorld(_seed);
        BuildUI();
        Message.Subscribe("MOUSE_DOWN", this);
    }

    public void OnMessage(Message msg)
    {
        if (msg.Code == "MOUSE_DOWN" && _hoverTileX >= 0)
        {
            _selectedTileX = _hoverTileX;
            _selectedTileY = _hoverTileY;
        }
    }

    private void BuildUI()
    {
        var panel = UI.Panel(UIAnchor.TopLeft);
        UI.Heading(panel, "Bdv World");
        UI.Text(panel, $"{MAP_SIZE}x{MAP_SIZE} — WASD + scroll");
        UI.Spacer(panel);

        UI.Input(panel, "Seed", _seedInput, v => _seedInput = v);
        UI.Button(panel, "Go", () =>
        {
            if (int.TryParse(_seedInput, out int v) && v > 0)
            {
                _seed = v;
                GenerateWorld(v);
            }
        });
        UI.Button(panel, "Random", () =>
        {
            _seed = new Random().Next(1, 999999);
            _seedInput = _seed.ToString();
            GenerateWorld(_seed);
        });
        UI.Spacer(panel);
        UI.TextLive(panel, () => $"Zoom: {Camera.Zoom:F3}x  Seed: {_seed}");
        UI.TextLive(panel, () =>
        {
            if (_hoverTileX < 0) return "(move mouse over map)";
            int idx = _tileMap.GetTile(_hoverTileX, _hoverTileY);
            int oi = _overlayMap.GetTile(_hoverTileX, _hoverTileY);
            int b = _biomeMap[_hoverTileY * MAP_SIZE + _hoverTileX];
            string bld = _buildingTiles.Contains(_hoverTileY * MAP_SIZE + _hoverTileX) ? " [Building]" : "";
            string ovl = oi >= 0 ? $" + {TileName(oi)}" : "";
            return $"({_hoverTileX},{_hoverTileY}) {BiomeNames[b]} | {TileName(idx)}{ovl}{bld}";
        });
    }

    private static string TileName(int i)
    {
        if (i >= GRASS_START && i < GRASS_START + 9) return "Grass";
        if (i == SAND_1) return "Sand";
        if (i == SAND_2) return "Road";
        if (i == BEACH) return "Beach";
        if (i == WATER) return "Water";
        if (i >= MT_START && i < MT_START + MT_COUNT) return "Mountain";
        if (i >= SNOW_START && i < SNOW_START + SNOW_COUNT) return "Snow";
        if (i >= CHAOS_GND_START && i < CHAOS_GND_START + CHAOS_GND_COUNT) return "Chaos";
        if (i >= 40 && i <= 47) return "Tree";
        if (i >= BUSH_START && i < BUSH_START + BUSH_COUNT) return "Bush";
        if (i < 0) return "—";
        return $"Tile {i}";
    }

    private void GenerateWorld(int seed)
    {
        var noise = new Noise(seed);
        var rng = new SeededRng(seed);
        _overlayMap.Fill(-1);
        _buildings.Clear();
        _occupiedTiles.Clear();
        _buildingTiles.Clear();

        for (int y = 0; y < MAP_SIZE; y++)
        for (int x = 0; x < MAP_SIZE; x++)
        {
            float dx = (x / (float)MAP_SIZE - 0.5f) * 2f;
            float dy = (y / (float)MAP_SIZE - 0.5f) * 2f;
            float island = 1f - MathF.Min(1f, (dx * dx + dy * dy) * 0.8f);
            float h = noise.Fbm(x * NOISE_SCALE, y * NOISE_SCALE, 6) * island;
            h = h * 0.85f + 0.15f;
            float lat = MathF.Abs(dy);
            if (lat > 0.7f && h > 0.30f)
                h += MathF.Pow((lat - 0.7f) / 0.3f, 2) * (1f - h) * 0.8f;
            _heightMap[y * MAP_SIZE + x] = h;
        }

        var landTiles = new List<(int X, int Y)>();
        for (int y = 30; y < MAP_SIZE - 30; y += 3)
        for (int x = 30; x < MAP_SIZE - 30; x += 3)
        {
            float h = _heightMap[y * MAP_SIZE + x];
            if (h > 0.42f && h < 0.78f) landTiles.Add((x, y));
        }
        for (int i = landTiles.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i);
            (landTiles[i], landTiles[j]) = (landTiles[j], landTiles[i]);
        }
        int chaosCx = landTiles.Count > 0 ? landTiles[0].X : MAP_SIZE / 3;
        int chaosCy = landTiles.Count > 0 ? landTiles[0].Y : MAP_SIZE / 3;
        int chaosR = rng.NextInt(25, 45);
        int enchCx = MAP_SIZE / 2, enchCy = MAP_SIZE / 2;
        float bestDist = 0;
        foreach (var (lx, ly) in landTiles)
        {
            float d = MathF.Sqrt((lx - chaosCx) * (lx - chaosCx) + (ly - chaosCy) * (ly - chaosCy));
            if (d > bestDist) { bestDist = d; enchCx = lx; enchCy = ly; }
        }
        int enchR = rng.NextInt(20, 35);

        var varNoise = new Noise(seed + 500);
        int SnowTileF(int x, int y) => SNOW_START + (int)(varNoise.Get(x * 0.3f + 100, y * 0.3f + 100) * SNOW_COUNT) % SNOW_COUNT;
        int ChaosTileF(int x, int y) => CHAOS_GND_START + (int)(varNoise.Get(x * 0.3f + 200, y * 0.3f + 200) * CHAOS_GND_COUNT) % CHAOS_GND_COUNT;

        for (int y = 0; y < MAP_SIZE; y++)
        for (int x = 0; x < MAP_SIZE; x++)
        {
            float h = _heightMap[y * MAP_SIZE + x];
            float lat = MathF.Abs((y / (float)MAP_SIZE - 0.5f) * 2f);
            float chD = MathF.Sqrt((x - chaosCx) * (x - chaosCx) + (y - chaosCy) * (y - chaosCy));
            float eD = MathF.Sqrt((x - enchCx) * (x - enchCx) + (y - enchCy) * (y - enchCy));

            Biome biome; int tile;
            if (h < 0.38f) { biome = Biome.Ocean; tile = WATER; }
            else if (chD < chaosR && h > 0.38f) { biome = Biome.Chaos; tile = ChaosTileF(x, y); }
            else if (eD < enchR && h > 0.38f) { biome = Biome.Enchanted; tile = GRASS_START; }
            else if (h > 0.85f || (lat > 0.78f && h > 0.35f)) { biome = Biome.Snow; tile = SnowTileF(x, y); }
            else if (h > 0.65f) { biome = Biome.Mountain; tile = GRASS_START; }
            else if (h > 0.55f) { biome = Biome.Forest; tile = GRASS_START; }
            else { biome = Biome.Grassland; tile = GRASS_START; }

            _biomeMap[y * MAP_SIZE + x] = (byte)biome;
            _tileMap.SetTile(x, y, tile);
        }

        // Rivers
        int riverCount = rng.NextInt(6, 12);
        for (int ri = 0; ri < riverCount; ri++)
        {
            int sx = 0, sy = 0; bool found = false;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                sx = rng.NextInt(20, MAP_SIZE - 20);
                sy = rng.NextInt(20, MAP_SIZE - 20);
                float h = _heightMap[sy * MAP_SIZE + sx];
                if (h > 0.65f && h < 0.85f) { found = true; break; }
            }
            if (!found) continue;

            int rx = sx, ry = sy;
            for (int step = 0; step < MAP_SIZE * 2; step++)
            {
                float h = _heightMap[ry * MAP_SIZE + rx];
                if (h < 0.35f) break;
                _tileMap.SetTile(rx, ry, WATER);
                _biomeMap[ry * MAP_SIZE + rx] = (byte)Biome.Ocean;

                float bestH = h; int bestX = rx, bestY = ry;
                for (int ddy = -1; ddy <= 1; ddy++)
                for (int ddx = -1; ddx <= 1; ddx++)
                {
                    if (ddx == 0 && ddy == 0) continue;
                    int nx = rx + ddx, ny = ry + ddy;
                    if ((uint)nx >= MAP_SIZE || (uint)ny >= MAP_SIZE) continue;
                    float nh = _heightMap[ny * MAP_SIZE + nx];
                    if (nh < bestH) { bestH = nh; bestX = nx; bestY = ny; }
                }
                if (bestX == rx && bestY == ry)
                {
                    rx = Math.Clamp(rx + rng.NextInt(-1, 1), 1, MAP_SIZE - 2);
                    ry = Math.Clamp(ry + rng.NextInt(-1, 1), 1, MAP_SIZE - 2);
                }
                else { rx = bestX; ry = bestY; }
            }
        }

        // Beaches
        for (int y = 1; y < MAP_SIZE - 1; y++)
        for (int x = 1; x < MAP_SIZE - 1; x++)
        {
            if (_biomeMap[y * MAP_SIZE + x] == (byte)Biome.Ocean) continue;
            if (_biomeMap[(y - 1) * MAP_SIZE + x] == (byte)Biome.Ocean
                || _biomeMap[(y + 1) * MAP_SIZE + x] == (byte)Biome.Ocean
                || _biomeMap[y * MAP_SIZE + x - 1] == (byte)Biome.Ocean
                || _biomeMap[y * MAP_SIZE + x + 1] == (byte)Biome.Ocean)
            {
                _biomeMap[y * MAP_SIZE + x] = (byte)Biome.Beach;
                _tileMap.SetTile(x, y, BEACH);
            }
        }

        // Overlay
        for (int y = 0; y < MAP_SIZE; y++)
        for (int x = 0; x < MAP_SIZE; x++)
        {
            var b = (Biome)_biomeMap[y * MAP_SIZE + x];
            float r = (float)rng.Next();
            switch (b)
            {
                case Biome.Forest:
                    if (r < 0.15f) _overlayMap.SetTile(x, y, rng.Next() > 0.5 ? FOREST_TREE_1 : FOREST_TREE_2);
                    else if (r < 0.18f) _overlayMap.SetTile(x, y, BUSH_START + rng.NextInt(0, 7));
                    break;
                case Biome.Enchanted:
                    if (r < 0.20f) _overlayMap.SetTile(x, y, rng.Next() > 0.5 ? MAGIC_TREE_1 : MAGIC_TREE_2);
                    else if (r < 0.25f) _overlayMap.SetTile(x, y, BUSH_START + rng.NextInt(8, 15));
                    break;
                case Biome.Chaos:
                    if (r < 0.12f) _overlayMap.SetTile(x, y, CHAOS_TREE);
                    else if (r < 0.16f) _overlayMap.SetTile(x, y, BUSH_START + rng.NextInt(0, BUSH_COUNT - 1));
                    break;
                case Biome.Snow:
                    if (r < 0.06f) _overlayMap.SetTile(x, y, SNOW_TREE_1 + rng.NextInt(0, 2));
                    break;
                case Biome.Mountain:
                    if (r < 0.10f) _overlayMap.SetTile(x, y, MT_START + rng.NextInt(0, MT_COUNT - 1));
                    else if (r < 0.13f) _overlayMap.SetTile(x, y, BUSH_START + rng.NextInt(0, BUSH_COUNT - 1));
                    break;
                case Biome.Grassland:
                    if (r < 0.02f) _overlayMap.SetTile(x, y, rng.Next() > 0.5 ? FOREST_TREE_1 : FOREST_TREE_2);
                    else if (r < 0.03f) _overlayMap.SetTile(x, y, BUSH_START + rng.NextInt(0, 7));
                    break;
            }
        }

        // Cities + buildings
        var cities = new List<(int X, int Y, int Size)>();
        int cityCount = rng.NextInt(15, 30);
        const int cityMinDist = 60;
        for (int attempt = 0; attempt < cityCount * 50 && cities.Count < cityCount; attempt++)
        {
            int cx = rng.NextInt(30, MAP_SIZE - 30);
            int cy = rng.NextInt(30, MAP_SIZE - 30);
            var b = (Biome)_biomeMap[cy * MAP_SIZE + cx];
            if (b != Biome.Grassland && b != Biome.Forest) continue;
            bool tooClose = false;
            foreach (var c in cities)
                if (MathF.Sqrt((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy)) < cityMinDist)
                { tooClose = true; break; }
            if (tooClose) continue;
            cities.Add((cx, cy, rng.NextInt(3, 7)));
        }

        bool PlaceBuilding(int bx, int by, int col, int row)
        {
            if (bx < 1 || by < 1 || bx + BLDG_FOOTPRINT >= MAP_SIZE || by + BLDG_FOOTPRINT >= MAP_SIZE) return false;
            for (int ddy = 0; ddy < BLDG_FOOTPRINT; ddy++)
            for (int ddx = 0; ddx < BLDG_FOOTPRINT; ddx++)
            {
                int tx = bx + ddx, ty = by + ddy;
                if (_occupiedTiles.Contains(ty * MAP_SIZE + tx)) return false;
                var tb = (Biome)_biomeMap[ty * MAP_SIZE + tx];
                if (tb == Biome.Ocean || tb == Biome.Beach) return false;
            }
            _buildings.Add((bx, by, col, row));
            for (int ddy = 0; ddy < BLDG_FOOTPRINT; ddy++)
            for (int ddx = 0; ddx < BLDG_FOOTPRINT; ddx++)
            {
                int tx = bx + ddx, ty = by + ddy;
                _buildingTiles.Add(ty * MAP_SIZE + tx);
                _overlayMap.SetTile(tx, ty, -1);
            }
            for (int ddy = -1; ddy <= BLDG_FOOTPRINT; ddy++)
            for (int ddx = -1; ddx <= BLDG_FOOTPRINT; ddx++)
            {
                int tx = bx + ddx, ty = by + ddy;
                if ((uint)tx >= MAP_SIZE || (uint)ty >= MAP_SIZE) continue;
                _occupiedTiles.Add(ty * MAP_SIZE + tx);
            }
            return true;
        }

        foreach (var city in cities)
        {
            PlaceBuilding(city.X, city.Y, rng.NextInt(0, 7), 3);
            int buildCount = rng.NextInt(4, city.Size * 3);
            for (int bi = 0; bi < buildCount; bi++)
            {
                int dx = rng.NextInt(-city.Size, city.Size);
                int dy = rng.NextInt(-city.Size, city.Size);
                int bx = city.X + (int)MathF.Round(dx / 6f) * 6;
                int by = city.Y + (int)MathF.Round(dy / 6f) * 6;
                if (bx < 2 || bx >= MAP_SIZE - 2 || by < 2 || by >= MAP_SIZE - 2) continue;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int row = dist < city.Size * 0.4f ? rng.NextInt(1, 2) : 0;
                PlaceBuilding(bx, by, rng.NextInt(0, 7), row);
            }
        }

        // Roads connecting cities
        for (int i = 0; i < cities.Count; i++)
        {
            var dists = new List<(int Idx, float D)>();
            for (int j = 0; j < cities.Count; j++)
            {
                if (i == j) continue;
                float dx = cities[i].X - cities[j].X;
                float dy = cities[i].Y - cities[j].Y;
                dists.Add((j, MathF.Sqrt(dx * dx + dy * dy)));
            }
            dists.Sort((a, b) => a.D.CompareTo(b.D));
            if (rng.Next() < 0.3f) continue;
            int connections = rng.NextInt(1, 2);
            for (int c = 0; c < Math.Min(connections, dists.Count); c++)
            {
                if (rng.Next() < 0.3f) continue;
                var target = cities[dists[c].Idx];
                bool horizFirst = rng.Next() > 0.5;
                int x = cities[i].X, y = cities[i].Y;
                int tx = target.X, ty = target.Y;
                void RoadHoriz(int from, int to, int yFixed)
                {
                    int sx = from < to ? 1 : -1;
                    int xx = from;
                    while (xx != to)
                    {
                        if ((Biome)_biomeMap[yFixed * MAP_SIZE + xx] != Biome.Ocean
                            && !_occupiedTiles.Contains(yFixed * MAP_SIZE + xx))
                            _overlayMap.SetTile(xx, yFixed, ROAD_TILE);
                        xx += sx;
                    }
                }
                void RoadVert(int from, int to, int xFixed)
                {
                    int sy2 = from < to ? 1 : -1;
                    int yy = from;
                    while (yy != to)
                    {
                        if ((Biome)_biomeMap[yy * MAP_SIZE + xFixed] != Biome.Ocean
                            && !_occupiedTiles.Contains(yy * MAP_SIZE + xFixed))
                            _overlayMap.SetTile(xFixed, yy, ROAD_TILE);
                        yy += sy2;
                    }
                }
                if (horizFirst) { RoadHoriz(x, tx, y); RoadVert(y, ty, tx); }
                else { RoadVert(y, ty, x); RoadHoriz(x, tx, ty); }
            }
        }

        // Humans
        _humanScene = new Scene();
        int humanId = 0;
        foreach (var city in cities)
        {
            int humanCount = rng.NextInt(3, 8);
            for (int i = 0; i < humanCount; i++)
            {
                int hx = (city.X + rng.NextInt(-city.Size, city.Size)) * TILE_RENDER_SIZE;
                int hy = (city.Y + rng.NextInt(-city.Size, city.Size)) * TILE_RENDER_SIZE;
                var human = new SimObject(humanId++, $"human_{humanId}");
                human.Transform.Position = new Vector3(hx, hy, 0);
                float spriteScale = TILE_RENDER_SIZE / 108f;
                human.Transform.Scale = new Vector3(spriteScale, spriteScale, 1);
                var humanSprite = new AnimatedSpriteComponent(new AnimatedSpriteComponentData
                {
                    Name = "humanSprite",
                    MaterialName = "human_mat",
                    FrameWidth = 108, FrameHeight = 112,
                    FrameCount = 16,
                    FrameSequence = new[] { 0 },
                });
                humanSprite.Sprite.Layer = SpriteLayer.Object;
                human.AddComponent(humanSprite);
                var animState = new StatefulAnimationBehavior(new StatefulAnimationBehaviorData
                {
                    Name = "animState",
                    ComponentName = "humanSprite",
                    FrameTime = 0.12,
                    DefaultState = "idle",
                    States = new Dictionary<string, int[]>
                    {
                        ["idle"] = new[] { 0 },
                        ["walk_right"] = new[] { 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2 },
                        ["walk_left"] = new[] { 8, 9, 10, 11, 12, 13, 14, 13, 12, 11, 10, 9 },
                    },
                });
                human.AddBehavior(animState);
                human.AddBehavior(new WanderBehavior(120f + (float)rng.Next() * 180f, 200f + rng.NextInt(0, 200), animState));
                _humanScene.AddObject(human);
            }
        }
        _humanScene.Load();
    }

    public override void Update(double deltaTime)
    {
        float dt = (float)deltaTime;
        float move = _camSpeed * dt / Camera.Zoom;
        if (InputManager.IsKeyDown(Key.W) || InputManager.IsKeyDown(Key.Up))    Camera.Y -= move;
        if (InputManager.IsKeyDown(Key.S) || InputManager.IsKeyDown(Key.Down))  Camera.Y += move;
        if (InputManager.IsKeyDown(Key.A) || InputManager.IsKeyDown(Key.Left))  Camera.X -= move;
        if (InputManager.IsKeyDown(Key.D) || InputManager.IsKeyDown(Key.Right)) Camera.X += move;

        float wheel = InputManager.ConsumeWheelDelta();
        if (wheel != 0)
        {
            float factor = wheel > 0 ? 1.15f : 1f / 1.15f;
            Camera.Zoom = Math.Clamp(Camera.Zoom * factor, 0.02f, 12f);
        }

        float ws = MAP_SIZE * TILE_RENDER_SIZE;
        Camera.X = Math.Clamp(Camera.X, 0, ws);
        Camera.Y = Math.Clamp(Camera.Y, 0, ws);

        _humanScene.Update(deltaTime);

        var mouse = InputManager.GetMousePosition();
        var world = Camera.ScreenToWorld(mouse.X, mouse.Y, ViewportWidth, ViewportHeight);
        int tx = (int)(world.X / TILE_RENDER_SIZE);
        int ty = (int)(world.Y / TILE_RENDER_SIZE);
        if ((uint)tx < MAP_SIZE && (uint)ty < MAP_SIZE) { _hoverTileX = tx; _hoverTileY = ty; }
        else { _hoverTileX = _hoverTileY = -1; }
    }

    public override void Render(Shader shader)
    {
        ComputeViewBounds(out float vMinX, out float vMinY, out float vMaxX, out float vMaxY);

        _tileMap.Render(Camera, ViewportWidth, ViewportHeight);
        _overlayMap.Render(Camera, ViewportWidth, ViewportHeight);
        RenderBuildings(vMinX, vMinY, vMaxX, vMaxY);
        RenderHumans(shader, vMinX, vMinY, vMaxX, vMaxY);

        float ts = TILE_RENDER_SIZE;
        if (_selectedTileX >= 0)
            Draw.RectOutline(_selectedTileX * ts, _selectedTileY * ts, ts, ts, new Color(255, 255, 0, 255));
        if (_hoverTileX >= 0)
            Draw.RectOutline(_hoverTileX * ts, _hoverTileY * ts, ts, ts, Color.White);

        if (++_frame == 240) Screenshot.PendingPath = "/tmp/terrain.ppm";
    }

    private int _frame;

    private void ComputeViewBounds(out float minX, out float minY, out float maxX, out float maxY)
    {
        float halfW = ViewportWidth  / 2f / Camera.Zoom;
        float halfH = ViewportHeight / 2f / Camera.Zoom;
        minX = Camera.X - halfW;
        minY = Camera.Y - halfH;
        maxX = Camera.X + halfW;
        maxY = Camera.Y + halfH;
    }

    private void RenderBuildings(float vMinX, float vMinY, float vMaxX, float vMaxY)
    {
        if (_buildings.Count == 0) return;
        var mat = MaterialManager.Get("buildings_mat");
        if (mat.DiffuseTexture == null || !mat.DiffuseTexture.IsLoaded) return;

        float ts = TILE_RENDER_SIZE;
        float bSize = ts * BLDG_FOOTPRINT;
        foreach (var b in _buildings)
        {
            float bx = b.TileX * ts;
            float by = b.TileY * ts;
            if (bx + bSize < vMinX || by + bSize < vMinY || bx > vMaxX || by > vMaxY) continue;
            SpriteBatcher.DrawTexture(mat, b.Col, b.Row, 8, 4, bx, by, bSize, bSize,
                layer: SpriteLayer.Object, sortY: by + bSize);
        }
    }

    private void RenderHumans(Shader shader, float vMinX, float vMinY, float vMaxX, float vMaxY)
    {
        const float HUMAN_RADIUS = 120f;
        float minX = vMinX - HUMAN_RADIUS, minY = vMinY - HUMAN_RADIUS;
        float maxX = vMaxX + HUMAN_RADIUS, maxY = vMaxY + HUMAN_RADIUS;
        foreach (var h in _humanScene.Root.Children)
        {
            var p = h.Transform.Position;
            if (p.X < minX || p.Y < minY || p.X > maxX || p.Y > maxY) continue;
            h.Render(shader);
        }
    }
}

internal sealed class WanderBehaviorData : IBehaviorData
{
    public string Name { get; set; } = "wander";
    public float Speed = 60f;
    public float Range = 300f;
    public void SetFromJson(System.Text.Json.JsonElement _) { }
}

internal sealed class WanderBehavior : BaseBehavior
{
    private float _targetX, _targetY;
    private bool _hasTarget;
    private readonly float _speed;
    private readonly float _range;
    private readonly StatefulAnimationBehavior _anim;
    private readonly Random _rng = new();

    public WanderBehavior(float speed, float range, StatefulAnimationBehavior anim)
        : base(new WanderBehaviorData { Speed = speed, Range = range })
    {
        _speed = speed;
        _range = range;
        _anim = anim;
    }

    public override void Update(double deltaTime)
    {
        var p = _owner.Transform.Position;

        if (!_hasTarget)
        {
            _targetX = p.X + ((float)_rng.NextDouble() - 0.5f) * _range * 2;
            _targetY = p.Y + ((float)_rng.NextDouble() - 0.5f) * _range * 2;
            _hasTarget = true;
        }

        float dx = _targetX - p.X;
        float dy = _targetY - p.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 5f) { _hasTarget = false; return; }

        float move = _speed * (float)deltaTime;
        p.X += dx / dist * move;
        p.Y += dy / dist * move;
        _owner.Transform.Position = p;

        _anim.SetState(dx > 0 ? "walk_right" : "walk_left");
    }
}
