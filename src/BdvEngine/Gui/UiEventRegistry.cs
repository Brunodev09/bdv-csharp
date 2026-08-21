using System;
using System.Collections.Generic;

namespace BdvEngine.Gui;

/// <summary>
/// Maps string names → C# handlers so a JSON UI can reference a
/// button's <c>onClick</c> by name without knowing about the game
/// code that runs it. Game code registers the handlers once; the
/// UI loader looks them up when it builds buttons. Missing handlers
/// resolve to a no-op so a stale JSON reference doesn't crash.
/// </summary>
public sealed class UiEventRegistry
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public UiEventRegistry Register(string name, Action handler)
    {
        _handlers[name] = handler;
        return this;
    }

    /// <summary>Look up a handler; returns a no-op action if the name
    /// isn't registered, and logs a warning once per unknown name so
    /// the game doesn't spam.</summary>
    public Action Get(string? name)
    {
        if (string.IsNullOrEmpty(name)) return () => { };
        if (_handlers.TryGetValue(name, out var h)) return h;
        if (_missingWarned.Add(name))
            System.Console.WriteLine($"[ui] no handler registered for event '{name}'");
        return () => { };
    }

    private readonly HashSet<string> _missingWarned = new();
}
