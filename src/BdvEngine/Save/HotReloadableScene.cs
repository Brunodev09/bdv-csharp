namespace BdvEngine;

/// <summary>
/// Wraps a <c>.scene.json</c> with a file watcher so edits to the file reload the level live —
/// the same contract as <see cref="Gui.HotReloadableUi"/>, applied to the 3D scene graph.
///
/// <para>The watcher fires on a background thread; we set a dirty flag and let the next
/// <see cref="Tick"/> (from the game's Update loop) do the swap, keeping every scene mutation on
/// the render thread as the engine expects. A save that doesn't parse keeps the last-good scene
/// and prints the error — a broken keystroke mid-edit must never cost you the level.</para>
///
/// <code>
/// private HotReloadableScene _level = null!;
/// public override void Init()   => _level = new HotReloadableScene(World, "levels/forest.scene.json");
/// public override void Update(double dt) => _level.Tick();
/// // ...and to persist edits made in-game:
/// _level.Save();
/// </code>
/// </summary>
public sealed class HotReloadableScene
{
    /// <summary>The container object holding the file's nodes. Swapped on every reload, so don't
    /// cache it across a <see cref="Tick"/> that sets <see cref="ReloadedThisFrame"/> — re-read
    /// this property instead.</summary>
    public SimObject Root { get; private set; }

    /// <summary>True for the one frame right after a reload. Anything holding references INTO the
    /// scene (a selected object, a cached player node) must re-resolve them when this fires — the
    /// old objects are detached and no longer updated.</summary>
    public bool ReloadedThisFrame { get; private set; }

    public string Path { get; }

    private readonly World _world;
    private FileSystemWatcher? _watcher;
    private DateTime _lastReload = DateTime.MinValue;
    private volatile bool _dirty;

    public HotReloadableScene(World world, string path)
    {
        _world = world;
        Path = path;
        Root = world.LoadScene(path);
        _lastReload = DateTime.UtcNow;
        TryStartWatcher();
    }

    /// <summary>Poll from the game's Update loop. Cheap when the file hasn't changed. Debounced,
    /// because most editors emit two change events per save.</summary>
    public void Tick()
    {
        ReloadedThisFrame = false;
        if (!_dirty) return;
        if ((DateTime.UtcNow - _lastReload).TotalMilliseconds < 200) return;
        _dirty = false;
        Reload();
    }

    /// <summary>Reload now — for when the file didn't change but its dependencies did.</summary>
    public void ForceReload() { _dirty = false; Reload(); }

    /// <summary>Write the live scene back to its file (what the Phase 2 inspector's Save button
    /// calls). Suppresses the watcher for this write so saving doesn't trigger a reload of what we
    /// just saved.</summary>
    public void Save()
    {
        _world.SaveScene(Path, Root);
        _lastReload = DateTime.UtcNow;
        _dirty = false;
    }

    private void Reload()
    {
        _lastReload = DateTime.UtcNow;
        try
        {
            Root = _world.ReloadScene(Path, Root);
            ReloadedThisFrame = true;
            Console.WriteLine($"[scene] reloaded {Path}");
        }
        catch (Exception e)
        {
            // Keep the last-good scene. A half-written or malformed file is a transient state
            // during editing, not a reason to blank the level.
            Console.Error.WriteLine($"[scene] reload failed for {Path}: {e.Message}");
        }
    }

    private void TryStartWatcher()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            var file = System.IO.Path.GetFileName(Path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;
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
            Console.Error.WriteLine($"[scene] watcher unavailable for {Path}: {e.Message}");
        }
    }
}
