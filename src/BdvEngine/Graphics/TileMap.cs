using Silk.NET.OpenGL;

namespace BdvEngine;

public sealed class TileSet
{
    public Material Material { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }

    private (float U0, float V0, float U1, float V1)[] _uvs = Array.Empty<(float, float, float, float)>();
    private bool _ready;
    private int _cols;

    public TileSet(string materialName, string imagePath, int tileWidth, int tileHeight)
    {
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Material = new Material(materialName, imagePath, Color.White);
        MaterialManager.Register(Material);
    }

    public bool ComputeUVs()
    {
        if (_ready) return true;
        var tex = Material.DiffuseTexture;
        if (tex == null || !tex.IsLoaded) return false;

        _cols = tex.Width / TileWidth;
        int rows = tex.Height / TileHeight;
        var list = new List<(float, float, float, float)>(_cols * rows);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < _cols; c++)
        {
            float u0 = c * TileWidth / (float)tex.Width;
            float v0 = r * TileHeight / (float)tex.Height;
            float u1 = (c + 1) * TileWidth / (float)tex.Width;
            float v1 = (r + 1) * TileHeight / (float)tex.Height;
            list.Add((u0, v0, u1, v1));
        }
        _uvs = list.ToArray();
        _ready = true;
        return true;
    }

    public bool IsReady => _ready;
    public int TileCount => _uvs.Length;
    public int Cols => _cols;
    public int Rows => _cols == 0 ? 0 : _uvs.Length / _cols;

    internal (float U0, float V0, float U1, float V1) GetUV(int idx) => _uvs[idx];

    public bool TryGetTileGrid(int index, out int col, out int row)
    {
        if (index < 0 || index >= _uvs.Length || _cols == 0) { col = row = 0; return false; }
        col = index % _cols;
        row = index / _cols;
        return true;
    }
}

/// <summary>
/// Chunked tilemap renderer. The map is divided into CHUNK_SIZE×CHUNK_SIZE static
/// VBO/EBO chunks that are baked once on first render and re-baked only when a
/// tile in that chunk changes. Per-frame work is just AABB-cull → bind VAO → draw.
/// </summary>
public sealed class TileMap : IDisposable
{
    public const int CHUNK_SIZE = 64;

    public TileSet TileSet { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public TileSet? LodTileSet;
    public float LodThreshold = 6f;

    private readonly short[] _tiles;
    private readonly TileChunk[] _chunks;
    private TileChunk[]? _lodChunks;
    private readonly int _chunksX, _chunksY;

    public TileMap(TileSet tileSet, int width, int height, int tileSize)
    {
        TileSet = tileSet;
        Width = width; Height = height; TileSize = tileSize;
        _tiles = new short[width * height];
        Array.Fill(_tiles, (short)-1);

        _chunksX = (width  + CHUNK_SIZE - 1) / CHUNK_SIZE;
        _chunksY = (height + CHUNK_SIZE - 1) / CHUNK_SIZE;
        _chunks = new TileChunk[_chunksX * _chunksY];
        for (int cy = 0; cy < _chunksY; cy++)
        for (int cx = 0; cx < _chunksX; cx++)
            _chunks[cy * _chunksX + cx] = new TileChunk(cx, cy);
    }

    public void SetTile(int x, int y, int idx)
    {
        if ((uint)x >= Width || (uint)y >= Height) return;
        _tiles[y * Width + x] = (short)idx;
        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE;
        _chunks[cy * _chunksX + cx].MarkDirty();
        _lodChunks?[cy * _chunksX + cx].MarkDirty();
    }

    public int GetTile(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height) return -1;
        return _tiles[y * Width + x];
    }

    public void Fill(int idx)
    {
        Array.Fill(_tiles, (short)idx);
        foreach (var c in _chunks) c.MarkDirty();
        if (_lodChunks != null) foreach (var c in _lodChunks) c.MarkDirty();
    }

    public void Render(Camera2D camera, int viewportW, int viewportH)
    {
        if (!TileSet.ComputeUVs()) return;

        float ts = TileSize;
        float zoom = camera.Zoom;
        float screenTs = ts * zoom;

        var activeSet = TileSet;
        TileChunk[] activeChunks = _chunks;
        if (LodTileSet != null && screenTs < LodThreshold && LodTileSet.ComputeUVs())
        {
            activeSet = LodTileSet;
            _lodChunks ??= BuildChunkArray();
            activeChunks = _lodChunks;
        }

        float halfW = viewportW / 2f / zoom;
        float halfH = viewportH / 2f / zoom;
        float minX = camera.X - halfW, minY = camera.Y - halfH;
        float maxX = camera.X + halfW, maxY = camera.Y + halfH;

        int chunkPx = CHUNK_SIZE * TileSize;
        int cMinX = Math.Max(0, (int)MathF.Floor(minX / chunkPx));
        int cMinY = Math.Max(0, (int)MathF.Floor(minY / chunkPx));
        int cMaxX = Math.Min(_chunksX, (int)MathF.Ceiling(maxX / chunkPx));
        int cMaxY = Math.Min(_chunksY, (int)MathF.Ceiling(maxY / chunkPx));

        // Flush any pending sprite batches first so terrain is below.
        // (Caller controls overall layer order; this is just a safety: tilemap draws
        // immediately, sprites queued before this point already dispatched separately.)

        var gl = Gfx.Gl;
        var shader = TileChunk.GetShader();
        shader.Use();
        shader.SetUniform("u_proj", Draw.Projection);
        shader.SetUniform("u_diffuse", 0);
        activeSet.Material.DiffuseTexture!.Activate(0);

        for (int cy = cMinY; cy < cMaxY; cy++)
        for (int cx = cMinX; cx < cMaxX; cx++)
        {
            var chunk = activeChunks[cy * _chunksX + cx];
            if (chunk.Dirty) chunk.Bake(_tiles, Width, Height, TileSize, activeSet);
            chunk.Draw(gl);
        }

        gl.BindVertexArray(0);
    }

    private TileChunk[] BuildChunkArray()
    {
        var arr = new TileChunk[_chunksX * _chunksY];
        for (int cy = 0; cy < _chunksY; cy++)
        for (int cx = 0; cx < _chunksX; cx++)
            arr[cy * _chunksX + cx] = new TileChunk(cx, cy);
        return arr;
    }

    public void Dispose()
    {
        foreach (var c in _chunks) c.Dispose();
        if (_lodChunks != null) foreach (var c in _lodChunks) c.Dispose();
    }
}

internal sealed class TileChunk
{
    private readonly int _chunkX, _chunkY;
    private uint _vao, _vbo, _ebo;
    private int _indexCount;
    private bool _initialized;
    public bool Dirty { get; private set; } = true;

    private static BatchSpriteShader? _shader;
    public static BatchSpriteShader GetShader() => _shader ??= new BatchSpriteShader();

    public TileChunk(int cx, int cy) { _chunkX = cx; _chunkY = cy; }

    public void MarkDirty() => Dirty = true;

    public unsafe void Bake(short[] tiles, int mapW, int mapH, int tileSize, TileSet set)
    {
        var gl = Gfx.Gl;
        if (!_initialized)
        {
            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();
            _ebo = gl.GenBuffer();
            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            const uint stride = 9 * sizeof(float);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(5 * sizeof(float)));
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _initialized = true;
        }

        int baseX = _chunkX * TileMap.CHUNK_SIZE;
        int baseY = _chunkY * TileMap.CHUNK_SIZE;
        int endX = Math.Min(baseX + TileMap.CHUNK_SIZE, mapW);
        int endY = Math.Min(baseY + TileMap.CHUNK_SIZE, mapH);

        // Build CPU buffers for this chunk's non-empty tiles.
        var verts = new List<float>(TileMap.CHUNK_SIZE * TileMap.CHUNK_SIZE * 4 * 9);
        var idx = new List<uint>(TileMap.CHUNK_SIZE * TileMap.CHUNK_SIZE * 6);
        uint quad = 0;
        const float r = 1f, g = 1f, b = 1f, a = 1f;

        for (int y = baseY; y < endY; y++)
        for (int x = baseX; x < endX; x++)
        {
            int tileIdx = tiles[y * mapW + x];
            if (tileIdx < 0) continue;
            if (tileIdx >= set.TileCount) continue;
            var (u0, v0, u1, v1) = set.GetUV(tileIdx);
            float x1 = x * tileSize, y1 = y * tileSize;
            float x2 = x1 + tileSize, y2 = y1 + tileSize;

            verts.Add(x1); verts.Add(y1); verts.Add(0); verts.Add(u0); verts.Add(v0); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
            verts.Add(x1); verts.Add(y2); verts.Add(0); verts.Add(u0); verts.Add(v1); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
            verts.Add(x2); verts.Add(y2); verts.Add(0); verts.Add(u1); verts.Add(v1); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);
            verts.Add(x2); verts.Add(y1); verts.Add(0); verts.Add(u1); verts.Add(v0); verts.Add(r); verts.Add(g); verts.Add(b); verts.Add(a);

            uint v0i = quad * 4;
            idx.Add(v0i + 0); idx.Add(v0i + 1); idx.Add(v0i + 2);
            idx.Add(v0i + 2); idx.Add(v0i + 3); idx.Add(v0i + 0);
            quad++;
        }

        _indexCount = idx.Count;
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        if (verts.Count > 0)
        {
            var va = verts.ToArray();
            fixed (float* p = va)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(va.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            var ia = idx.ToArray();
            fixed (uint* p = ia)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(ia.Length * sizeof(uint)), p, BufferUsageARB.StaticDraw);
        }
        Dirty = false;
    }

    public unsafe void Draw(GL gl)
    {
        if (_indexCount == 0 || !_initialized) return;
        gl.BindVertexArray(_vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
        GLStats.IncDrawCalls(_indexCount * 2 / 3); // ~vertex count for stats
        GLStats.IncChunks();
    }

    public void Dispose()
    {
        if (!_initialized) return;
        var gl = Gfx.Gl;
        gl.DeleteBuffer(_vbo);
        gl.DeleteBuffer(_ebo);
        gl.DeleteVertexArray(_vao);
        _initialized = false;
    }
}
