using System.Text.Json;
using BdvEngine;
using StbImageSharp;

namespace SpritesheetEditorApp;

/// <summary>
/// Spritesheet packer with two preview modes:
///   • Normal — straight rectangular grid (cellW × cellH per cell).
///   • Hex    — same packed file, but the preview offsets odd rows by half a
///              cell width so you can verify pointy-top hex art tiles cleanly.
///
/// The packed PNG is *always* a regular grid (the hex mode is purely a layout
/// metadata flag the consuming game uses to interpret the cells). Saves both:
///   - output.png  : packed image, cols × rows of cellW × cellH cells
///   - output.json : { mode, cellWidth, cellHeight, cols, rows, tiles[] }
///
/// Drop PNG files into <c>assets/input/</c>, run the editor, tweak the sliders,
/// hit Save. The auto-arrange order is alphabetical row-major.
/// </summary>
public sealed class SpritesheetEditor : Game
{
    private sealed class Loaded
    {
        public string Path = "";
        public string Name = "";
        public int Width, Height;
        public byte[] Pixels = Array.Empty<byte>();
        public Material Material = null!;
    }

    private sealed class Config
    {
        public string InputDir { get; set; } = "input";
        public string OutputPath { get; set; } = "output.png";
        public string MetadataPath { get; set; } = "output.json";
        public int CellWidth { get; set; } = 195;
        public int CellHeight { get; set; } = 203;
        public int Cols { get; set; } = 6;
        public string Mode { get; set; } = "hex";
        public bool SliceFromSheet { get; set; } = false;
        public int SourceCellWidth { get; set; } = 195;
        public int SourceCellHeight { get; set; } = 203;
        public int SourceCols { get; set; } = 6;
        /// <summary>Number of rows in the source sheet. Used for *exact* slicing —
        /// the row pitch is computed as sheet.Height / SourceRows, so non-integer
        /// row heights (e.g., 1170×2032 → 203.2/row) don't drift across rows.</summary>
        public int SourceRows { get; set; } = 10;
        public string Fit { get; set; } = "stretch";
        /// <summary>If true, after slicing each cell its alpha bounding box is detected
        /// and the slice is cropped to that bbox + a small margin, then resized back to
        /// canonical cell size. Fixes non-uniform source layouts where content drifts
        /// vertically/horizontally across rows.</summary>
        public bool AutoTrim { get; set; } = false;
        /// <summary>Pixels of transparent margin to leave around the trimmed content
        /// (so hex shapes etc. don't get touched at the edges by the cell border).</summary>
        public int TrimMargin { get; set; } = 2;
        /// <summary>Alpha value (0..255) below which a pixel counts as "transparent" for
        /// the bbox scan. Default 8 — anything mostly invisible counts as background.</summary>
        public int TrimAlphaThreshold { get; set; } = 8;
    }

    /// <summary>How an oversized/undersized source tile is fit into the output cell.</summary>
    public enum FitMode
    {
        /// <summary>No resize. Center inside the cell — crop overflow, pad shortfall.</summary>
        Center,
        /// <summary>Scale to exactly cell size; aspect ratio ignored. Distorts non-square sources.</summary>
        Stretch,
        /// <summary>Scale to fit inside the cell preserving aspect (letterbox/pillarbox padding).</summary>
        Fit,
        /// <summary>Scale to fill the cell preserving aspect; crop the excess.</summary>
        Fill,
    }

    private Config _cfg = new();
    private string _inputDir = "";
    private string _outputPath = "";
    private string _metadataPath = "";
    private readonly List<Loaded> _files = new();
    private int _cellW = 195;
    private int _cellH = 203;
    private int _cols = 6;
    private bool _hexMode = true;
    private bool _sliceFromSheet = false;
    private int _sourceCellW = 195;
    private int _sourceCellH = 203;
    private int _sourceCols = 6;
    private int _sourceRows = 10;
    private FitMode _fitMode = FitMode.Stretch;
    private bool _autoTrim = false;
    private int _trimMargin = 2;
    private int _trimAlpha = 8;
    private string _status = "";
    private double _statusUntil;

    private Font _font = null!;
    private BdvEngine.Gui.Root _gui = null!;

    public override void Init()
    {
        _font = Font.LoadDefault("editor", 64);

        // Load config (relative paths anchored at assets/).
        string assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        string cfgPath = Path.Combine(assetsDir, "editor.json");
        if (File.Exists(cfgPath))
        {
            try
            {
                _cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(cfgPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Config();
            }
            catch (Exception ex) { Console.WriteLine($"Editor: config load failed: {ex.Message}"); }
        }
        _inputDir    = Path.IsPathRooted(_cfg.InputDir)     ? _cfg.InputDir     : Path.Combine(assetsDir, _cfg.InputDir);
        _outputPath  = Path.IsPathRooted(_cfg.OutputPath)   ? _cfg.OutputPath   : Path.Combine(assetsDir, _cfg.OutputPath);
        _metadataPath= Path.IsPathRooted(_cfg.MetadataPath) ? _cfg.MetadataPath : Path.Combine(assetsDir, _cfg.MetadataPath);
        _cellW   = _cfg.CellWidth;
        _cellH   = _cfg.CellHeight;
        _cols    = _cfg.Cols;
        _hexMode = string.Equals(_cfg.Mode, "hex", StringComparison.OrdinalIgnoreCase);
        _sliceFromSheet = _cfg.SliceFromSheet;
        _sourceCellW = _cfg.SourceCellWidth;
        _sourceCellH = _cfg.SourceCellHeight;
        _sourceCols  = _cfg.SourceCols;
        _sourceRows  = _cfg.SourceRows;
        _autoTrim    = _cfg.AutoTrim;
        _trimMargin  = _cfg.TrimMargin;
        _trimAlpha   = _cfg.TrimAlphaThreshold;
        _fitMode = _cfg.Fit?.ToLowerInvariant() switch
        {
            "center"  => FitMode.Center,
            "fit"     => FitMode.Fit,
            "fill"    => FitMode.Fill,
            _         => FitMode.Stretch,
        };

        ReloadFiles();
        BuildGui();

        Camera.X = 0; Camera.Y = 0; Camera.Zoom = 1f;
    }

    public override void Update(double deltaTime)
    {
        _gui.Update(Camera, ViewportWidth, ViewportHeight);
        if (_status.Length > 0 && Time.Total > _statusUntil) _status = "";
    }

    public override void Render(Shader shader)
    {
        DrawPreviewGrid();
        _gui.Render(Camera, ViewportWidth, ViewportHeight);
    }

    // -------------------- file IO --------------------

    private void ReloadFiles()
    {
        _files.Clear();
        if (!Directory.Exists(_inputDir))
        {
            Directory.CreateDirectory(_inputDir);
            Flash($"Created empty input dir: {_inputDir}");
            return;
        }

        var paths = Directory.EnumerateFiles(_inputDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(p =>
            {
                var e = Path.GetExtension(p).ToLowerInvariant();
                return e is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga";
            })
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int loaded = 0;
        foreach (var p in paths)
        {
            try
            {
                using var fs = File.OpenRead(p);
                var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
                var name = Path.GetFileName(p);
                var matName = "__editor_tile:" + name;
                var texName = "__editor_tex:" + name;
                var tex = Texture.CreateBlank(texName, img.Width, img.Height);
                tex.UploadRgba(img.Width, img.Height, img.Data);
                TextureManager.Register(texName, tex);
                var mat = new Material(matName, texName, Color.White);
                MaterialManager.Register(mat);
                _files.Add(new Loaded
                {
                    Path = p, Name = name, Width = img.Width, Height = img.Height,
                    Pixels = img.Data, Material = mat,
                });
                loaded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Editor: failed to load {p}: {ex.Message}");
            }
        }

        // Slice mode: each loaded image is treated as a sheet, sliced into N tiles
        // using SourceCellW/H and SourceCols. Each tile becomes a virtual entry that
        // SaveSheet can pack into the output. This is the inverse of the default
        // "one file per cell" mode — used for re-tiling existing sheets at a new
        // cell size, or splitting hand-arranged sheets back into individual cells.
        if (_sliceFromSheet && _files.Count > 0)
        {
            int cols = Math.Max(1, _sourceCols);
            int rows = Math.Max(1, _sourceRows);
            // Derive canonical source-cell dims from the *first* sheet's pixel
            // dimensions so each slice gets normalized to the artist's actual cell
            // size. Removing the user-facing cellW/cellH sliders means it's
            // impossible to mis-set them out of sync with cols/rows.
            var firstSheet = _files[0];
            _sourceCellW = MathF.Max(1, firstSheet.Width  / cols) > 0 ? firstSheet.Width  / cols : 1;
            _sourceCellH = MathF.Max(1, firstSheet.Height / rows) > 0 ? firstSheet.Height / rows : 1;

            var sliced = new List<Loaded>();
            foreach (var sheet in _files)
            {
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var tile = SliceTile(sheet, c, r, cols, rows);
                    if (tile != null) sliced.Add(tile);
                }
            }
            _files.Clear();
            _files.AddRange(sliced);
            Flash($"Loaded {loaded} sheet(s) → sliced into {_files.Count} tile(s) " +
                  $"({cols}×{rows} of {_sourceCellW}×{_sourceCellH})");
            return;
        }

        Flash($"Loaded {loaded} file(s) from {_inputDir}");
    }

    private void SaveSheet()
    {
        if (_files.Count == 0) { Flash("No files loaded — drop PNGs in input/"); return; }
        int rows = (_files.Count + _cols - 1) / _cols;
        int outW = _cols * _cellW;
        int outH = rows * _cellH;
        var rgba = new byte[outW * outH * 4]; // zero-filled = transparent

        for (int i = 0; i < _files.Count; i++)
        {
            int r = i / _cols, c = i % _cols;
            BlitToCell(_files[i], rgba, outW, outH, c * _cellW, r * _cellH);
        }

        try
        {
            PngWriter.SavePng(_outputPath, outW, outH, rgba);

            var metadata = new
            {
                image = Path.GetFileName(_outputPath),
                mode = _hexMode ? "hex-pointy-odd-r" : "grid",
                cellWidth = _cellW,
                cellHeight = _cellH,
                cols = _cols,
                rows,
                tiles = _files.Select((f, i) => new
                {
                    row = i / _cols,
                    col = i % _cols,
                    name = f.Name,
                    sourceWidth = f.Width,
                    sourceHeight = f.Height,
                }).ToArray(),
            };
            File.WriteAllText(_metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            Flash($"Saved {outW}×{outH} → {Path.GetFileName(_outputPath)} ({_files.Count} tiles)");
        }
        catch (Exception ex) { Flash($"Save failed: {ex.Message}"); }
    }

    /// <summary>Copy a source image into one cell of the output buffer, applying the
    /// configured <see cref="FitMode"/> (Center / Stretch / Fit / Fill). Resizing uses
    /// nearest-neighbor — pixel-perfect for pixel art, no antialiasing/blurring.</summary>
    private void BlitToCell(Loaded src, byte[] dst, int dstW, int dstH, int cellX, int cellY)
    {
        // Compute the (possibly resized) intermediate buffer + dimensions.
        byte[] data = src.Pixels;
        int rW = src.Width, rH = src.Height;

        switch (_fitMode)
        {
            case FitMode.Stretch when (rW != _cellW || rH != _cellH):
                data = ResizeNearest(src.Pixels, src.Width, src.Height, _cellW, _cellH);
                rW = _cellW; rH = _cellH;
                break;
            case FitMode.Fit:
            {
                float ar = src.Width / (float)src.Height;
                int nW, nH;
                if (ar > _cellW / (float)_cellH) { nW = _cellW; nH = MathF.Max(1, (int)MathF.Round(_cellW / ar)) > 0 ? (int)MathF.Round(_cellW / ar) : 1; }
                else                              { nH = _cellH; nW = MathF.Max(1, (int)MathF.Round(_cellH * ar)) > 0 ? (int)MathF.Round(_cellH * ar) : 1; }
                if (nW != src.Width || nH != src.Height)
                    data = ResizeNearest(src.Pixels, src.Width, src.Height, nW, nH);
                rW = nW; rH = nH;
                break;
            }
            case FitMode.Fill:
            {
                float ar = src.Width / (float)src.Height;
                int nW, nH;
                if (ar > _cellW / (float)_cellH) { nH = _cellH; nW = (int)MathF.Round(_cellH * ar); }
                else                              { nW = _cellW; nH = (int)MathF.Round(_cellW / ar); }
                if (nW != src.Width || nH != src.Height)
                    data = ResizeNearest(src.Pixels, src.Width, src.Height, nW, nH);
                rW = nW; rH = nH;
                break;
            }
            // FitMode.Center: keep src as-is and let the centering math handle crop/pad.
        }

        // Center the (possibly resized) buffer into the cell.
        int offX = (_cellW - rW) / 2;
        int offY = (_cellH - rH) / 2;
        int copyW = Math.Min(rW, _cellW);
        int copyH = Math.Min(rH, _cellH);
        int srcStartX = offX < 0 ? -offX : 0;
        int srcStartY = offY < 0 ? -offY : 0;
        int dstStartX = cellX + Math.Max(0, offX);
        int dstStartY = cellY + Math.Max(0, offY);

        for (int y = 0; y < copyH; y++)
        {
            int srcRow = (srcStartY + y) * rW * 4 + srcStartX * 4;
            int dstRow = (dstStartY + y) * dstW * 4 + dstStartX * 4;
            Buffer.BlockCopy(data, srcRow, dst, dstRow, copyW * 4);
        }
    }

    /// <summary>Cut one tile out of a sheet at (col, row) using exact float arithmetic:
    /// the slice rect is `[col*W/cols .. (col+1)*W/cols] × [row*H/rows .. (row+1)*H/rows]`.
    /// Slice dimensions can vary by ±1 px to evenly tile non-integer pitches (e.g.,
    /// 1170×2032 sliced as 6×10 → 195 × 203/204 alternating). Each slice is then
    /// resampled to canonical (_sourceCellW, _sourceCellH) so downstream code sees
    /// uniform sources. Returns null if the cell falls outside the sheet bounds.</summary>
    private Loaded? SliceTile(Loaded sheet, int col, int row, int totalCols, int totalRows)
    {
        // Exact rect via long-int multiply-then-divide to avoid rounding compounding.
        int x0 = (int)((long)col       * sheet.Width  / totalCols);
        int x1 = (int)((long)(col + 1) * sheet.Width  / totalCols);
        int y0 = (int)((long)row       * sheet.Height / totalRows);
        int y1 = (int)((long)(row + 1) * sheet.Height / totalRows);
        int sliceW = x1 - x0;
        int sliceH = y1 - y0;
        if (sliceW <= 0 || sliceH <= 0) return null;
        if (x0 + sliceW > sheet.Width || y0 + sliceH > sheet.Height) return null;

        // Slice strictly within the cell rect — over-reading into neighbors caused
        // auto-trim to merge adjacent tiles into one bbox.
        int rW = sliceW, rH = sliceH;
        var raw = new byte[rW * rH * 4];
        for (int y = 0; y < rH; y++)
        {
            int srcRow = ((y0 + y) * sheet.Width + x0) * 4;
            int dstRow = y * rW * 4;
            Buffer.BlockCopy(sheet.Pixels, srcRow, raw, dstRow, rW * 4);
        }

        // Auto-trim: find the alpha bounding box and crop the slice to just the
        // visible content (with TrimMargin pixels of safety). Handles non-uniform
        // source layouts by snapping each cell to its actual silhouette.
        if (_autoTrim)
        {
            var (bx, by, bw, bh) = FindAlphaBounds(raw, rW, rH, _trimAlpha);
            if (bw > 0 && bh > 0)
            {
                int m = _trimMargin;
                int cx0 = Math.Max(0, bx - m);
                int cy0 = Math.Max(0, by - m);
                int cx1 = Math.Min(rW, bx + bw + m);
                int cy1 = Math.Min(rH, by + bh + m);
                int cW = cx1 - cx0, cH = cy1 - cy0;
                var cropped = new byte[cW * cH * 4];
                for (int y = 0; y < cH; y++)
                {
                    int sR = ((cy0 + y) * rW + cx0) * 4;
                    int dR = y * cW * 4;
                    Buffer.BlockCopy(raw, sR, cropped, dR, cW * 4);
                }
                raw = cropped; rW = cW; rH = cH;
            }
        }

        // Normalize to canonical source-cell dimensions so all slices are uniform.
        byte[] pixels = raw;
        int outW = _sourceCellW, outH = _sourceCellH;
        if (rW != outW || rH != outH)
            pixels = ResizeNearest(raw, rW, rH, outW, outH);

        string baseName = Path.GetFileNameWithoutExtension(sheet.Path);
        string id = $"{baseName}_{col}_{row}";
        string texName = "__slice_tex:" + id;
        var tex = Texture.CreateBlank(texName, outW, outH);
        tex.UploadRgba(outW, outH, pixels);
        TextureManager.Register(texName, tex);
        var mat = new Material("__slice_mat:" + id, texName, Color.White);
        MaterialManager.Register(mat);
        return new Loaded { Path = sheet.Path, Name = id, Width = outW, Height = outH, Pixels = pixels, Material = mat };
    }

    /// <summary>Find the smallest rectangle containing all pixels with alpha &gt; threshold.
    /// Returns (0,0,0,0) if the image is fully transparent.</summary>
    private static (int x, int y, int w, int h) FindAlphaBounds(byte[] rgba, int width, int height, int threshold)
    {
        int xMin = width, yMin = height, xMax = -1, yMax = -1;
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * width * 4 + 3; // alpha channel
            for (int x = 0; x < width; x++)
            {
                if (rgba[rowBase + x * 4] > threshold)
                {
                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }
        }
        if (xMax < 0) return (0, 0, 0, 0);
        return (xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
    }

    /// <summary>Pixel-perfect nearest-neighbor resample. Crisp for pixel art; no AA.</summary>
    private static byte[] ResizeNearest(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        for (int y = 0; y < dstH; y++)
        {
            int srcY = Math.Min(srcH - 1, y * srcH / dstH);
            for (int x = 0; x < dstW; x++)
            {
                int srcX = Math.Min(srcW - 1, x * srcW / dstW);
                int srcIdx = (srcY * srcW + srcX) * 4;
                int dstIdx = (y * dstW + x) * 4;
                dst[dstIdx + 0] = src[srcIdx + 0];
                dst[dstIdx + 1] = src[srcIdx + 1];
                dst[dstIdx + 2] = src[srcIdx + 2];
                dst[dstIdx + 3] = src[srcIdx + 3];
            }
        }
        return dst;
    }

    // -------------------- preview render --------------------

    private void DrawPreviewGrid()
    {
        if (_files.Count == 0) return;

        // Preview area sits to the right of the sidebar (320 wide @ x=16) and the
        // file list (220 wide @ x=360), with a 20px gutter.
        const int margin = 600;
        const int topMargin = 24;
        const int bottomPad = 24;
        const int rightPad  = 24;
        int rows = (_files.Count + _cols - 1) / _cols;

        // Available area inside the preview region.
        float availW = MathF.Max(0, ViewportWidth  - margin - rightPad);
        float availH = MathF.Max(0, ViewportHeight - topMargin - bottomPad);
        if (availW <= 0 || availH <= 0) return;

        float cellAspect = _cellW > 0 ? (float)_cellH / _cellW : 1f;
        float maxCellPx  = 96f;

        // Cell width must satisfy BOTH width and height constraints. Hex mode adds
        // 0.5 of horizontal overhang (odd rows shift right) and vertical packing of
        // 0.75 between rows + 0.25 trailing.
        float widthDivisor  = _cols + (_hexMode ? 0.5f : 0f);
        float heightDivisor = _hexMode ? (rows * 0.75f + 0.25f) : rows;
        float capByWidth  = availW / widthDivisor;
        float capByHeight = heightDivisor > 0 ? (availH / heightDivisor) / cellAspect : maxCellPx;
        float candCellW = MathF.Min(maxCellPx, MathF.Min(capByWidth, capByHeight));
        if (candCellW < 4f) candCellW = 4f;
        float candCellH = candCellW * cellAspect;

        float gridW = candCellW * widthDivisor;
        float gridH = candCellH * heightDivisor;

        float ox = margin;
        float oy = topMargin;

        // Scissor-clip the preview to its viewport rect so any residual overflow
        // (e.g., from a slow-shrink frame after the cols slider changes) doesn't
        // bleed into the rest of the UI.
        BdvEngine.Gui.Scissor.Push(ox, oy, availW, availH);

        Draw.RectOutline(ox - 4, oy - 4, gridW + 8, gridH + 8, new Color(80, 90, 120, 255));

        for (int i = 0; i < _files.Count; i++)
        {
            int r = i / _cols, c = i % _cols;
            float x, y;
            if (_hexMode)
            {
                x = ox + c * candCellW + (r & 1) * candCellW * 0.5f;
                y = oy + r * candCellH * 0.75f;
            }
            else
            {
                x = ox + c * candCellW;
                y = oy + r * candCellH;
            }
            var ws = WorldScale();
            var w = WorldFromScreen(x, y);
            SpriteBatcher.DrawSolid(w.X, w.Y, candCellW * ws, candCellH * ws,
                new Color(30, 30, 38, 255), SpriteLayer.UIBack);
            SpriteBatcher.DrawTextureUV(_files[i].Material, 0f, 0f, 1f, 1f,
                w.X, w.Y, candCellW * ws, candCellH * ws,
                Color.White, SpriteLayer.UI);
            Draw.RectOutline(x, y, candCellW, candCellH, new Color(70, 80, 100, 180));
        }

        BdvEngine.Gui.Scissor.Pop();
    }

    private System.Numerics.Vector2 WorldFromScreen(float sx, float sy)
        => Camera.ScreenToWorld(sx, sy, ViewportWidth, ViewportHeight);
    private float WorldScale() => 1f / Camera.Zoom;

    // -------------------- gui --------------------

    private void BuildGui()
    {
        _gui = new BdvEngine.Gui.Root().WithFont(_font);

        // Settings sidebar — anchored StretchLeft so it follows the window height.
        // X=16 inset from left, Width=320 explicit; Y=16 top inset, Height=16 bottom inset.
        // (ViewportHeight is 0 at Init time — using stretch anchors avoids the math.)
        var sidebar = new BdvEngine.Gui.VerticalLayout(16, 16, 320, 16)
            .WithSpacing(6f)
            .WithPadding(new BdvEngine.Gui.Padding(14, 14, 14, 14));
        sidebar.AnchorTo(BdvEngine.Gui.Anchor.StretchLeft);
        sidebar.WithBackground(new Color(18, 22, 32, 255))
               .WithBorder(new Color(95, 115, 160, 255), 2f);

        Lbl(sidebar, "Spritesheet Editor", 0.40f, Color.White, 28);

        // Path text inputs — tweak input/output paths live without editing editor.json.
        Lbl(sidebar, "Input dir", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.TextInput(0, 0, 290, 26, _inputDir)
            .WithFont(_font, 0.26f)
            .WithPlaceholder("/path/to/input/")
            .OnSubmit(s => { _inputDir = s; ReloadFiles(); }));

        Lbl(sidebar, "Output PNG", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.TextInput(0, 0, 290, 26, _outputPath)
            .WithFont(_font, 0.26f)
            .WithPlaceholder("/path/to/sheet.png")
            .OnSubmit(s => { _outputPath = s; _metadataPath = Path.ChangeExtension(s, ".json"); }));

        // Reload + Save buttons side-by-side via HorizontalLayout.
        var actions = new BdvEngine.Gui.HorizontalLayout(0, 0, 290, 32)
            .WithSpacing(8f)
            .WithPadding(BdvEngine.Gui.Padding.Zero);
        actions.Background = null; // no chrome — actions row is invisible, just layout
        actions.Add(new BdvEngine.Gui.Button(0, 0, 130, 30, "Reload")
            .WithFont(_font, 0.30f)
            .OnClick(ReloadFiles)
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior()));
        actions.Add(new BdvEngine.Gui.Button(0, 0, 130, 30, "Save")
            .WithFont(_font, 0.30f)
            .OnClick(SaveSheet)
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior()));
        sidebar.Add(actions);

        // Mode dropdown (replaces the Hex Preview checkbox — extensible to more modes).
        Lbl(sidebar, "Layout mode", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Dropdown(0, 0, 290, 28,
                new[] { "Hex (pointy-top, odd-r)", "Grid (rectangular)" },
                _hexMode ? 0 : 1)
            .WithFont(_font, 0.28f)
            .OnChange(i => _hexMode = i == 0));

        // Fit mode — how oversize/undersize source tiles map into output cells.
        Lbl(sidebar, "Fit mode", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Dropdown(0, 0, 290, 28,
                new[] { "Stretch (ignore aspect)", "Fit (preserve aspect, pad)",
                        "Fill (preserve aspect, crop)", "Center (no resize)" },
                (int)_fitMode switch { 0 => 3, 1 => 0, 2 => 1, 3 => 2, _ => 0 })
            .WithFont(_font, 0.26f)
            .OnChange(i => _fitMode = i switch { 0 => FitMode.Stretch, 1 => FitMode.Fit,
                                                 2 => FitMode.Fill, 3 => FitMode.Center, _ => FitMode.Stretch }));

        // Slice mode — when on, each input image is treated as a packed sheet and
        // cut into source-cell-sized tiles before being repacked into the output.
        sidebar.Add(new BdvEngine.Gui.Toggle(0, 0, 290, 22, "Slice input as sheet", _sliceFromSheet)
            .WithFont(_font, 0.26f)
            .OnChange(v => { _sliceFromSheet = v; ReloadFiles(); }));
        LiveLbl(sidebar, () => $"Source: {_sourceCols} cols × {_sourceRows} rows   → cells {_sourceCellW}×{_sourceCellH}",
            0.24f, new Color(160, 175, 200, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 12, 1f, 32f, _sourceCols)
            .OnChange(v => { _sourceCols = (int)MathF.Round(v); if (_sliceFromSheet) ReloadFiles(); }));
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 12, 1f, 32f, _sourceRows)
            .OnChange(v => { _sourceRows = (int)MathF.Round(v); if (_sliceFromSheet) ReloadFiles(); }));
        // One-click: copy the auto-derived source cell dims to the output sliders so
        // the common "re-pack at original size" workflow doesn't require manual setup.
        sidebar.Add(new BdvEngine.Gui.Button(0, 0, 290, 26, "Apply source dims to output")
            .WithFont(_font, 0.26f)
            .OnClick(() => { _cols = _sourceCols; _cellW = _sourceCellW; _cellH = _sourceCellH; })
            .AddBehavior(new BdvEngine.Gui.PulseOnHoverBehavior()));

        // Auto-trim: snap each slice to its actual content silhouette. The slice rect
        // is over-read by 25% first so even significantly drifted content still falls
        // inside our search area before the alpha bbox crops it back.
        sidebar.Add(new BdvEngine.Gui.Toggle(0, 0, 290, 22, "Auto-trim (alpha bbox)", _autoTrim)
            .WithFont(_font, 0.26f)
            .OnChange(v => { _autoTrim = v; if (_sliceFromSheet) ReloadFiles(); }));
        LiveLbl(sidebar, () => $"Trim margin: {_trimMargin} px   alpha threshold: {_trimAlpha}",
            0.22f, new Color(160, 175, 200, 255), 12);
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 10, 0f, 16f, _trimMargin)
            .OnChange(v => { _trimMargin = (int)MathF.Round(v); if (_sliceFromSheet && _autoTrim) ReloadFiles(); }));
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 10, 0f, 64f, _trimAlpha)
            .OnChange(v => { _trimAlpha  = (int)MathF.Round(v); if (_sliceFromSheet && _autoTrim) ReloadFiles(); }));

        LiveLbl(sidebar, () => $"Output cols: {_cols}", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 14, 1f, 16f, _cols).OnChange(v => _cols = (int)MathF.Round(v)));
        LiveLbl(sidebar, () => $"Output cell W: {_cellW} px", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 14, 16f, 2048f, _cellW).OnChange(v => _cellW = (int)MathF.Round(v)));
        LiveLbl(sidebar, () => $"Output cell H: {_cellH} px", 0.26f, new Color(180, 190, 210, 255), 14);
        sidebar.Add(new BdvEngine.Gui.Slider(0, 0, 290, 14, 16f, 2048f, _cellH).OnChange(v => _cellH = (int)MathF.Round(v)));

        var stats = new BdvEngine.Gui.LiveLabel(0, 0, () =>
        {
            int rows = _files.Count == 0 ? 0 : (_files.Count + _cols - 1) / _cols;
            return $"Files: <color=#ffd87a>{_files.Count}</color>   Grid: {_cols} × {rows}   Cell: {_cellW} × {_cellH}";
        });
        stats.Width = 290; stats.Height = 18;
        stats.WithScale(0.24f).WithColor(new Color(220, 225, 240, 255)).Rich();
        sidebar.Add(stats);

        var status = new BdvEngine.Gui.LiveLabel(0, 0, () => _status);
        status.Width = 290; status.Height = 36;
        status.WithScale(0.24f).WithColor(new Color(255, 240, 180, 255)).Wrap();
        sidebar.Add(status);

        _gui.Add(sidebar);

        // ── Loaded files list — scrollable. Anchored StretchLeft so it fills viewport
        //    vertically, same trick as the sidebar to dodge the Init-time viewport=0 issue.
        var fileList = (BdvEngine.Gui.ScrollView)new BdvEngine.Gui.ScrollView(360, 16, 220, 16)
            .AnchorTo(BdvEngine.Gui.Anchor.StretchLeft);
        fileList.WithBackground(new Color(20, 25, 38, 255))
                .WithBorder(new Color(95, 115, 160, 255), 2f);
        fileList.Content.Add(new BdvEngine.Gui.Label(8, 8, "Loaded files").WithScale(0.32f));
        for (int i = 0; i < _files.Count; i++)
        {
            float rowY = 36 + i * 22;
            fileList.Content.Add(new BdvEngine.Gui.Label(8, rowY, $"{i:D2}  {_files[i].Name}")
                .WithScale(0.22f).WithColor(new Color(220, 225, 240, 255)));
        }
        fileList.ContentHeight = 36 + _files.Count * 22 + 16;
        _gui.Add(fileList);
    }

    private static BdvEngine.Gui.Label Lbl(BdvEngine.Gui.Element parent, string text, float scale, Color color, float h)
    {
        var l = parent.Add(new BdvEngine.Gui.Label(0, 0, text));
        l.WithScale(scale).WithColor(color);
        l.Width = 0; l.Height = h;
        return l;
    }

    private static BdvEngine.Gui.LiveLabel LiveLbl(BdvEngine.Gui.Element parent, Func<string> p, float scale, Color color, float h)
    {
        var l = parent.Add(new BdvEngine.Gui.LiveLabel(0, 0, p));
        l.WithScale(scale).WithColor(color);
        l.Width = 0; l.Height = h;
        return l;
    }

    private void Flash(string msg, double seconds = 4.0)
    {
        _status = msg;
        _statusUntil = Time.Total + seconds;
        Console.WriteLine(msg);
    }
}
