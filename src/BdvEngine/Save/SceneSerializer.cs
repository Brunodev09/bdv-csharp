using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// Reads and writes a <c>.scene.json</c> — the level as data rather than as C# source.
///
/// <para>The point of this file is the loop it enables: a scene you can load, tune (in the
/// inspector or in a text editor), save, and reload without recompiling — and which an agent can
/// author directly. Procedural content (terrain, scatter) stays in code; what lands here is the
/// placed, named, tuned content. <see cref="Save"/> can bake a generated world into a file to
/// cross that line deliberately.</para>
///
/// <para><b>Format</b> (every field optional, defaults everywhere — the same shape rule as
/// <c>Gui/UiNode.cs</c>):</para>
/// <code>
/// {
///   "version": 1,
///   "environment": { "sky": "#1A1F2E", "ambient": "#474757",
///                    "sun": { "direction": {"x":-0.5,"y":-1,"z":-0.35}, "color": "#F2EDDB" } },
///   "materials": [ { "name": "bark", "shading": "Lit", "color": "#4A3524",
///                    "texture": "assets/bark.png", "metallic": 0, "roughness": 0.5,
///                    "doubleSided": false } ],
///   "nodes": [
///     { "name": "pine", "position": {"x":12,"y":0,"z":-8}, "scale": {"x":1,"y":1.4,"z":1},
///       "mesh": { "primitive": "cube" }, "material": "bark",
///       "behaviors": [ { "type": "rotation", "name": "spin", "rotation": {"x":0,"y":1,"z":0} } ],
///       "children": [ ... ] },
///     { "name": "hero",  "model": "assets/hero.glb", "position": {"x":0,"y":2,"z":0} },
///     { "name": "lamp",  "position": {"x":3,"y":4,"z":2},
///       "light": { "type": "Point", "color": "#FFFFFF", "intensity": 8, "range": 14 } }
///   ]
/// }
/// </code>
///
/// <para><b>Round-trip contract.</b> Load → save is lossless for everything the format covers, and
/// key order is fixed (fixed sequence for known keys, alphabetical for reflected component fields)
/// so a save you didn't edit produces no git diff. Float colours quantise to 8-bit hex on the
/// first save and are stable after that — see <see cref="SceneJson"/>.</para>
///
/// <para><b>Not covered</b> (deliberately, v1): prefab references, skinned/animated model state,
/// custom shaders, and any component field whose type the reflection bridge doesn't support
/// (<see cref="SceneJson.IsSupported"/>). Unsupported components are skipped with a warning rather
/// than silently dropped.</para>
/// </summary>
public static class SceneSerializer
{
    public const int Version = 1;

    /// <summary>Primitive meshes are shared across every node with the same spec — 400 pines built
    /// from <c>{"primitive":"cube"}</c> get one GPU buffer, matching what hand-written scene code
    /// does by keeping a <c>_cube</c> field around.</summary>
    private static readonly Dictionary<string, Mesh> _meshCache = new();

    // ─────────────────────────────────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Write a scene file. <paramref name="root"/> scopes the save to one subtree (its
    /// children become the file's top-level nodes); null saves every node in the world.</summary>
    public static void Save(World world, string path, SimObject? root = null)
    {
        var nodes = (root ?? world.Scene.Root).Children;

        // Collect the materials actually referenced, so the file is self-contained.
        var materials = new SortedDictionary<string, Material>(StringComparer.Ordinal);
        foreach (var n in nodes) CollectMaterials(n, materials);

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);

            WriteEnvironment(w, world.Environment);

            w.WriteStartArray("materials");
            foreach (var m in materials.Values) WriteMaterial(w, m);
            w.WriteEndArray();

            w.WriteStartArray("nodes");
            foreach (var n in nodes) WriteNode(w, n);
            w.WriteEndArray();

            w.WriteEndObject();
        }

        // Write via a temp file + move so a crash mid-write can't destroy the level.
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, buffer.ToArray());
        File.Move(tmp, path, overwrite: true);
        Console.WriteLine($"[scene] saved {path} ({nodes.Count} nodes, {materials.Count} materials)");
    }

    private static void CollectMaterials(SimObject o, IDictionary<string, Material> into)
    {
        foreach (var c in o.Components)
        {
            if (c is MeshComponent mc) into[mc.Material.Name] = mc.Material;
            else if (c is BillboardComponent bc) into[bc.Material.Name] = bc.Material;
        }
        // A model node's children are regenerated from the .glb on load, so their materials
        // come back with them and don't belong in the scene file.
        if (o.Source != null) return;
        foreach (var ch in o.Children) CollectMaterials(ch, into);
    }

    private static void WriteEnvironment(Utf8JsonWriter w, WorldEnvironment env)
    {
        w.WriteStartObject("environment");
        w.WriteString("sky", SceneJson.ToHex(env.Sky));
        w.WriteString("ambient", SceneJson.ToHex(env.Ambient));
        w.WriteStartObject("sun");
        SceneJson.WriteVec3(w, "direction", env.Sun.Direction);
        w.WriteString("color", SceneJson.ToHex(env.Sun.Color));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteMaterial(Utf8JsonWriter w, Material m)
    {
        w.WriteStartObject();
        w.WriteString("name", m.Name);
        w.WriteString("shading", m.Shading.ToString());
        w.WriteString("color", SceneJson.ToHex(m.Color));
        // The shared 1x1 white texture is an implementation detail of flat colours, not an asset.
        if (!string.IsNullOrEmpty(m.DiffuseTextureName) && m.DiffuseTextureName != Materials3D.WhiteTexture)
            w.WriteString("texture", m.DiffuseTextureName);
        if (m.Shading == MaterialShading.Pbr)
        {
            w.WriteNumber("metallic", m.Metallic);
            w.WriteNumber("roughness", m.Roughness);
        }
        if (m.DoubleSided) w.WriteBoolean("doubleSided", true);
        w.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter w, SimObject o)
    {
        w.WriteStartObject();
        w.WriteString("name", o.Name);

        var t = o.Transform;
        if (t.Position != Vector3.Zero) SceneJson.WriteVec3(w, "position", t.Position);
        if (t.UseOrientation)
        {
            var q = t.Orientation;
            SceneJson.WriteVec4(w, "quaternion", new Vector4(q.X, q.Y, q.Z, q.W));
        }
        else if (t.Rotation != Vector3.Zero) SceneJson.WriteVec3(w, "rotation", t.Rotation);
        if (t.Scale != Vector3.One) SceneJson.WriteVec3(w, "scale", t.Scale);

        if (o.Source != null)
        {
            // Model node: reference the asset; children are re-imported on load.
            w.WriteString("model", o.Source);
            WriteGenericMembers(w, o);
            w.WriteEndObject();
            return;
        }

        foreach (var c in o.Components)
        {
            switch (c)
            {
                case MeshComponent mc:
                    if (WriteMeshSpec(w, mc.Mesh, o.Name)) w.WriteString("material", mc.Material.Name);
                    break;
                case LightComponent lc:
                    w.WriteStartObject("light");
                    w.WriteString("type", lc.Type.ToString());
                    w.WriteString("color", SceneJson.ToHex(lc.Color));
                    w.WriteNumber("intensity", lc.Intensity);
                    if (lc.Type == LightType.Point) w.WriteNumber("range", lc.Range);
                    else SceneJson.WriteVec3(w, "direction", lc.Direction);
                    w.WriteEndObject();
                    break;
                case BillboardComponent bc:
                    w.WriteStartObject("billboard");
                    w.WriteString("material", bc.Material.Name);
                    w.WriteNumber("width", bc.Width);
                    w.WriteNumber("height", bc.Height);
                    if (bc.Offset != Vector3.Zero) SceneJson.WriteVec3(w, "offset", bc.Offset);
                    w.WriteEndObject();
                    break;
            }
        }

        WriteGenericMembers(w, o);

        if (o.Children.Count > 0)
        {
            w.WriteStartArray("children");
            foreach (var ch in o.Children) WriteNode(w, ch);
            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    /// <summary>Components and behaviors that go through the builder registry — written from their
    /// live public fields (union of the data bag and the object, live values winning).</summary>
    private static void WriteGenericMembers(Utf8JsonWriter w, SimObject o)
    {
        var comps = o.Components.Where(c => ComponentManager.TryGetTypeName(c, out _)).ToList();
        if (comps.Count > 0)
        {
            w.WriteStartArray("components");
            foreach (var c in comps)
            {
                ComponentManager.TryGetTypeName(c, out var type);
                WriteMember(w, type, c.Name, (c as BaseComponent)?.Data, c);
            }
            w.WriteEndArray();
        }

        var behs = o.Behaviors.Where(b => BehaviorManager.TryGetTypeName(b, out _)).ToList();
        if (behs.Count > 0)
        {
            w.WriteStartArray("behaviors");
            foreach (var b in behs)
            {
                BehaviorManager.TryGetTypeName(b, out var type);
                WriteMember(w, type, b.Name, (b as BaseBehavior)?.Data, b);
            }
            w.WriteEndArray();
        }

        // Warn once per type about anything we'd silently drop, rather than losing it quietly.
        foreach (var c in o.Components)
            if (c is not MeshComponent && c is not LightComponent && c is not BillboardComponent
                && !ComponentManager.TryGetTypeName(c, out _))
                WarnUnserialisable(c.GetType(), "component");
        foreach (var b in o.Behaviors)
            if (!BehaviorManager.TryGetTypeName(b, out _)) WarnUnserialisable(b.GetType(), "behavior");
    }

    private static void WriteMember(Utf8JsonWriter w, string type, string name, object? data, object live)
    {
        // Data bag first, live object second — a component that copies its data into fields the
        // inspector then edits must serialise the edited value, not the construction-time one.
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        SceneJson.CollectFields(data, values);
        SceneJson.CollectFields(live, values);
        values.Remove("name");   // written explicitly below

        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteString("name", name);
        // Alphabetical: reflection order isn't guaranteed stable, and the file must be.
        foreach (var key in values.Keys.OrderBy(k => k, StringComparer.Ordinal))
            SceneJson.WriteValue(w, key, values[key]);
        w.WriteEndObject();
    }

    private static readonly HashSet<Type> _warned = new();

    private static void WarnUnserialisable(Type t, string kind)
    {
        if (!_warned.Add(t)) return;
        Console.Error.WriteLine(
            $"[scene] {kind} '{t.Name}' has no registered builder — it will not be saved. " +
            $"Register one via {(kind == "component" ? "ComponentManager" : "BehaviorManager")}" +
            $".RegisterBuilder and set {(kind == "component" ? "ComponentType" : "BehaviorType")}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Load
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Parse a scene file and build its node tree under a fresh container object. The
    /// container is returned rather than added — the caller (<see cref="World.LoadScene"/>) decides
    /// where it goes, which is what makes hot reload a swap of one child.</summary>
    public static SimObject Load(World world, string path, Func<int> nextId)
    {
        if (Gfx.Gl == null)
            throw new InvalidOperationException(
                $"SceneSerializer.Load('{path}') needs a GL context — call it from Game.Init() or later.");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var root = doc.RootElement;

        if (root.TryGetProperty("environment", out var envEl)) ReadEnvironment(envEl, world.Environment);
        if (root.TryGetProperty("materials", out var matsEl) && matsEl.ValueKind == JsonValueKind.Array)
            foreach (var m in matsEl.EnumerateArray()) ReadMaterial(m);

        var container = new SimObject(nextId(), $"scene:{Path.GetFileName(path)}");
        if (root.TryGetProperty("nodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Array)
            foreach (var n in nodesEl.EnumerateArray())
                container.AddChild(ReadNode(n, nextId));

        return container;
    }

    private static void ReadEnvironment(JsonElement e, WorldEnvironment env)
    {
        if (e.TryGetProperty("sky", out var sky)) env.Sky = SceneJson.ParseColor3(sky, env.Sky);
        if (e.TryGetProperty("ambient", out var amb)) env.Ambient = SceneJson.ParseColor3(amb, env.Ambient);
        if (!e.TryGetProperty("sun", out var sun)) return;

        var dir = sun.TryGetProperty("direction", out var d)
            ? SceneJson.ParseVec3(d, env.Sun.Direction) : env.Sun.Direction;
        var col = sun.TryGetProperty("color", out var c)
            ? SceneJson.ParseColor3(c, env.Sun.Color) : env.Sun.Color;
        env.Sun = new DirectionalLight(dir, col);
    }

    private static void ReadMaterial(JsonElement e)
    {
        if (!e.TryGetProperty("name", out var nameEl)) return;
        var name = nameEl.GetString();
        if (string.IsNullOrEmpty(name)) return;

        var color = e.TryGetProperty("color", out var c) ? SceneJson.ParseColor(c, Color.White) : Color.White;
        var texture = e.TryGetProperty("texture", out var t) ? t.GetString() : null;

        Materials3D.EnsureWhiteTexture();
        // Update in place when the material already exists, so a hot reload retunes the live
        // material instead of being ignored by MaterialManager.Register's duplicate no-op.
        if (!MaterialManager.TryPeek(name, out var mat))
        {
            mat = new Material(name, string.IsNullOrEmpty(texture) ? Materials3D.WhiteTexture : texture, color);
            MaterialManager.Register(mat);
        }
        else
        {
            mat.Color = color;
            if (!string.IsNullOrEmpty(texture) && mat.DiffuseTextureName != texture)
                mat.SetDiffuseTexture(texture);
        }

        if (e.TryGetProperty("shading", out var s) && s.ValueKind == JsonValueKind.String
            && Enum.TryParse<MaterialShading>(s.GetString(), ignoreCase: true, out var shading))
            mat.Shading = shading;
        if (e.TryGetProperty("metallic", out var me)) mat.Metallic = me.GetSingle();
        if (e.TryGetProperty("roughness", out var ro)) mat.Roughness = ro.GetSingle();
        if (e.TryGetProperty("doubleSided", out var ds)) mat.DoubleSided = ds.GetBoolean();
    }

    private static SimObject ReadNode(JsonElement e, Func<int> nextId)
    {
        string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "node" : "node";

        SimObject obj;
        if (e.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
        {
            var modelPath = modelEl.GetString()!;
            obj = GlbLoader.Load(modelPath, nextId);
            obj.Source = modelPath;
            obj.Name = name;
        }
        else
        {
            obj = new SimObject(nextId(), name);
        }

        ReadTransform(e, obj.Transform);

        if (e.TryGetProperty("mesh", out var meshEl))
        {
            string material = e.TryGetProperty("material", out var mt) ? mt.GetString() ?? "" : "";
            var mesh = ResolveMesh(meshEl);
            if (mesh != null && MaterialManager.TryPeek(material, out _))
                obj.AddComponent(new MeshComponent(mesh, material));
            else if (mesh != null)
                Console.Error.WriteLine($"[scene] node '{name}': material '{material}' not declared; mesh skipped.");
        }

        if (e.TryGetProperty("light", out var lightEl)) ReadLight(lightEl, obj);
        if (e.TryGetProperty("billboard", out var bbEl)) ReadBillboard(bbEl, obj, name);

        if (e.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
            foreach (var c in comps.EnumerateArray())
                TryAdd(() =>
                {
                    var comp = ComponentManager.ExtractComponent(c);
                    // The builder's SetFromJson covers construction params; this restores every
                    // other public field, including ones that reader ignores.
                    SceneJson.ApplyFields(comp, c);
                    SceneJson.ApplyFields((comp as BaseComponent)?.Data, c);
                    obj.AddComponent(comp);
                }, name, "component");

        if (e.TryGetProperty("behaviors", out var behs) && behs.ValueKind == JsonValueKind.Array)
            foreach (var b in behs.EnumerateArray())
                TryAdd(() =>
                {
                    var beh = BehaviorManager.ExtractBehavior(b);
                    SceneJson.ApplyFields(beh, b);
                    SceneJson.ApplyFields((beh as BaseBehavior)?.Data, b);
                    obj.AddBehavior(beh);
                }, name, "behavior");

        if (e.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
            foreach (var k in kids.EnumerateArray())
                obj.AddChild(ReadNode(k, nextId));

        return obj;
    }

    // One bad node shouldn't cost the author the rest of the level.
    private static void TryAdd(Action add, string node, string kind)
    {
        try { add(); }
        catch (Exception ex) { Console.Error.WriteLine($"[scene] node '{node}': {kind} skipped — {ex.Message}"); }
    }

    private static void ReadTransform(JsonElement e, Transform t)
    {
        if (e.TryGetProperty("position", out var p)) t.Position = SceneJson.ParseVec3(p, t.Position);
        if (e.TryGetProperty("scale", out var s)) t.Scale = SceneJson.ParseVec3(s, t.Scale);
        if (e.TryGetProperty("quaternion", out var q))
        {
            var v = SceneJson.ParseVec4(q, new Vector4(0, 0, 0, 1));
            t.Orientation = new Quaternion(v.X, v.Y, v.Z, v.W);
            t.UseOrientation = true;
        }
        else if (e.TryGetProperty("rotation", out var r)) t.Rotation = SceneJson.ParseVec3(r, t.Rotation);
    }

    private static void ReadLight(JsonElement e, SimObject obj)
    {
        var light = new LightComponent();
        if (e.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
            && Enum.TryParse<LightType>(ty.GetString(), ignoreCase: true, out var lt)) light.Type = lt;
        if (e.TryGetProperty("color", out var c)) light.Color = SceneJson.ParseColor3(c, light.Color);
        if (e.TryGetProperty("intensity", out var i)) light.Intensity = i.GetSingle();
        if (e.TryGetProperty("range", out var r)) light.Range = r.GetSingle();
        if (e.TryGetProperty("direction", out var d)) light.Direction = SceneJson.ParseVec3(d, light.Direction);
        obj.AddComponent(light);
    }

    private static void ReadBillboard(JsonElement e, SimObject obj, string node)
    {
        string material = e.TryGetProperty("material", out var m) ? m.GetString() ?? "" : "";
        if (!MaterialManager.TryPeek(material, out _))
        {
            Console.Error.WriteLine($"[scene] node '{node}': billboard material '{material}' not declared; skipped.");
            return;
        }
        float w = e.TryGetProperty("width", out var we) ? we.GetSingle() : 1f;
        float h = e.TryGetProperty("height", out var he) ? he.GetSingle() : 1f;
        var offset = e.TryGetProperty("offset", out var oe) ? SceneJson.ParseVec3(oe, Vector3.Zero) : Vector3.Zero;
        obj.AddComponent(new BillboardComponent(material, w, h, offset));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mesh specs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Write a mesh as its spec object; false if it couldn't be written. A mesh with no
    /// <see cref="Mesh.Source"/> was built by hand in game code and cannot be reconstructed from
    /// the file — that's reported loudly and the node is written without a mesh, rather than
    /// guessing a primitive and silently changing the level's geometry.</summary>
    private static bool WriteMeshSpec(Utf8JsonWriter w, Mesh mesh, string node)
    {
        var src = mesh.Source;
        if (string.IsNullOrEmpty(src))
        {
            Console.Error.WriteLine(
                $"[scene] node '{node}': mesh was built in code (no Primitives.* spec) and can't be " +
                "serialised — node saved without it. Build it via Primitives.* or load it from a .glb.");
            return false;
        }

        var (kind, args) = SplitSpec(src);
        w.WriteStartObject("mesh");
        w.WriteString("primitive", kind);
        switch (kind)
        {
            case "sphere" when args.Length >= 2:
                w.WriteNumber("segments", int.Parse(args[0], CultureInfo.InvariantCulture));
                w.WriteNumber("rings", int.Parse(args[1], CultureInfo.InvariantCulture));
                break;
            case "plane" when args.Length >= 1:
                w.WriteNumber("size", float.Parse(args[0], CultureInfo.InvariantCulture));
                break;
        }
        w.WriteEndObject();
        return true;
    }

    /// <summary>Build (or reuse) the mesh a node's <c>"mesh"</c> block asks for. Accepts the object
    /// form <c>{"primitive":"sphere","segments":24,"rings":16}</c> and the shorthand string
    /// <c>"sphere:24,16"</c>.</summary>
    private static Mesh? ResolveMesh(JsonElement e)
    {
        string spec;
        if (e.ValueKind == JsonValueKind.String)
        {
            spec = e.GetString() ?? "";
        }
        else if (e.ValueKind == JsonValueKind.Object)
        {
            string prim = e.TryGetProperty("primitive", out var p) ? p.GetString() ?? "cube" : "cube";
            spec = prim switch
            {
                "sphere" => $"sphere:{(e.TryGetProperty("segments", out var sg) ? sg.GetInt32() : 24)}," +
                            $"{(e.TryGetProperty("rings", out var rg) ? rg.GetInt32() : 16)}",
                "plane" => $"plane:{(e.TryGetProperty("size", out var sz) ? sz.GetSingle() : 1f).ToString(CultureInfo.InvariantCulture)}",
                _ => "cube",
            };
        }
        else return null;

        if (_meshCache.TryGetValue(spec, out var cached)) return cached;

        var (kind, args) = SplitSpec(spec);
        Mesh mesh = kind switch
        {
            "sphere" => Mesh.Sphere(
                args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 24,
                args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 16),
            "plane" => Mesh.Plane(args.Length > 0 ? float.Parse(args[0], CultureInfo.InvariantCulture) : 1f),
            _ => Mesh.Cube(),
        };
        mesh.Source = spec;
        _meshCache[spec] = mesh;
        return mesh;
    }

    private static (string kind, string[] args) SplitSpec(string spec)
    {
        int colon = spec.IndexOf(':');
        return colon < 0
            ? (spec, [])
            : (spec[..colon], spec[(colon + 1)..].Split(','));
    }

    /// <summary>Drop cached primitive meshes (test isolation; a level swap that wants the GPU
    /// buffers back). Live scenes still holding these meshes keep working — the cache only decides
    /// whether the NEXT load builds a new buffer or reuses one.</summary>
    public static void ClearMeshCache() => _meshCache.Clear();
}
