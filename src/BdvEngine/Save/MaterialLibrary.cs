using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// A standalone <c>materials.json</c> — a shared palette, separate from any one scene.
///
/// <para>Scene files already carry the materials they use, which keeps them self-contained. A
/// library is for the other case: a palette shared by several scenes, where retuning "bark" should
/// change it everywhere rather than in one level. Same JSON shape either way, so a block can be
/// moved between the two by copy-paste.</para>
///
/// <para>Loading is an UPDATE, not a replace: a material that already exists is retuned in place,
/// so every mesh already holding it picks up the change. That is what makes hot reload work at
/// all — swapping in a new Material object would leave every existing MeshComponent pointing at
/// the old one.</para>
/// </summary>
public static class MaterialLibrary
{
    public const int Version = 1;

    /// <summary>Read a palette file and register (or retune) every material in it.</summary>
    public static void Load(string path)
    {
        if (Gfx.Gl == null)
            throw new InvalidOperationException(
                $"MaterialLibrary.Load('{path}') needs a GL context — call it from Game.Init() or later.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = doc.RootElement;
        // Accept the wrapped form and a bare array, so a "materials" block lifted out of a scene
        // file works as a library without editing.
        var arr = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("materials", out var m) ? m
                : default;
        if (arr.ValueKind != JsonValueKind.Array) return;

        int n = 0;
        foreach (var el in arr.EnumerateArray()) { SceneSerializer.ReadMaterialJson(el); n++; }
        Console.WriteLine($"[materials] loaded {n} from {path}");
    }

    /// <summary>Write the named materials out as a palette. Names that aren't registered are
    /// skipped with a warning rather than silently omitted.</summary>
    public static void Save(string path, IEnumerable<string> names)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteStartArray("materials");
            // Sorted so the file is stable and diffable regardless of registration order.
            foreach (var name in names.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!MaterialManager.TryPeek(name, out var mat))
                {
                    Console.Error.WriteLine($"[materials] '{name}' is not registered; skipped.");
                    continue;
                }
                SceneSerializer.WriteMaterialJson(w, mat);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, buffer.ToArray());
        File.Move(tmp, path, overwrite: true);
        Console.WriteLine($"[materials] saved {path}");
    }
}

/// <summary>
/// Watches a <c>materials.json</c> and retunes the live materials when it changes — the same
/// contract as <see cref="HotReloadableScene"/>. Simpler than a scene reload, because materials
/// update in place: nothing in the scene graph has to be rebuilt.
/// </summary>
public sealed class HotReloadableMaterials
{
    public string Path { get; }
    public bool ReloadedThisFrame { get; private set; }

    private FileSystemWatcher? _watcher;
    private DateTime _lastReload = DateTime.MinValue;
    private volatile bool _dirty;

    public HotReloadableMaterials(string path)
    {
        Path = path;
        Reload();
        TryStartWatcher();
    }

    /// <summary>Poll from the game's Update loop. Debounced — editors commonly emit two change
    /// events per save.</summary>
    public void Tick()
    {
        ReloadedThisFrame = false;
        if (!_dirty) return;
        if ((DateTime.UtcNow - _lastReload).TotalMilliseconds < 200) return;
        _dirty = false;
        Reload();
        ReloadedThisFrame = true;
    }

    private void Reload()
    {
        _lastReload = DateTime.UtcNow;
        try { MaterialLibrary.Load(Path); }
        catch (Exception e)
        {
            // Keep the last-good palette: a half-written file mid-edit must not blank the scene.
            Console.Error.WriteLine($"[materials] reload failed for {Path}: {e.Message}");
        }
    }

    private void TryStartWatcher()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            var file = System.IO.Path.GetFileName(Path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;
            // No directory means the file is missing, which Reload() has already reported. A
            // watcher on a path that doesn't exist can never fire, so throwing here would only
            // add a second line about the same single cause.
            if (!Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, __) => _dirty = true;
            _watcher.Created += (_, __) => _dirty = true;
            _watcher.Renamed += (_, __) => _dirty = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[materials] watcher unavailable for {Path}: {e.Message}");
        }
    }
}
