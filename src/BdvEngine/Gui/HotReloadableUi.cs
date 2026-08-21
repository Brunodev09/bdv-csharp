using System;
using System.IO;

namespace BdvEngine.Gui;

/// <summary>
/// Wraps a JSON-driven UI panel with a file watcher so edits to the
/// JSON reload the widget tree live. Game code stashes the returned
/// <see cref="Container"/> in its Root layer once; the container's
/// CHILDREN get swapped whenever the file changes, so external
/// references (e.g. "the panel I show/hide on tab click") stay stable
/// across reloads.
///
/// The file watcher fires on a background thread; we set a dirty
/// flag and let the next <see cref="Tick"/> call (from the main
/// thread's game loop) rebuild the tree. This keeps all UI mutations
/// on the render thread as the engine expects.
///
/// Usage:
/// <code>
/// var events = new UiEventRegistry()
///     .Register("closePawn", DeselectPawn)
///     .Register("openWork",  () => ToggleTab(BottomTab.Work));
/// var reload = new HotReloadableUi("assets/ui/pawn_panel.json", font, events);
/// root.Add(reload.Container, UiLayer.Panels);
/// // in Update:
/// reload.Tick();
/// </code>
/// </summary>
public sealed class HotReloadableUi
{
    /// <summary>Root panel — always the same instance across reloads.
    /// Children get swapped in-place when the file changes.</summary>
    public Panel Container { get; }

    private readonly string _path;
    private readonly Font? _font;
    private readonly UiEventRegistry _events;
    private FileSystemWatcher? _watcher;
    private DateTime _lastReload = DateTime.MinValue;
    private volatile bool _dirty;

    public HotReloadableUi(string path, Font? font, UiEventRegistry events)
    {
        _path = path;
        _font = font;
        _events = events;
        // Non-pickable transparent container — the loaded root sits
        // inside it as the first child. Rect is 0×0 by default; the
        // child's own rect drives visibility.
        // Container stretches to fill its parent (a UiLayer) so
        // children with anchors like BottomCenter / TopRight resolve
        // against the full viewport as they'd expect. No background,
        // no border, non-pickable — purely a hosting shell.
        Container = new Panel(0, 0, 0, 0) { Pickable = false };
        Container.AnchorTo(Anchor.StretchAll);
        Container.NoClip();

        Reload();
        TryStartWatcher();
    }

    /// <summary>Poll from the game's Update loop. Cheap when the file
    /// hasn't changed. When the watcher has flagged a change, we
    /// wait for a short debounce window (some editors emit two
    /// change events per save) and then rebuild.</summary>
    public void Tick()
    {
        ReloadedThisFrame = false;
        if (!_dirty) return;
        if ((DateTime.UtcNow - _lastReload).TotalMilliseconds < 200) return;
        _dirty = false;
        Reload();
        ReloadedThisFrame = true;
    }

    /// <summary>Set to true for one frame right after the panel was
    /// rebuilt from disk. Consumers cache child references from
    /// <see cref="Find{T}"/>; check this flag after every
    /// <see cref="Tick"/> and re-resolve any cached refs when it
    /// fires — otherwise the old (now-detached) widget stays
    /// wired.</summary>
    public bool ReloadedThisFrame { get; private set; }

    /// <summary>Manual reload. Use when the file didn't change but
    /// the runtime dependencies did (font swap, event handler
    /// re-registration, etc.).</summary>
    public void ForceReload() { _dirty = false; Reload(); ReloadedThisFrame = true; }

    /// <summary>Look up a named widget after the panel is loaded /
    /// hot-reloaded. Names come from the JSON's <c>"name"</c> field.
    /// Returns null if the name isn't in the current tree or the
    /// widget isn't the requested type. Safe to cache the CALLER
    /// (this returns fresh instances after each reload, so callers
    /// that need the reference across reloads should re-lookup on
    /// use).</summary>
    public T? Find<T>(string name) where T : Element
    {
        return Walk(Container) as T;
        Element? Walk(Element el)
        {
            if (el.Name == name) return el;
            for (int i = 0; i < el.Children.Count; i++)
            {
                var hit = Walk(el.Children[i]);
                if (hit != null) return hit;
            }
            return null;
        }
    }

    private void Reload()
    {
        _lastReload = DateTime.UtcNow;
        try
        {
            var freshRoot = UiLoader.Load(_path, _font, _events);
            Container.Children.Clear();
            Container.Add(freshRoot);
            System.Console.WriteLine($"[ui] reloaded {_path}");
        }
        catch (Exception e)
        {
            // Keep the last-good tree — a broken save shouldn't nuke
            // the panel. Print the error so the author sees it.
            System.Console.Error.WriteLine($"[ui] reload failed for {_path}: {e.Message}");
        }
    }

    private void TryStartWatcher()
    {
        try
        {
            var dir  = Path.GetDirectoryName(Path.GetFullPath(_path));
            var file = Path.GetFileName(_path);
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
            System.Console.Error.WriteLine($"[ui] watcher unavailable for {_path}: {e.Message}");
        }
    }
}
