using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

public enum SpriteLayer { Ground, Object, UIBack, UI }

/// <summary>
/// Layered sprite batcher.
///
/// Layers flush in order: Ground → Object → UI.
///   • Ground / UI: per-texture batch, insertion order. One draw per (shader × texture).
///   • Object: per-quad entries with sortY; sorted by Y on flush, run-length batched by
///     texture to produce stable depth ordering for trees/units/buildings.
///
/// All batches use indexed quads (4 verts + 6 indices per quad).
/// Vertex layout: x, y, z, u, v, r, g, b, a (9 floats / 36 bytes).
/// </summary>
public static class SpriteBatcher
{
    private const int FLOATS_PER_VERT = 9;
    private const int FLOATS_PER_QUAD = 4 * FLOATS_PER_VERT; // 36

    private sealed class Batch
    {
        public List<float> Verts = new();
        public List<uint>  Indices = new();
        public uint NextBase;
        public Texture Texture = null!;
        public Material? Material;
    }

    private sealed class ObjectEntry
    {
        public float SortY;
        public string Key = null!;
        public Texture Texture = null!;
        public Material? Material;
        public float[] Verts = null!; // 36 floats
    }

    private static readonly Dictionary<string, Batch> _groundBatches = new();
    private static readonly List<Batch> _groundOrder = new();
    private static readonly Dictionary<string, Batch> _uiBackBatches = new();
    private static readonly List<Batch> _uiBackOrder = new();
    private static readonly Dictionary<string, Batch> _uiBatches = new();
    private static readonly List<Batch> _uiOrder = new();
    private static readonly List<ObjectEntry> _objectEntries = new();

    private static uint _vao;
    private static uint _vbo;
    private static uint _ebo;
    private static BatchSpriteShader? _batchShader;
    private static bool _initialized;
    private static Material? _solidMat;

    /// <summary>1×1 white material — useful for solid colored quads pushed into the
    /// regular batcher (so they share insertion-order with sprites/text).</summary>
    private static Material GetSolidMaterial()
    {
        if (_solidMat != null) return _solidMat;
        const string texName = "__sprite_solid__";
        var tex = Texture.CreateBlank(texName, 1, 1);
        Span<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        tex.UploadRgba(1, 1, white);
        TextureManager.Register(texName, tex);
        _solidMat = new Material("__sprite_solid_mat__", texName, Color.White);
        MaterialManager.Register(_solidMat);
        return _solidMat;
    }

    /// <summary>Push a solid-color quad through the batcher. Layer/sortY behave the
    /// same as DrawTexture. Use for UI fills that must respect SpriteBatcher draw
    /// order (i.e. panels that sit *behind* their child labels/images).</summary>
    public static void DrawSolid(float x, float y, float width, float height, Color color,
        SpriteLayer layer = SpriteLayer.UI, float sortY = 0f)
        => DrawTextureUV(GetSolidMaterial(), 0f, 0f, 1f, 1f, x, y, width, height, color, layer, sortY);

    private static unsafe void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        var gl = Gfx.Gl;
        _batchShader = new BatchSpriteShader();
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        const uint stride = FLOATS_PER_VERT * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(5 * sizeof(float)));
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Queue a sprite quad. Expects 6 input vertices in the layout produced by Sprite.Load
    /// (BL, TL, TR, TR-dup, BR, BL-dup) and emits indexed 4-vert geometry.
    /// </summary>
    public static void Push(IList<Vertex> vertices, Material material, Matrix4x4 worldMatrix,
        SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        var tex = material.DiffuseTexture;
        if (tex == null || vertices.Count < 4) return;

        string shaderName = material.HasCustomShader ? material.CustomShader!.Name : "__default_batch__";
        string key = shaderName + ":" + material.DiffuseTextureName;

        // Transform 4 unique corners (input verts 0, 1, 2, 4 — see Sprite.Load).
        float m0 = worldMatrix.M11, m1 = worldMatrix.M12, m2 = worldMatrix.M13;
        float m4 = worldMatrix.M21, m5 = worldMatrix.M22, m6 = worldMatrix.M23;
        float m8 = worldMatrix.M31, m9 = worldMatrix.M32, m10 = worldMatrix.M33;
        float m12 = worldMatrix.M41, m13 = worldMatrix.M42, m14 = worldMatrix.M43;
        var c = material.Color;
        float r = c.RFloat, g = c.GFloat, b = c.BFloat, a = c.AFloat;

        Span<float> quad = stackalloc float[FLOATS_PER_QUAD];
        int[] cornerIdx = { 0, 1, 2, 4 };
        for (int i = 0; i < 4; i++)
        {
            var v = vertices[cornerIdx[i]];
            float wx = m0 * v.X + m4 * v.Y + m8  * v.Z + m12;
            float wy = m1 * v.X + m5 * v.Y + m9  * v.Z + m13;
            float wz = m2 * v.X + m6 * v.Y + m10 * v.Z + m14;
            int o = i * FLOATS_PER_VERT;
            quad[o + 0] = wx; quad[o + 1] = wy; quad[o + 2] = wz;
            quad[o + 3] = v.U; quad[o + 4] = v.V;
            quad[o + 5] = r;  quad[o + 6] = g;  quad[o + 7] = b; quad[o + 8] = a;
        }

        EmitQuad(quad, key, tex, material.HasCustomShader ? material : null, layer, sortY);
    }

    /// <summary>Queue an axis-aligned textured quad sampling a sub-rect of a spritesheet.</summary>
    public static void DrawTexture(Material material,
        int srcCol, int srcRow, int gridCols, int gridRows,
        float x, float y, float width, float height,
        Color? tint = null, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        float u0 = srcCol / (float)gridCols;
        float v0 = srcRow / (float)gridRows;
        float u1 = (srcCol + 1) / (float)gridCols;
        float v1 = (srcRow + 1) / (float)gridRows;
        DrawTextureUV(material, u0, v0, u1, v1, x, y, width, height, tint, layer, sortY);
    }

    /// <summary>Queue an axis-aligned textured quad sampling an explicit UV rectangle.</summary>
    public static void DrawTextureUV(Material material,
        float u0, float v0, float u1, float v1,
        float x, float y, float width, float height,
        Color? tint = null, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        var tex = material.DiffuseTexture;
        if (tex == null) return;

        string key = "__default_batch__:" + material.DiffuseTextureName;
        var c = tint ?? Color.White;
        float r = c.RFloat, g = c.GFloat, b = c.BFloat, a = c.AFloat;
        float x2 = x + width, y2 = y + height;

        Span<float> quad = stackalloc float[FLOATS_PER_QUAD];
        // BL
        quad[0]  = x;  quad[1]  = y;  quad[2]  = 0; quad[3]  = u0; quad[4]  = v0; quad[5]  = r; quad[6]  = g; quad[7]  = b; quad[8]  = a;
        // TL
        quad[9]  = x;  quad[10] = y2; quad[11] = 0; quad[12] = u0; quad[13] = v1; quad[14] = r; quad[15] = g; quad[16] = b; quad[17] = a;
        // TR
        quad[18] = x2; quad[19] = y2; quad[20] = 0; quad[21] = u1; quad[22] = v1; quad[23] = r; quad[24] = g; quad[25] = b; quad[26] = a;
        // BR
        quad[27] = x2; quad[28] = y;  quad[29] = 0; quad[30] = u1; quad[31] = v0; quad[32] = r; quad[33] = g; quad[34] = b; quad[35] = a;

        EmitQuad(quad, key, tex, null, layer, sortY);
    }

    private static void EmitQuad(ReadOnlySpan<float> quad, string key, Texture tex, Material? mat,
        SpriteLayer layer, float sortY)
    {
        if (layer == SpriteLayer.Object)
        {
            _objectEntries.Add(new ObjectEntry
            {
                SortY = sortY, Key = key, Texture = tex, Material = mat,
                Verts = quad.ToArray(),
            });
            return;
        }

        Dictionary<string, Batch> dict;
        List<Batch> order;
        switch (layer)
        {
            case SpriteLayer.UI:      dict = _uiBatches;     order = _uiOrder;     break;
            case SpriteLayer.UIBack:  dict = _uiBackBatches; order = _uiBackOrder; break;
            default:                  dict = _groundBatches; order = _groundOrder; break;
        }

        if (!dict.TryGetValue(key, out var batch))
        {
            batch = new Batch { Texture = tex, Material = mat };
            dict[key] = batch;
            order.Add(batch);
        }
        AppendQuadToBatch(batch, quad);
    }

    private static void AppendQuadToBatch(Batch batch, ReadOnlySpan<float> quad)
    {
        var v = batch.Verts;
        for (int i = 0; i < FLOATS_PER_QUAD; i++) v.Add(quad[i]);
        uint b = batch.NextBase;
        batch.Indices.Add(b + 0); batch.Indices.Add(b + 1); batch.Indices.Add(b + 2);
        batch.Indices.Add(b + 2); batch.Indices.Add(b + 3); batch.Indices.Add(b + 0);
        batch.NextBase = b + 4;
    }

    /// <summary>Submit all queued sprites for the frame.</summary>
    public static unsafe void Flush()
    {
        bool anything = _groundOrder.Count > 0 || _objectEntries.Count > 0
                        || _uiBackOrder.Count > 0 || _uiOrder.Count > 0;
        if (!anything) return;
        EnsureInit();

        FlushBatchList(_groundOrder);
        FlushObjectLayer();
        FlushBatchList(_uiBackOrder);
        FlushBatchList(_uiOrder);

        Gfx.Gl.BindVertexArray(0);
    }

    private static unsafe void FlushBatchList(List<Batch> order)
    {
        var gl = Gfx.Gl;
        foreach (var batch in order)
        {
            if (batch.Indices.Count == 0) continue;
            BindShaderForBatch(batch);
            batch.Texture.Activate(0);

            UploadAndDraw(batch.Verts, batch.Indices);

            batch.Verts.Clear();
            batch.Indices.Clear();
            batch.NextBase = 0;
        }
    }

    private static unsafe void FlushObjectLayer()
    {
        if (_objectEntries.Count == 0) return;
        _objectEntries.Sort((a, b) => a.SortY.CompareTo(b.SortY));

        var gl = Gfx.Gl;
        var verts = new List<float>(_objectEntries.Count * FLOATS_PER_QUAD);
        var idx   = new List<uint>(_objectEntries.Count * 6);
        uint nextBase = 0;
        string? curKey = null;
        Texture? curTex = null;
        Material? curMat = null;

        foreach (var e in _objectEntries)
        {
            if (curKey != e.Key)
            {
                if (verts.Count > 0)
                {
                    BindShaderForObject(curTex!, curMat);
                    UploadAndDraw(verts, idx);
                    verts.Clear(); idx.Clear(); nextBase = 0;
                }
                curKey = e.Key; curTex = e.Texture; curMat = e.Material;
            }
            for (int i = 0; i < FLOATS_PER_QUAD; i++) verts.Add(e.Verts[i]);
            idx.Add(nextBase + 0); idx.Add(nextBase + 1); idx.Add(nextBase + 2);
            idx.Add(nextBase + 2); idx.Add(nextBase + 3); idx.Add(nextBase + 0);
            nextBase += 4;
        }

        if (verts.Count > 0)
        {
            BindShaderForObject(curTex!, curMat);
            UploadAndDraw(verts, idx);
        }
        _objectEntries.Clear();
    }

    private static void BindShaderForBatch(Batch batch)
    {
        Shader shader;
        if (batch.Material != null && batch.Material.HasCustomShader)
        {
            shader = batch.Material.CustomShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
            batch.Material.ApplyUniforms(shader);
        }
        else
        {
            shader = _batchShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
        }
        shader.SetUniform("u_diffuse", 0);
    }

    private static void BindShaderForObject(Texture tex, Material? mat)
    {
        Shader shader;
        if (mat != null && mat.HasCustomShader)
        {
            shader = mat.CustomShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
            mat.ApplyUniforms(shader);
        }
        else
        {
            shader = _batchShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
        }
        shader.SetUniform("u_diffuse", 0);
        tex.Activate(0);
    }

    private static unsafe void UploadAndDraw(List<float> verts, List<uint> indices)
    {
        var gl = Gfx.Gl;
        var va = verts.ToArray();
        var ia = indices.ToArray();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = va)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(va.Length * sizeof(float)), p, BufferUsageARB.DynamicDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* p = ia)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(ia.Length * sizeof(uint)), p, BufferUsageARB.DynamicDraw);
        gl.DrawElements(PrimitiveType.Triangles, (uint)ia.Length, DrawElementsType.UnsignedInt, (void*)0);
        GLStats.IncDrawCalls(ia.Length);
    }
}
