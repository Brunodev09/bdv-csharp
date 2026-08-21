using System.Collections.Generic;

namespace BdvEngine.Prefabs;

/// <summary>What each cell of a prefab represents. Extended by
/// per-project mappings — <see cref="PrefabCell.Id"/> is the string
/// the palette declared, and the game decides how to turn e.g.
/// <c>"wood_wall"</c> into an actual tile placement.</summary>
public enum PrefabKind
{
    /// <summary>Transparent pixel — nothing to place. Used to shape
    /// non-rectangular footprints (e.g. an L-shaped room).</summary>
    Empty,
    Floor,
    Wall,
    /// <summary>A prop / structure sitting on top of a floor cell —
    /// bed, torch, door, workbench, etc. Placement layers over the
    /// floor beneath.</summary>
    Prop,
}

/// <summary>One tile of a decoded prefab. 4 bytes of state per cell —
/// stays cache-friendly at reasonable prefab sizes.</summary>
public readonly struct PrefabCell
{
    public readonly PrefabKind Kind;
    /// <summary>The palette id (<c>"wood_wall"</c>, <c>"torch"</c>).
    /// The spawner passes this through to a placement callback so
    /// the game maps the string to its own tile / prop objects.</summary>
    public readonly string?    Id;

    public PrefabCell(PrefabKind kind, string? id) { Kind = kind; Id = id; }

    public static readonly PrefabCell Empty = new(PrefabKind.Empty, null);
}

/// <summary>
/// A finished prefab as loaded from disk. <see cref="Grid"/> is a
/// <c>[Width, Height]</c> array of <see cref="PrefabCell"/>. Metadata
/// (name, cost, tags, anchor) comes from the sibling <c>.json</c>;
/// the grid comes from decoding the sibling <c>.png</c> through the
/// global <see cref="PrefabPalette"/>.
/// </summary>
public sealed class Prefab
{
    /// <summary>Filename stem — e.g. "house_small" for
    /// <c>house_small.json</c> + <c>house_small.png</c>. Used to
    /// look prefabs up in <see cref="PrefabRegistry"/>.</summary>
    public string Id = "";
    public string Name = "";
    public string Category = "";
    public int Width, Height;
    public PrefabCell[,] Grid = new PrefabCell[0, 0];

    /// <summary>Cell the spawn cursor sits under. Defaults to the
    /// grid centre; JSON overrides via <c>"anchor": [col,row]</c>.</summary>
    public (int Col, int Row) Anchor;

    public Dictionary<string, int> Cost = new();
    public int WorkTicks;
    /// <summary>Free-form tags — <c>"shelter"</c>, <c>"warm"</c>,
    /// <c>"sleeps-2"</c>. The game filters by these to build
    /// build-menu categories, room-mood buffs, etc.</summary>
    public List<string> Tags = new();
    /// <summary>Anything else the JSON declares that we didn't model
    /// (beds, capacity, temperature bonus …). Kept as JSON strings
    /// so per-project code can read them without the loader knowing
    /// every schema in advance.</summary>
    public Dictionary<string, string> Extras = new();
}
