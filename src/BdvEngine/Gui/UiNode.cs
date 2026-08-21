using System.Collections.Generic;

namespace BdvEngine.Gui;

/// <summary>
/// JSON schema for a single UI element. One tree per file. Every
/// property is optional and takes a sensible default; the type
/// discriminator picks which widget to construct.
///
/// Colours parse as <c>"#RRGGBB"</c> or <c>"#RRGGBBAA"</c>. Anchor
/// parses as the string form of the <see cref="Anchor"/> enum
/// (<c>"BottomCenter"</c>, <c>"TopRight"</c>, ...). Event names on
/// <see cref="OnClick"/> are looked up in a
/// <see cref="UiEventRegistry"/> the game code populates.
/// </summary>
public sealed class UiNode
{
    public string Type { get; set; } = "Panel";
    public string? Name { get; set; }

    // Rect + anchor.
    public float X { get; set; }
    public float Y { get; set; }
    public float Width  { get; set; }
    public float Height { get; set; }
    public string? Anchor { get; set; }
    public bool? Visible { get; set; }
    public bool? Pickable { get; set; }

    // Panel-only.
    public string? Background { get; set; }
    public string? Border { get; set; }
    public float? BorderThickness { get; set; }

    // Label-only.
    public string? Text { get; set; }
    public float? Scale { get; set; }
    public string? Color { get; set; }
    public string? Align { get; set; }   // "Left" | "Center" | "Right"
    public bool?   Wrap { get; set; }
    public bool?   AutoFit { get; set; }
    public bool?   Rich { get; set; }

    // Button-only.
    public string? OnClick { get; set; }
    public string? ColorIdle { get; set; }
    public string? ColorHover { get; set; }
    public string? ColorPressed { get; set; }

    // ProgressBar-only.
    public float? Max { get; set; }
    public float? Value { get; set; }
    public string? Fill { get; set; }

    // ScrollView-only.
    public float? ContentHeight { get; set; }

    // ── Flex / Grid layout properties ─────────────────────────

    /// <summary>Layout mode for a container's children: <c>"absolute"</c>
    /// (default, positions from x/y like today), <c>"row"</c> (Flex
    /// row), <c>"column"</c> (Flex column), or <c>"grid"</c>.</summary>
    public string? Layout { get; set; }

    /// <summary>Main-axis packing for flex containers.
    /// <c>"start" | "center" | "end" | "space-between" | "space-around" | "space-evenly"</c>.</summary>
    public string? Justify { get; set; }

    // Note: cross-axis alignment for flex containers reuses the
    // Align field above (loader dispatches by widget type).

    /// <summary>Pixels between siblings inside a flex container.</summary>
    public float? Gap { get; set; }
    public float? GapRow { get; set; }
    public float? GapCol { get; set; }

    /// <summary>Padding inside a flex / grid container. Accepts a
    /// scalar (all sides) or a 4-element array (top, right, bottom,
    /// left) — the loader picks the right overload.</summary>
    public object? Padding { get; set; }

    // ── Grid-specific.
    /// <summary>Number of columns when <c>columnTemplates</c> is
    /// empty. Default 1.</summary>
    public int? Columns { get; set; }
    /// <summary>Per-column sizing (CSS grid-template-columns).
    /// Array of strings like <c>["100px","1fr","auto"]</c>.</summary>
    public string[]? ColumnTemplates { get; set; }
    public float? RowHeight { get; set; }

    // ── Child sizing hints ────────────────────────────────────

    /// <summary>Per-axis size string (see <see cref="Sizing.Parse"/>).
    /// Overrides the numeric Width/Height when present. Used by a
    /// flex / grid PARENT to decide this child's assigned size.</summary>
    public string? W { get; set; }
    public string? H { get; set; }
    /// <summary>Shorthand for both axes.</summary>
    public string? Size { get; set; }
    /// <summary>CSS flex-grow shorthand — same as writing
    /// <c>"w": "Nfr"</c>. Convenience because it reads better in
    /// button rows.</summary>
    public float? Flex { get; set; }

    /// <summary>Optional style-sheet classes (space-separated). Looked
    /// up in the runtime <see cref="UiStyleSheet"/> to fill in fields
    /// the node itself doesn't set.</summary>
    public string? Class { get; set; }

    // Nested children.
    public List<UiNode>? Children { get; set; }
}
