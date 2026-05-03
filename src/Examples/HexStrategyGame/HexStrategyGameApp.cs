using BdvEngine;

namespace HexStrategyGameApp;

public sealed class HexStrategyGame : Game, IMessageHandler
{
    // Spritesheet: 6 cols × 10 rows of 195×~203 cells (1170×2032 image).
    // Tile value = row * 6 + col so the enum matches the user-facing [row,col] mapping.
    // Placeholder cells (24, 46, 47, 52, 53, 58, 59) are intentionally absent.
    private enum Tile : byte
    {
        Grass = 0, GrassFlowers = 1, SparsePine = 2, PineForest = 3, SparseHills = 4, Hills = 5,
        Clearing = 6, SparseForest = 7, Forest = 8, DenseForest = 9, DenseForestClearing = 10, TropicalDenseForest = 11,
        SmallMountains = 12, Mountains = 13, SnowyMountains = 14, DenseSnowyMountains = 15, AridMountains = 16, LavaMountain = 17,
        Desert = 18, CactusDesert = 19, DenseCactusDesert = 20, DesertDunes = 21, DesertRocks = 22, DesertOasis = 23,
        ShallowWaters = 25, Water = 26, DeepWaters = 27, DeepWatersStones = 28, DeepWaterIsland = 29,
        Plains = 30, PlainsCliff = 31, PlainsPlateau = 32, PlainsCliff2 = 33, PlainsCliff3 = 34, PlainsValley = 35,
        SnowStones = 36, SnowBushes = 37, SparseSnowForest = 38, SnowForest = 39, SnowSmallMountains = 40, SnowLargeMountains = 41,
        BlueVillage = 42, BlueCity = 43, BlueCastle = 44, BlueFortress = 45,
        RedVillage = 48, RedCity = 49, RedCastle = 50, RedFortress = 51,
        PurpleVillage = 54, PurpleCity = 55, PurpleCastle = 56, PurpleFortress = 57,
    }

    private const int SHEET_COLS = 6, SHEET_ROWS = 10;
    private const int MAP_W = 128;
    private const int MAP_H = 128;

    // Source 195×203.2 cell holds a pointy-top hex (point at top/bottom, flat on left/right).
    // The inscribed regular hex spans the full cell height and is narrower than the cell
    // width, leaving horizontal padding. Tiling steps must use the actual hex dimensions
    // (not the cell) to tessellate without gaps.
    private const float TILE_W = 120f;
    private const float TILE_H = TILE_W * 203.2f / 195f;
    private static readonly float HEX_H = TILE_H;                  // hex point-to-point spans cell height
    private static readonly float HEX_W = HEX_H * 0.8660254f;      // sqrt(3)/2 — pointy-top hex width
    private static readonly float COL_STEP = HEX_W;
    private static readonly float ROW_STEP = HEX_H * 0.75f;
    private static readonly float ODD_ROW_X = HEX_W * 0.5f;

    private Material _sheet = null!;
    private Tile[] _tiles = null!;
    private float[] _height = null!;
    private float[] _moist = null!;
    private bool[] _waterMask = null!;
    private bool[] _lavaMask = null!;
    // Civilization data lives independently of the biome tile layer. Settlements aren't
    // stamped onto the map — they're abstract entities that drive influence/dominance and
    // (later) feed a separate strongholds-and-units render layer.
    private byte[] _civMap = null!; // 0 = none; otherwise (nation*4 + rank + 1), rank 0=village,1=city,2=castle,3=fortress
    private sbyte[] _domMap = null!; // -1 = unowned, else nation index

    private const int NATION_COUNT = 3;
    // Sprite lookup kept around for the future strongholds layer; currently unread.
    private static readonly Tile[,] CIV_TILES = new Tile[NATION_COUNT, 4]
    {
        { Tile.BlueVillage,   Tile.BlueCity,   Tile.BlueCastle,   Tile.BlueFortress   },
        { Tile.RedVillage,    Tile.RedCity,    Tile.RedCastle,    Tile.RedFortress    },
        { Tile.PurpleVillage, Tile.PurpleCity, Tile.PurpleCastle, Tile.PurpleFortress },
    };
    private static readonly string[] NATION_NAMES = { "Blue", "Red", "Purple" };
    private static readonly string[] RANK_NAMES = { "Village", "City", "Castle", "Fortress" };

    // How far each settlement projects ownership, in hex distance.
    // Index = rank (0 village → 3 fortress).
    private static readonly int[] INFLUENCE = { 3, 5, 8, 12 };

    // Solid hex fill colors for the civilization filter — fully opaque, no texture
    // shows through. Picked saturated enough to scan instantly at a glance.
    private static readonly Color[] NATION_TINTS =
    {
        new Color( 55, 110, 230, 255), // Blue
        new Color(215,  60,  55, 255), // Red
        new Color(160,  70, 200, 255), // Purple
    };
    private static readonly Color UNOWNED_TINT = new Color(8, 8, 12, 255);

    private enum FilterMode { None, Civilization }
    private FilterMode _filter = FilterMode.None;

    // Hex neighbor offsets for odd-r offset coordinates. Rows index by (row & 1).
    private static readonly int[][] HEX_DC =
    {
        new[] { 1, -1,  0, -1,  0, -1 }, // even row
        new[] { 1, -1,  1,  0,  1,  0 }, // odd row
    };
    private static readonly int[] HEX_DR = { 0, 0, -1, -1, 1, 1 };

    private int _seed = 1337;
    private string _seedInput = "1337";
    private float _camSpeed = 800f;
    private int _hoverCol = -1, _hoverRow = -1;
    private int _selCol = -1, _selRow = -1;

    private Font? _font;
    private BdvEngine.Gui.Root _gui = null!;
    private BdvEngine.Gui.Label _previewLabel = null!;
    private BdvEngine.Gui.Image _previewImage = null!;
    private int _previewTile;

    public override void Init()
    {
        _sheet = new Material("hex_sheet", "hex_tileset.png", Color.White);
        MaterialManager.Register(_sheet);

        // Try a project-local font first, then fall back to a system font so the demo
        // shows real text out of the box. Drop your own TTF at assets/font.ttf to use it.
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "font.ttf"),
            "/System/Library/Fonts/Supplemental/Andale Mono.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
        };
        foreach (var p in candidates)
        {
            if (!File.Exists(p)) continue;
            _font = new Font("ui", p, 64);
            FontManager.Register(_font);
            break;
        }

        _tiles = new Tile[MAP_W * MAP_H];
        _height = new float[MAP_W * MAP_H];
        _moist = new float[MAP_W * MAP_H];
        _waterMask = new bool[MAP_W * MAP_H];
        _lavaMask = new bool[MAP_W * MAP_H];
        _civMap = new byte[MAP_W * MAP_H];
        _domMap = new sbyte[MAP_W * MAP_H];
        Generate(_seed);

        BuildGui();

        float worldW = MAP_W * COL_STEP + ODD_ROW_X;
        float worldH = (MAP_H - 1) * ROW_STEP + HEX_H;
        Camera.X = worldW / 2f;
        Camera.Y = worldH / 2f;
        Camera.Zoom = 0.13f;

        BuildUI();
        Message.Subscribe("MOUSE_DOWN", this);
    }

    public void OnMessage(Message msg)
    {
        if (msg.Code == "MOUSE_DOWN" && _hoverCol >= 0)
        {
            _selCol = _hoverCol;
            _selRow = _hoverRow;
        }
    }

    // BuildUI is now empty — all panels live under _gui (built in BuildGui).
    private void BuildUI() { }

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
            Camera.Zoom = Math.Clamp(Camera.Zoom * factor, 0.05f, 6f);
        }

        float worldW = MAP_W * COL_STEP + ODD_ROW_X;
        float worldH = (MAP_H - 1) * ROW_STEP + HEX_H;
        Camera.X = Math.Clamp(Camera.X, 0, worldW);
        Camera.Y = Math.Clamp(Camera.Y, 0, worldH);

        // "Breathing" preview tile — pulse the Image's W/H around its center each frame
        // using the stateless Anim helper. Pure read; no behavior plumbing required.
        const float PREVIEW_BASE_W = 130f, PREVIEW_BASE_H = 132f;
        const float PREVIEW_BASE_X = 14f,  PREVIEW_BASE_Y = 50f;
        float pk = Anim.Pulse(0.92f, 1.06f, period: 1.6f);
        _previewImage.Width  = PREVIEW_BASE_W * pk;
        _previewImage.Height = PREVIEW_BASE_H * pk;
        _previewImage.X = PREVIEW_BASE_X + (PREVIEW_BASE_W - _previewImage.Width)  * 0.5f;
        _previewImage.Y = PREVIEW_BASE_Y + (PREVIEW_BASE_H - _previewImage.Height) * 0.5f;

        _gui.Update(Camera, ViewportWidth, ViewportHeight);

        var mouse = InputManager.GetMousePosition();
        var w = Camera.ScreenToWorld(mouse.X, mouse.Y, ViewportWidth, ViewportHeight);
        var (c, r) = PixelToHex(w.X, w.Y);
        if ((uint)c < MAP_W && (uint)r < MAP_H) { _hoverCol = c; _hoverRow = r; }
        else { _hoverCol = _hoverRow = -1; }
    }

    public override void Render(Shader shader)
    {
        if (_sheet.DiffuseTexture == null || !_sheet.DiffuseTexture.IsLoaded) return;

        float halfW = ViewportWidth  / 2f / Camera.Zoom;
        float halfH = ViewportHeight / 2f / Camera.Zoom;
        float minX = Camera.X - halfW - TILE_W;
        float minY = Camera.Y - halfH - TILE_H;
        float maxX = Camera.X + halfW + TILE_W;
        float maxY = Camera.Y + halfH + TILE_H;

        // Bound visible col/row range — wider span than strictly needed but cheap to over-cull.
        int colStart = Math.Max(0, (int)MathF.Floor(minX / COL_STEP) - 1);
        int colEnd   = Math.Min(MAP_W, (int)MathF.Ceiling(maxX / COL_STEP) + 1);
        int rowStart = Math.Max(0, (int)MathF.Floor(minY / ROW_STEP) - 1);
        int rowEnd   = Math.Min(MAP_H, (int)MathF.Ceiling(maxY / ROW_STEP) + 1);

        bool civFilter = _filter == FilterMode.Civilization;
        for (int col = colStart; col < colEnd; col++)
        for (int row = rowStart; row < rowEnd; row++)
        {
            float hexCx = col * COL_STEP + (row & 1) * ODD_ROW_X + HEX_W * 0.5f;
            float hexCy = row * ROW_STEP + HEX_H * 0.5f;
            float cellX = hexCx - TILE_W * 0.5f;
            float cellY = hexCy - TILE_H * 0.5f;
            if (cellX > maxX || cellY > maxY || cellX + TILE_W < minX || cellY + TILE_H < minY) continue;
            int mapIdx = row * MAP_W + col;

            if (civFilter)
            {
                // Solid hex fan; texture is fully replaced by the faction color.
                sbyte dom = _domMap[mapIdx];
                DrawHex(hexCx, hexCy, dom < 0 ? UNOWNED_TINT : NATION_TINTS[dom]);
                continue;
            }

            var t = _tiles[mapIdx];
            int idx = (int)t;
            int srcCol = idx % SHEET_COLS;
            int srcRow = idx / SHEET_COLS;
            SpriteBatcher.DrawTexture(_sheet, srcCol, srcRow, SHEET_COLS, SHEET_ROWS,
                cellX, cellY, TILE_W, TILE_H);
        }

        // Hover: static white outline.
        if (_hoverCol >= 0)
        {
            GetCellRect(_hoverCol, _hoverRow, out float hx, out float hy);
            Draw.RectOutline(hx, hy, TILE_W, TILE_H, new Color(255, 255, 255, 180));
        }
        // Selection: "breathing" highlight via Anim.Pulse — outline grows/shrinks
        // around the cell center and the alpha pulses in counter-phase for extra pop.
        if (_selCol >= 0)
        {
            GetCellRect(_selCol, _selRow, out float sx, out float sy);
            float k     = Anim.Pulse(0.94f, 1.10f, period: 1.2f);
            float alpha = Anim.Pulse(170f,   255f, period: 1.2f, phase: 0.5f);
            float w = TILE_W * k, h = TILE_H * k;
            float cx = sx + TILE_W * 0.5f, cy = sy + TILE_H * 0.5f;
            Draw.RectOutline(cx - w * 0.5f, cy - h * 0.5f, w, h,
                new Color(255, 230, 60, (byte)alpha));
        }

        DrawTextDemo();
        _gui.Render(Camera, ViewportWidth, ViewportHeight);
    }

    private void DrawTextDemo()
    {
        if (_font == null) return;

        // Screen-space HUD: stays glued to the viewport regardless of pan/zoom.
        var banner = new TextAnim
        {
            WaveAmplitude = 8f, WaveSpeed = 6f,
            PopAmount = 0.18f, PopSpeed = 7f,
            Rainbow = true, RainbowSpeed = 4f,
            Stagger = 0.08f,
        };
        TextRenderer.DrawScreen(_font, "BDV HEX STRATEGY",
            ViewportWidth * 0.5f, 80f, 0.9f, Color.White,
            Camera, ViewportWidth, ViewportHeight, banner, TextAlign.Center);

        TextRenderer.DrawScreen(_font, "WASD to pan  ·  scroll to zoom  ·  click to select",
            ViewportWidth * 0.5f, 140f, 0.32f, new Color(220, 220, 230, 255),
            Camera, ViewportWidth, ViewportHeight, TextAnim.None, TextAlign.Center);

        // World-space label floats above the hovered hex (scales with zoom).
        if (_hoverCol >= 0)
        {
            GetCellRect(_hoverCol, _hoverRow, out float hx, out float hy);
            TextRenderer.Draw(_font, _tiles[_hoverRow * MAP_W + _hoverCol].ToString(),
                hx + TILE_W * 0.5f, hy - 8f, 0.6f, new Color(255, 240, 180, 255),
                TextAnim.Shaky(1.2f), TextAlign.Center);
        }
    }

    private void GetCellRect(int col, int row, out float x, out float y)
    {
        x = col * COL_STEP + (row & 1) * ODD_ROW_X + HEX_W * 0.5f - TILE_W * 0.5f;
        y = row * ROW_STEP + HEX_H * 0.5f - TILE_H * 0.5f;
    }

    // Filled pointy-top hex via 6-triangle fan from the center. Renders through Draw,
    // which flushes after the SpriteBatcher so the fill fully covers the texture.
    private static void DrawHex(float cx, float cy, Color color)
    {
        float hw = HEX_W * 0.5f;
        float hh = HEX_H * 0.5f;
        float qh = HEX_H * 0.25f;
        float topX = cx,      topY = cy - hh;
        float trX  = cx + hw, trY  = cy - qh;
        float brX  = cx + hw, brY  = cy + qh;
        float botX = cx,      botY = cy + hh;
        float blX  = cx - hw, blY  = cy + qh;
        float tlX  = cx - hw, tlY  = cy - qh;
        Draw.Triangle(cx, cy, topX, topY, trX, trY, color);
        Draw.Triangle(cx, cy, trX,  trY,  brX, brY, color);
        Draw.Triangle(cx, cy, brX,  brY,  botX, botY, color);
        Draw.Triangle(cx, cy, botX, botY, blX, blY, color);
        Draw.Triangle(cx, cy, blX,  blY,  tlX, tlY, color);
        Draw.Triangle(cx, cy, tlX,  tlY,  topX, topY, color);
    }

    private string TileAt(int col, int row) => _tiles[row * MAP_W + col].ToString();

    // -------------------- new Gui library demo --------------------

    private void BuildGui()
    {
        var root = new BdvEngine.Gui.Root();
        if (_font != null) root.WithFont(_font);

        // ── Top-left: world info & seed controls ──
        var info = new BdvEngine.Gui.Panel(16, 16, 320, 200)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        info.Add(new BdvEngine.Gui.Label(14, 10, "Bdv Hex Strategy").WithScale(0.46f));
        info.Add(new BdvEngine.Gui.Label(14, 42, $"{MAP_W}x{MAP_H} hex world").WithScale(0.30f).WithColor(new Color(180, 190, 210, 255)));
        info.Add(new BdvEngine.Gui.Label(14, 62, "WASD pan · scroll zoom · click selects").WithScale(0.26f).WithColor(new Color(170, 180, 200, 255)));
        info.Add(new BdvEngine.Gui.Button(14, 90, 110, 28, "Random Seed")
            .WithFont(_font!, 0.30f)
            .OnClick(() =>
            {
                _seed = new Random().Next(1, 999_999);
                _seedInput = _seed.ToString();
                Generate(_seed);
            })
            // Behavior-style attachable: button breathes while the cursor is over it.
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior()));
        info.Add(new BdvEngine.Gui.LiveLabel(14, 128, () => $"Seed: {_seed}   Zoom: {Camera.Zoom:F3}x")
            .WithScale(0.30f).WithColor(new Color(220, 225, 240, 255)));
        info.Add(new BdvEngine.Gui.LiveLabel(14, 152, () =>
            _hoverCol < 0 ? "Hover: —"
                          : $"Hover: ({_hoverCol},{_hoverRow}) {TileAt(_hoverCol, _hoverRow)}{CivLabel(_hoverCol, _hoverRow)}"
        ).WithScale(0.26f));
        info.Add(new BdvEngine.Gui.LiveLabel(14, 172, () =>
            _selCol < 0 ? "Selected: —"
                        : $"Selected: ({_selCol},{_selRow}) {TileAt(_selCol, _selRow)}{CivLabel(_selCol, _selRow)}"
        ).WithScale(0.26f));
        root.Add(info);

        // ── Top-right: filters ──
        float filterX = ViewportWidth - 230 - 16;
        var filters = new BdvEngine.Gui.Panel(filterX, 16, 230, 160)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        filters.Add(new BdvEngine.Gui.Label(14, 10, "Filters").WithScale(0.40f));
        filters.Add(new BdvEngine.Gui.Button(14, 42, 90, 26, "None")
            .WithFont(_font!, 0.30f).OnClick(() => _filter = FilterMode.None)
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior(0.95f, 1.06f, 0.9f)));
        filters.Add(new BdvEngine.Gui.Button(110, 42, 105, 26, "Civilization")
            .WithFont(_font!, 0.26f).OnClick(() => _filter = FilterMode.Civilization)
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior(0.95f, 1.06f, 0.9f)));
        filters.Add(new BdvEngine.Gui.LiveLabel(14, 80, () => $"Active: {_filter}")
            .WithScale(0.28f).WithColor(new Color(220, 225, 240, 255)));
        filters.Add(new BdvEngine.Gui.LiveLabel(14, 110, () =>
        {
            if (_hoverCol < 0) return "Owner: —";
            sbyte d = _domMap[_hoverRow * MAP_W + _hoverCol];
            return d < 0 ? "Owner: unowned" : $"Owner: {NATION_NAMES[d]}";
        }).WithScale(0.26f));
        root.Add(filters);

        // ── Bottom-left: tile preview with sheet stepper ──
        var preview = new BdvEngine.Gui.Panel(20, 240, 280, 280)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        preview.Add(new BdvEngine.Gui.Label(14, 12, "Tile Preview").WithScale(0.42f).WithColor(new Color(220, 225, 240, 255)));
        _previewImage = preview.Add(new BdvEngine.Gui.Image(14, 50, 130, 132, _sheet)
            .WithSubRect(0, 0, SHEET_COLS, SHEET_ROWS));
        _previewLabel = preview.Add(new BdvEngine.Gui.Label(160, 60, "Grass")
            .WithScale(0.36f).WithColor(new Color(255, 240, 200, 255))
            .WithAnim(new TextAnim { WaveAmplitude = 2f, WaveSpeed = 4f, Stagger = 0.12f }));
        preview.Add(new BdvEngine.Gui.Arrow(160, 100, 28, BdvEngine.Gui.ArrowDirection.Left)
            .OnClick(() => StepPreview(-1))
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior(0.92f, 1.12f, 0.7f)));
        preview.Add(new BdvEngine.Gui.Arrow(196, 100, 28, BdvEngine.Gui.ArrowDirection.Right)
            .OnClick(() => StepPreview(+1))
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior(0.92f, 1.12f, 0.7f)));
        preview.Add(new BdvEngine.Gui.Label(14, 195, "Camera Speed").WithScale(0.30f).WithColor(new Color(180, 190, 210, 255)));
        preview.Add(new BdvEngine.Gui.Slider(14, 220, 250, 14, 100f, 2000f, _camSpeed).OnChange(v => _camSpeed = v));
        root.Add(preview);

        _gui = root;
        UpdatePreview();
    }

    private void StepPreview(int delta)
    {
        // Step through the spritesheet, skipping placeholder cells.
        int total = SHEET_COLS * SHEET_ROWS;
        for (int i = 0; i < total; i++)
        {
            _previewTile = (_previewTile + delta + total) % total;
            if (Enum.IsDefined(typeof(Tile), (byte)_previewTile)) break;
        }
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        int col = _previewTile % SHEET_COLS;
        int row = _previewTile / SHEET_COLS;
        _previewImage.WithSubRect(col, row, SHEET_COLS, SHEET_ROWS);
        _previewLabel.Text = ((Tile)_previewTile).ToString();
    }

    private string CivLabel(int col, int row)
    {
        byte v = _civMap[row * MAP_W + col];
        if (v == 0) return "";
        int packed = v - 1;
        return $" — {NATION_NAMES[packed / 4]} {RANK_NAMES[packed % 4]}";
    }

    // Pointy-top hex pixel→axial→offset (odd-r), with cube rounding. World origin shifted
    // so col 0 row 0 hex center sits at (HEX_W/2, HEX_H/2).
    private static (int Col, int Row) PixelToHex(float worldX, float worldY)
    {
        float adjX = worldX - HEX_W * 0.5f;
        float adjY = worldY - HEX_H * 0.5f;
        float size = HEX_H * 0.5f; // center-to-vertex (point) distance
        float q = (MathF.Sqrt(3f) / 3f * adjX - 1f / 3f * adjY) / size;
        float r = (2f / 3f * adjY) / size;
        float cx = q, cz = r, cy = -cx - cz;
        int rx = (int)MathF.Round(cx);
        int ry = (int)MathF.Round(cy);
        int rz = (int)MathF.Round(cz);
        float dx = MathF.Abs(rx - cx), dy = MathF.Abs(ry - cy), dz = MathF.Abs(rz - cz);
        if (dx > dy && dx > dz) rx = -ry - rz;
        else if (dy > dz) ry = -rx - rz;
        else rz = -rx - ry;
        int row = rz;
        int col = rx + (row - (row & 1)) / 2;
        return (col, row);
    }

    // -------------------- world generation --------------------

    private void Generate(int seed)
    {
        var elev   = new Noise(seed);
        var moist  = new Noise(seed + 9173);
        var temp   = new Noise(seed + 2741);
        var detail = new Noise(seed + 5519);
        var rng    = new SeededRng(seed);
        Array.Clear(_waterMask);
        Array.Clear(_lavaMask);
        Array.Clear(_civMap);
        Array.Fill(_domMap, (sbyte)-1);

        var tempMap = new float[MAP_W * MAP_H];
        var detailMap = new float[MAP_W * MAP_H];

        // Pass 1 — sample noise into per-cell maps.
        for (int row = 0; row < MAP_H; row++)
        for (int col = 0; col < MAP_W; col++)
        {
            // World-space sample point keeps the noise frequency uniform across the
            // staggered hex grid.
            float wx = col * 0.6f + (row & 1) * 0.3f;
            float wy = row * 0.6f;

            float h = elev.Fbm(wx * 0.030f, wy * 0.030f, 6);

            // Continent falloff — circular bias toward land in the middle, ocean at edges.
            float ndx = (col / (float)MAP_W - 0.5f) * 2f;
            float ndy = (row / (float)MAP_H - 0.5f) * 2f;
            float d2 = ndx * ndx + ndy * ndy;
            float island = 1f - MathF.Min(1f, d2 * 0.85f);
            h = h * 0.65f + island * 0.35f;

            float m = moist.Fbm(wx * 0.045f, wy * 0.045f, 4);

            // Temperature: warm equator (mid row), cold poles, with elevation cooling.
            float lat = MathF.Abs(ndy);
            float t = (1f - lat) * 0.85f + temp.Fbm(wx * 0.05f, wy * 0.05f, 3) * 0.15f;
            if (h > 0.55f) t -= (h - 0.55f) * 0.6f;
            t = Math.Clamp(t, 0f, 1f);

            int idx = row * MAP_W + col;
            _height[idx] = h;
            _moist[idx] = m;
            tempMap[idx] = t;
            detailMap[idx] = detail.Fbm(wx * 0.18f, wy * 0.18f, 2);
        }

        // Pass 2 — carve inland lakes in moist depressions, then rivers from highland
        // sources flowing downhill. Both write into _waterMask.
        CarveLakes(rng);
        CarveRivers(rng);
        CarveVolcanoes(rng);
        CarveCivilizations(rng);
        ComputeDominance();

        // Pass 3 — assign biome tiles, with water mask overriding land biomes.
        for (int row = 0; row < MAP_H; row++)
        for (int col = 0; col < MAP_W; col++)
        {
            int idx = row * MAP_W + col;
            float h = _height[idx];
            float m = _moist[idx];
            float t = tempMap[idx];
            float v = detailMap[idx];
            float j = (float)rng.Next();

            if (_waterMask[idx])
            {
                // Lake interior vs rim/river: count fully-surrounded water (mask + ocean).
                int waterN = 0;
                for (int k = 0; k < 6; k++)
                {
                    int nc = col + HEX_DC[row & 1][k];
                    int nr = row + HEX_DR[k];
                    if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) { waterN++; continue; }
                    int ni = nr * MAP_W + nc;
                    if (_waterMask[ni] || _height[ni] < 0.30f) waterN++;
                }
                _tiles[idx] = waterN >= 5 ? Tile.Water : Tile.ShallowWaters;
                continue;
            }

            if (_lavaMask[idx]) { _tiles[idx] = Tile.LavaMountain; continue; }

            // Civilizations don't override the biome tile — settlements live in _civMap
            // and are visualized via the dominance filter / a future strongholds layer.
            _tiles[idx] = PickBiome(h, m, t, v, j);
        }
    }

    private void CarveLakes(SeededRng rng)
    {
        int target = rng.NextInt(6, 14);
        int placed = 0, attempts = 0;
        var stack = new Stack<int>();
        var seen = new HashSet<int>();
        var lake = new List<int>();

        while (placed < target && attempts < 800)
        {
            attempts++;
            int sc = rng.NextInt(8, MAP_W - 8);
            int sr = rng.NextInt(8, MAP_H - 8);
            int sIdx = sr * MAP_W + sc;
            float sh = _height[sIdx];
            if (sh < 0.34f || sh > 0.55f) continue;
            if (_moist[sIdx] < 0.42f) continue;
            if (_waterMask[sIdx]) continue;

            // Reject if any ocean is within ~6 hex rings — lakes should be inland.
            bool nearOcean = false;
            for (int dr = -6; dr <= 6 && !nearOcean; dr++)
            for (int dc = -6; dc <= 6 && !nearOcean; dc++)
            {
                int nc = sc + dc, nr = sr + dr;
                if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) continue;
                if (_height[nr * MAP_W + nc] < 0.30f) nearOcean = true;
            }
            if (nearOcean) continue;

            // Flood fill: include neighbors whose elevation stays under the lake's
            // shoreline ceiling. Cap size so lakes don't sprawl across whole basins.
            stack.Clear(); seen.Clear(); lake.Clear();
            stack.Push(sIdx);
            float ceiling = sh + 0.045f;
            int maxSize = rng.NextInt(10, 40);

            while (stack.Count > 0 && lake.Count < maxSize)
            {
                int idx = stack.Pop();
                if (!seen.Add(idx)) continue;
                if (_waterMask[idx]) continue;
                float h = _height[idx];
                if (h < 0.30f || h > ceiling) continue;
                lake.Add(idx);
                int rr = idx / MAP_W, cc = idx % MAP_W;
                for (int k = 0; k < 6; k++)
                {
                    int nc = cc + HEX_DC[rr & 1][k];
                    int nr = rr + HEX_DR[k];
                    if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) continue;
                    stack.Push(nr * MAP_W + nc);
                }
            }
            if (lake.Count < 5) continue;
            foreach (var i in lake) _waterMask[i] = true;
            placed++;
        }
    }

    private void CarveCivilizations(SeededRng rng)
    {
        // 3 nations, capital fortresses far apart on habitable land. Each spawns a
        // ring of castles, then cities, then villages — denser in the middle.
        var capitals = new List<(int c, int r)>();
        float minSep = MAP_W * 0.30f;
        int attempts = 0;
        while (capitals.Count < NATION_COUNT && attempts < 1500)
        {
            attempts++;
            int c = rng.NextInt(10, MAP_W - 10);
            int r = rng.NextInt(10, MAP_H - 10);
            int idx = r * MAP_W + c;
            if (!IsHabitable(idx)) continue;
            bool ok = true;
            foreach (var cap in capitals)
            {
                if (HexDist(cap.c, cap.r, c, r) < minSep) { ok = false; break; }
            }
            if (!ok) continue;
            capitals.Add((c, r));
        }

        for (int n = 0; n < capitals.Count; n++)
        {
            var (cc, cr) = capitals[n];
            SetCiv(cr * MAP_W + cc, n, 3); // fortress at capital

            int castles  = rng.NextInt(2, 4);
            int cities   = rng.NextInt(4, 7);
            int villages = rng.NextInt(8, 16);
            PlaceRing(cc, cr, n, 2, 5,  castles,  rng, 2);
            PlaceRing(cc, cr, n, 3, 7,  cities,   rng, 1);
            PlaceRing(cc, cr, n, 4, 11, villages, rng, 0);
        }
    }

    // For every tile, find the closest civ tile within its rank's influence radius.
    // Closest claim wins; ties broken by iteration order. Tiles outside any influence
    // remain unowned (-1). Used by the Civilization filter to color the map by control.
    private void ComputeDominance()
    {
        var bestDist = new float[MAP_W * MAP_H];
        Array.Fill(bestDist, float.MaxValue);

        for (int row = 0; row < MAP_H; row++)
        for (int col = 0; col < MAP_W; col++)
        {
            int idx = row * MAP_W + col;
            if (_civMap[idx] == 0) continue;
            int packed = _civMap[idx] - 1;
            int nation = packed / 4;
            int rank = packed % 4;
            int radius = INFLUENCE[rank];

            int rMin = Math.Max(0, row - radius);
            int rMax = Math.Min(MAP_H - 1, row + radius);
            int cMin = Math.Max(0, col - radius);
            int cMax = Math.Min(MAP_W - 1, col + radius);
            for (int rr = rMin; rr <= rMax; rr++)
            for (int cc = cMin; cc <= cMax; cc++)
            {
                float d = HexDist(col, row, cc, rr);
                if (d > radius) continue;
                int ni = rr * MAP_W + cc;
                if (d < bestDist[ni])
                {
                    bestDist[ni] = d;
                    _domMap[ni] = (sbyte)nation;
                }
            }
        }
    }

    private bool IsHabitable(int idx)
    {
        if (_waterMask[idx] || _lavaMask[idx]) return false;
        if (_civMap[idx] != 0) return false;
        float h = _height[idx];
        return h >= 0.32f && h <= 0.78f;
    }

    private void PlaceRing(int cc, int cr, int nation, int minR, int maxR, int count, SeededRng rng, int rank)
    {
        int placed = 0, attempts = 0;
        while (placed < count && attempts < count * 40)
        {
            attempts++;
            int dc = rng.NextInt(-maxR, maxR);
            int dr = rng.NextInt(-maxR, maxR);
            int c = cc + dc, r = cr + dr;
            if ((uint)c >= MAP_W || (uint)r >= MAP_H) continue;
            float dist = HexDist(cc, cr, c, r);
            if (dist < minR || dist > maxR) continue;
            int idx = r * MAP_W + c;
            if (!IsHabitable(idx)) continue;
            SetCiv(idx, nation, rank);
            placed++;
        }
    }

    private void SetCiv(int idx, int nation, int rank)
        => _civMap[idx] = (byte)(nation * 4 + rank + 1);

    // Exact hex distance via cube coords (odd-r offset → axial → cube).
    private static float HexDist(int c1, int r1, int c2, int r2)
    {
        int q1 = c1 - (r1 - (r1 & 1)) / 2; int s1 = -q1 - r1;
        int q2 = c2 - (r2 - (r2 & 1)) / 2; int s2 = -q2 - r2;
        return (Math.Abs(q1 - q2) + Math.Abs(r1 - r2) + Math.Abs(s1 - s2)) / 2f;
    }

    private void CarveVolcanoes(SeededRng rng)
    {
        // Volcano clusters: a hot/dry seed plus a few mountain neighbors, so lava
        // shows up as a recognizable landmark rather than scattered single hexes.
        int target = rng.NextInt(4, 11);
        int placed = 0, attempts = 0;
        var frontier = new List<int>();

        while (placed < target && attempts < 600)
        {
            attempts++;
            int c = rng.NextInt(4, MAP_W - 4);
            int r = rng.NextInt(4, MAP_H - 4);
            int idx = r * MAP_W + c;
            if (_waterMask[idx] || _lavaMask[idx]) continue;
            if (_height[idx] < 0.62f) continue;
            if (_moist[idx] > 0.55f) continue; // volcanoes prefer dry ground

            int size = rng.NextInt(2, 6);
            _lavaMask[idx] = true;
            frontier.Clear(); frontier.Add(idx);
            int grown = 1;

            while (grown < size && frontier.Count > 0)
            {
                int pickAt = rng.NextInt(0, frontier.Count - 1);
                int from = frontier[pickAt];
                frontier.RemoveAt(pickAt);
                int fr = from / MAP_W, fc = from % MAP_W;
                for (int k = 0; k < 6 && grown < size; k++)
                {
                    int nc = fc + HEX_DC[fr & 1][k];
                    int nr = fr + HEX_DR[k];
                    if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) continue;
                    int ni = nr * MAP_W + nc;
                    if (_lavaMask[ni] || _waterMask[ni]) continue;
                    if (_height[ni] < 0.55f) continue;
                    _lavaMask[ni] = true;
                    frontier.Add(ni);
                    grown++;
                }
            }
            placed++;
        }
    }

    private void CarveRivers(SeededRng rng)
    {
        int target = rng.NextInt(10, 24);
        int placed = 0, attempts = 0;
        var seen = new HashSet<int>();
        var path = new List<int>();

        while (placed < target && attempts < 800)
        {
            attempts++;
            int c = rng.NextInt(4, MAP_W - 4);
            int r = rng.NextInt(4, MAP_H - 4);
            int idx = r * MAP_W + c;
            float h = _height[idx];
            if (h < 0.62f || h > 0.88f) continue; // mountain/hill source
            if (_moist[idx] < 0.38f) continue;
            if (_waterMask[idx]) continue;

            // Walk steepest descent. Allow a small random nudge if pinned at a saddle
            // so rivers don't terminate inside basins above sea level.
            seen.Clear(); path.Clear();
            int cc = c, rr = r;
            bool reachedDrain = false;

            for (int step = 0; step < MAP_W * 2; step++)
            {
                int curIdx = rr * MAP_W + cc;
                if (!seen.Add(curIdx)) break;
                if (_height[curIdx] < 0.30f) { reachedDrain = true; break; }
                if (_waterMask[curIdx]) { reachedDrain = true; break; }
                path.Add(curIdx);

                float curH = _height[curIdx];
                float bestH = curH;
                int bestC = cc, bestR = rr;
                for (int k = 0; k < 6; k++)
                {
                    int nc = cc + HEX_DC[rr & 1][k];
                    int nr = rr + HEX_DR[k];
                    if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) continue;
                    float nh = _height[nr * MAP_W + nc];
                    if (nh < bestH) { bestH = nh; bestC = nc; bestR = nr; }
                }

                if (bestC == cc && bestR == rr)
                {
                    int dir = rng.NextInt(0, 5);
                    int nc = cc + HEX_DC[rr & 1][dir];
                    int nr = rr + HEX_DR[dir];
                    if ((uint)nc >= MAP_W || (uint)nr >= MAP_H) break;
                    cc = nc; rr = nr;
                }
                else { cc = bestC; rr = bestR; }
            }

            if (!reachedDrain || path.Count < 6) continue;
            foreach (var i in path) _waterMask[i] = true;
            placed++;
        }
    }

    private static Tile PickBiome(float h, float m, float t, float v, float j)
    {
        // Water tiers — depth from coast (high h) to abyss (low h).
        if (h < 0.30f)
        {
            if (h < 0.10f)
            {
                if (j < 0.005f) return Tile.DeepWaterIsland;
                if (j < 0.04f)  return Tile.DeepWatersStones;
                return Tile.DeepWaters;
            }
            if (h < 0.22f) return Tile.Water;
            return Tile.ShallowWaters;
        }

        // Peak mountains. Lava mountains are placed as deliberate volcano clusters
        // in CarveVolcanoes, so the per-tile branch only picks arid here.
        if (h > 0.86f)
        {
            if (t < 0.22f) return Tile.DenseSnowyMountains;
            if (t > 0.78f && m < 0.32f) return Tile.AridMountains;
            if (t < 0.42f) return Tile.SnowyMountains;
            return Tile.Mountains;
        }

        // Mid-high mountains.
        if (h > 0.72f)
        {
            if (t < 0.30f) return v < 0.45f ? Tile.SnowLargeMountains : Tile.SnowSmallMountains;
            if (t > 0.78f && m < 0.32f) return Tile.AridMountains;
            return v < 0.45f ? Tile.SmallMountains : Tile.Mountains;
        }

        // Hills.
        if (h > 0.58f)
        {
            if (t < 0.32f) return v < 0.50f ? Tile.SnowSmallMountains : Tile.SnowLargeMountains;
            if (m > 0.55f) return v < 0.45f ? Tile.Hills : Tile.SparseHills;
            return v < 0.50f ? Tile.PlainsPlateau : Tile.Hills;
        }

        // Cold lowlands → snowy biomes (boreal/tundra).
        if (t < 0.30f)
        {
            if (m > 0.55f) return v < 0.50f ? Tile.SnowForest : Tile.SparseSnowForest;
            if (m > 0.32f) return v < 0.50f ? Tile.SparseSnowForest : Tile.SnowBushes;
            return v < 0.55f ? Tile.SnowStones : Tile.SnowBushes;
        }

        // Hot + dry → desert family. Variants spread by detail-noise so they cluster.
        if (t > 0.70f && m < 0.32f)
        {
            if (j < 0.012f) return Tile.DesertOasis;
            if (v < 0.20f) return Tile.DesertRocks;
            if (v < 0.38f) return Tile.DesertDunes;
            if (v < 0.55f) return Tile.DenseCactusDesert;
            if (v < 0.75f) return Tile.CactusDesert;
            return Tile.Desert;
        }

        // Tropical wet.
        if (t > 0.68f && m > 0.62f)
        {
            if (v < 0.35f) return Tile.TropicalDenseForest;
            if (v < 0.60f) return Tile.DenseForest;
            return Tile.Forest;
        }

        // Cold-leaning forests → pines.
        if (t < 0.42f)
        {
            if (m > 0.55f) return v < 0.50f ? Tile.PineForest : Tile.SparsePine;
            if (m > 0.30f) return v < 0.50f ? Tile.SparsePine : Tile.Plains;
            return Tile.Plains;
        }

        // Moist temperate forest band.
        if (m > 0.60f)
        {
            if (v < 0.18f) return Tile.DenseForestClearing;
            if (v < 0.45f) return Tile.DenseForest;
            if (v < 0.70f) return Tile.Forest;
            return Tile.SparseForest;
        }

        // Mid-moisture woodland & meadow.
        if (m > 0.42f)
        {
            if (v < 0.25f) return Tile.SparseForest;
            if (v < 0.55f) return Tile.Clearing;
            if (v < 0.80f) return Tile.GrassFlowers;
            return Tile.Grass;
        }

        // Drier plains with cliff/valley accents driven by detail noise.
        if (m > 0.28f)
        {
            if (v < 0.08f) return Tile.PlainsValley;
            if (v < 0.16f) return Tile.PlainsCliff;
            if (v < 0.22f) return Tile.PlainsCliff2;
            if (v < 0.28f) return Tile.PlainsCliff3;
            if (v < 0.55f) return Tile.Plains;
            if (v < 0.85f) return Tile.Grass;
            return Tile.GrassFlowers;
        }

        // Driest fringes — sparse grass / plains.
        return v < 0.50f ? Tile.Plains : Tile.Grass;
    }
}
