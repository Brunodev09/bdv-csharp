using System;
using System.Collections.Generic;
using System.IO;

namespace BdvEngine.Prefabs;

/// <summary>
/// Directory scan → prefab lookup. Point at <c>assets/prefabs/</c>,
/// pass in the palette, and every <c>*.json</c> in the folder gets
/// loaded and indexed by filename stem. Missing PNGs / malformed
/// JSON are logged and skipped so one broken prefab doesn't take
/// the whole registry down.
/// </summary>
public sealed class PrefabRegistry
{
    private readonly Dictionary<string, Prefab> _byId = new();

    public IReadOnlyDictionary<string, Prefab> All => _byId;

    public Prefab? Get(string id) => _byId.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<Prefab> ByCategory(string cat)
    {
        foreach (var p in _byId.Values) if (p.Category == cat) yield return p;
    }

    public IEnumerable<Prefab> WithTag(string tag)
    {
        foreach (var p in _byId.Values)
            foreach (var t in p.Tags)
                if (t == tag) { yield return p; break; }
    }

    /// <summary>Load every prefab in <paramref name="folder"/>. The
    /// palette file (<c>palette.json</c>) is skipped by name so it
    /// can live next to the prefabs without being mistaken for one.</summary>
    public static PrefabRegistry LoadAll(string folder, PrefabPalette palette)
    {
        var r = new PrefabRegistry();
        if (!Directory.Exists(folder)) return r;
        foreach (var path in Directory.GetFiles(folder, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name == "palette") continue;
            try
            {
                var p = PrefabLoader.Load(path, palette);
                r._byId[p.Id] = p;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[prefab] {name}: {e.Message}");
            }
        }
        return r;
    }
}
