using System.Numerics;
using System.Text.Json;
using StbImageSharp;

namespace BdvEngine;

/// <summary>
/// Minimal, dependency-free glTF-binary (<c>.glb</c>) loader (Q1 — hand-rolled, no external
/// packages, AOT/iOS-safe). v1 supports the GLB container (JSON + BIN chunks), the node hierarchy
/// → <see cref="SimObject"/> tree, indexed mesh primitives (POSITION / NORMAL / TEXCOORD_0), and
/// <c>pbrMetallicRoughness</c> base-colour factor + embedded base-colour texture.
///
/// NOT yet (add later behind the same call): skins, skeletal / morph animation, sparse accessors,
/// external / data-URI buffers, and KHR extensions.
/// </summary>
public static class GlbLoader
{
    private const uint Magic     = 0x46546C67; // "glTF"
    private const uint ChunkJson = 0x4E4F534A; // "JSON"
    private const uint ChunkBin  = 0x004E4942; // "BIN\0"

    /// <summary>Load a <c>.glb</c> file into a SimObject tree (one wrapper root over the scene's
    /// nodes). <paramref name="nextId"/> supplies unique object ids (usually the World's counter).</summary>
    public static SimObject Load(string path, Func<int> nextId)
        => Parse(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path), nextId);

    public static SimObject Parse(byte[] glb, string modelName, Func<int> nextId)
    {
        var (json, bin) = ReadChunks(glb);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessors   = Arr(root, "accessors");
        var bufferViews = Arr(root, "bufferViews");
        var meshes      = Arr(root, "meshes");
        var nodes       = Arr(root, "nodes");
        var materials   = Arr(root, "materials");
        var textures    = Arr(root, "textures");
        var images      = Arr(root, "images");

        // ── materials → registered engine material names ──
        var matNames = new string[materials.Length];
        for (int i = 0; i < materials.Length; i++)
            matNames[i] = BuildMaterial(modelName, i, materials[i], textures, images, bufferViews, bin);

        // ── meshes → primitives (each an engine Mesh + material name) ──
        var meshPrims = new List<(Mesh mesh, string mat)>[meshes.Length];
        for (int m = 0; m < meshes.Length; m++)
        {
            var prims = new List<(Mesh, string)>();
            foreach (var prim in Arr(meshes[m], "primitives"))
                prims.Add(BuildPrimitive(prim, accessors, bufferViews, bin, matNames, modelName));
            meshPrims[m] = prims;
        }

        // ── nodes → SimObjects ──
        var sims = new SimObject[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            var nj = nodes[i];
            string name = nj.TryGetProperty("name", out var nm) ? nm.GetString() ?? $"node{i}" : $"node{i}";
            var so = new SimObject(nextId(), name);
            ApplyTransform(so.Transform, nj);
            if (nj.TryGetProperty("mesh", out var mi))
                foreach (var (mesh, mat) in meshPrims[mi.GetInt32()])
                    so.AddComponent(new MeshComponent(mesh, mat));
            sims[i] = so;
        }
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i].TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray())
                    sims[i].AddChild(sims[c.GetInt32()]);

        // ── scene root → one wrapper SimObject ──
        var wrapper = new SimObject(nextId(), modelName);
        var scenes = Arr(root, "scenes");
        int sceneIdx = root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0;
        if (scenes.Length > sceneIdx && scenes[sceneIdx].TryGetProperty("nodes", out var rootNodes))
        {
            foreach (var rn in rootNodes.EnumerateArray())
                wrapper.AddChild(sims[rn.GetInt32()]);
        }
        else
        {
            // No scene list: attach every node that isn't someone's child.
            var isChild = new bool[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].TryGetProperty("children", out var ch))
                    foreach (var c in ch.EnumerateArray()) isChild[c.GetInt32()] = true;
            for (int i = 0; i < nodes.Length; i++)
                if (!isChild[i]) wrapper.AddChild(sims[i]);
        }
        return wrapper;
    }

    // ──────────────────────────────────────────────────────────── GLB container
    private static (byte[] json, byte[] bin) ReadChunks(byte[] glb)
    {
        if (glb.Length < 12 || BitConverter.ToUInt32(glb, 0) != Magic)
            throw new InvalidDataException("Not a GLB file (bad magic).");
        // header: magic(4) version(4) length(4)
        int pos = 12;
        byte[]? json = null, bin = null;
        while (pos + 8 <= glb.Length)
        {
            uint len  = BitConverter.ToUInt32(glb, pos);
            uint type = BitConverter.ToUInt32(glb, pos + 4);
            pos += 8;
            var data = new byte[len];
            Array.Copy(glb, pos, data, 0, (int)len);
            pos += (int)len;
            if (type == ChunkJson) json = TrimJson(data);
            else if (type == ChunkBin) bin = data;
        }
        if (json == null) throw new InvalidDataException("GLB has no JSON chunk.");
        return (json, bin ?? Array.Empty<byte>());
    }

    // The JSON chunk is 4-byte padded — spec-compliant files use spaces, but be defensive and
    // strip any trailing whitespace / NUL padding so JsonDocument.Parse sees a clean value.
    private static byte[] TrimJson(byte[] d)
    {
        int end = d.Length;
        while (end > 0 && (d[end - 1] is 0x00 or 0x20 or (byte)'\n' or (byte)'\r' or (byte)'\t')) end--;
        if (end == d.Length) return d;
        var r = new byte[end];
        Array.Copy(d, r, end);
        return r;
    }

    // ──────────────────────────────────────────────────────────── primitives
    private static (Mesh, string) BuildPrimitive(
        JsonElement prim, JsonElement[] accessors, JsonElement[] bufferViews, byte[] bin,
        string[] matNames, string modelName)
    {
        var attrs = prim.GetProperty("attributes");
        float[] pos  = ReadFloats(attrs.GetProperty("POSITION").GetInt32(), accessors, bufferViews, bin, 3);
        int vcount = pos.Length / 3;
        float[]? norm = attrs.TryGetProperty("NORMAL", out var n)     ? ReadFloats(n.GetInt32(), accessors, bufferViews, bin, 3) : null;
        float[]? uv   = attrs.TryGetProperty("TEXCOORD_0", out var t) ? ReadFloats(t.GetInt32(), accessors, bufferViews, bin, 2) : null;

        var verts = new float[vcount * Mesh.FloatsPerVertex];
        for (int i = 0; i < vcount; i++)
        {
            int o = i * 8;
            verts[o + 0] = pos[i * 3 + 0]; verts[o + 1] = pos[i * 3 + 1]; verts[o + 2] = pos[i * 3 + 2];
            verts[o + 3] = norm != null ? norm[i * 3 + 0] : 0f;
            verts[o + 4] = norm != null ? norm[i * 3 + 1] : 1f;
            verts[o + 5] = norm != null ? norm[i * 3 + 2] : 0f;
            verts[o + 6] = uv != null ? uv[i * 2 + 0] : 0f;
            verts[o + 7] = uv != null ? uv[i * 2 + 1] : 0f;
        }

        uint[] idx = prim.TryGetProperty("indices", out var ip)
            ? ReadIndices(ip.GetInt32(), accessors, bufferViews, bin)
            : Trivial(vcount);

        Mesh mesh;
        if (vcount <= 65536)
        {
            var s = new ushort[idx.Length];
            for (int i = 0; i < idx.Length; i++) s[i] = (ushort)idx[i];
            mesh = new Mesh(verts, s);
        }
        else mesh = new Mesh(verts, idx);

        string mat;
        if (prim.TryGetProperty("material", out var mp)) mat = matNames[mp.GetInt32()];
        else mat = DefaultWhite();
        return (mesh, mat);
    }

    private static uint[] Trivial(int n)
    {
        var a = new uint[n];
        for (int i = 0; i < n; i++) a[i] = (uint)i;
        return a;
    }

    // ──────────────────────────────────────────────────────────── accessors
    private static float[] ReadFloats(int accessorIdx, JsonElement[] accessors, JsonElement[] bufferViews, byte[] bin, int comps)
    {
        var acc = accessors[accessorIdx];
        int count = acc.GetProperty("count").GetInt32();
        int compType = acc.GetProperty("componentType").GetInt32();     // 5126 = FLOAT expected
        if (compType != 5126)
            throw new NotSupportedException($"glb: non-float attribute accessor (componentType {compType}) not supported in v1.");
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;

        var bv = bufferViews[acc.GetProperty("bufferView").GetInt32()];
        int bvOffset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int stride = bv.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : comps * 4;

        int baseOff = bvOffset + accOffset;
        var outp = new float[count * comps];
        for (int i = 0; i < count; i++)
            for (int c = 0; c < comps; c++)
                outp[i * comps + c] = BitConverter.ToSingle(bin, baseOff + i * stride + c * 4);
        return outp;
    }

    private static uint[] ReadIndices(int accessorIdx, JsonElement[] accessors, JsonElement[] bufferViews, byte[] bin)
    {
        var acc = accessors[accessorIdx];
        int count = acc.GetProperty("count").GetInt32();
        int compType = acc.GetProperty("componentType").GetInt32();     // 5121 ubyte / 5123 ushort / 5125 uint
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        var bv = bufferViews[acc.GetProperty("bufferView").GetInt32()];
        int bvOffset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int baseOff = bvOffset + accOffset;

        var outp = new uint[count];
        int size = compType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new NotSupportedException($"glb: index componentType {compType}") };
        for (int i = 0; i < count; i++)
        {
            int p = baseOff + i * size;
            outp[i] = size switch
            {
                1 => bin[p],
                2 => BitConverter.ToUInt16(bin, p),
                _ => BitConverter.ToUInt32(bin, p),
            };
        }
        return outp;
    }

    // ──────────────────────────────────────────────────────────── transforms
    private static void ApplyTransform(Transform tr, JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var mEl))
        {
            Span<float> g = stackalloc float[16];
            int i = 0;
            foreach (var v in mEl.EnumerateArray()) g[i++] = v.GetSingle();
            // glTF stores column-major (column-vector math); loading the 16 floats straight into
            // System.Numerics' row-major fields yields the row-vector equivalent the engine uses.
            var s = new Matrix4x4(
                g[0], g[1], g[2], g[3],
                g[4], g[5], g[6], g[7],
                g[8], g[9], g[10], g[11],
                g[12], g[13], g[14], g[15]);
            if (Matrix4x4.Decompose(s, out var scale, out var rot, out var trans))
            {
                tr.Position = trans;
                tr.Orientation = rot;
                tr.UseOrientation = true;
                tr.Scale = scale;
            }
            return;
        }

        if (node.TryGetProperty("translation", out var tEl)) tr.Position = Vec3(tEl);
        if (node.TryGetProperty("scale", out var sEl)) tr.Scale = Vec3(sEl);
        if (node.TryGetProperty("rotation", out var rEl))
        {
            Span<float> q = stackalloc float[4];
            int i = 0;
            foreach (var v in rEl.EnumerateArray()) q[i++] = v.GetSingle();
            tr.Orientation = new Quaternion(q[0], q[1], q[2], q[3]);   // glTF [x,y,z,w] == System.Numerics
            tr.UseOrientation = true;
        }
    }

    private static Vector3 Vec3(JsonElement a)
    {
        Span<float> v = stackalloc float[3];
        int i = 0;
        foreach (var e in a.EnumerateArray()) { if (i < 3) v[i] = e.GetSingle(); i++; }
        return new Vector3(v[0], v[1], v[2]);
    }

    // ──────────────────────────────────────────────────────────── materials
    private static string BuildMaterial(
        string modelName, int index, JsonElement mat,
        JsonElement[] textures, JsonElement[] images, JsonElement[] bufferViews, byte[] bin)
    {
        string name = $"{modelName}_mat{index}";
        if (Registered(name)) return name;

        var color = Color.White;
        string? textureName = null;
        float metallic = 1f, roughness = 1f;   // glTF defaults
        bool doubleSided = mat.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean();

        if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr))
        {
            if (pbr.TryGetProperty("baseColorFactor", out var bcf))
            {
                Span<float> c = stackalloc float[4] { 1, 1, 1, 1 };
                int i = 0;
                foreach (var v in bcf.EnumerateArray()) { if (i < 4) c[i] = v.GetSingle(); i++; }
                color = new Color(F2B(c[0]), F2B(c[1]), F2B(c[2]), F2B(c[3]));
            }
            if (pbr.TryGetProperty("metallicFactor", out var mf)) metallic = mf.GetSingle();
            if (pbr.TryGetProperty("roughnessFactor", out var rf)) roughness = rf.GetSingle();
            if (pbr.TryGetProperty("baseColorTexture", out var bct) && bct.TryGetProperty("index", out var ti))
                textureName = LoadTexture($"{name}_tex", textures[ti.GetInt32()], images, bufferViews, bin);
        }

        Material m;
        if (textureName != null) { m = new Material(name, textureName, color); MaterialManager.Register(m); }
        else { Materials3D.Solid(name, color); m = MaterialManager.Get(name); }   // shared white texture
        m.Shading = MaterialShading.Pbr;      // imported glTF materials render PBR-lite
        m.Metallic = metallic;
        m.Roughness = roughness;
        m.DoubleSided = doubleSided;
        return name;
    }

    private static string? LoadTexture(string name, JsonElement texture, JsonElement[] images, JsonElement[] bufferViews, byte[] bin)
    {
        if (!texture.TryGetProperty("source", out var srcEl)) return null;
        var image = images[srcEl.GetInt32()];
        if (!image.TryGetProperty("bufferView", out var bvEl)) return null;   // v1: embedded images only

        var bv = bufferViews[bvEl.GetInt32()];
        int off = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int len = bv.GetProperty("byteLength").GetInt32();
        var imgBytes = new byte[len];
        Array.Copy(bin, off, imgBytes, 0, len);

        var decoded = ImageResult.FromMemory(imgBytes, ColorComponents.RedGreenBlueAlpha);
        var tex = Texture.CreateBlank(name, decoded.Width, decoded.Height);
        tex.UploadRgba(decoded.Width, decoded.Height, decoded.Data);
        TextureManager.Register(name, tex);
        return name;
    }

    // ──────────────────────────────────────────────────────────── helpers
    private static byte F2B(float f) => (byte)Math.Clamp((int)(f * 255f + 0.5f), 0, 255);

    private static readonly HashSet<string> _registered = new();
    private static bool Registered(string name) => !_registered.Add(name);

    private static string DefaultWhite()
    {
        const string n = "__glb_white";
        Materials3D.Solid(n, Color.White);   // idempotent
        return n;
    }

    private static JsonElement[] Arr(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
}
