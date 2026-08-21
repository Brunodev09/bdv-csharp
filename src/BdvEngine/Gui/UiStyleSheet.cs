using System.Collections.Generic;

namespace BdvEngine.Gui;

/// <summary>
/// USS-lite runtime style sheet. Maps a class name (as declared on a
/// UiNode's <c>"class": "primary-button"</c>) to a bag of default
/// properties that get applied BEFORE the node's own properties, so
/// per-node overrides always win.
///
/// The whole thing is just a dictionary keyed by class name — no
/// selectors, no cascading, no specificity. Good enough for "define
/// primary/secondary/danger button colours once, reference them
/// everywhere" without inventing a parser.
/// </summary>
public sealed class UiStyleSheet
{
    /// <summary>Style declarations indexed by class name. Multi-class
    /// nodes (<c>"class": "primary-button large"</c>) look each name
    /// up in order — earlier keys applied first, later ones override.</summary>
    public readonly Dictionary<string, UiNode> Classes = new();

    /// <summary>Register a class programmatically. Passing a fully-
    /// constructed <see cref="UiNode"/> makes it clear that the class
    /// is just "the props I'd have written on the node itself".</summary>
    public UiStyleSheet Add(string className, UiNode style)
    {
        Classes[className] = style;
        return this;
    }

    /// <summary>Apply the sheet's classes to a node in place. Every
    /// property the class defines and the node has left unset gets
    /// filled in from the class. Node-defined props are untouched.</summary>
    public void Apply(UiNode node)
    {
        if (string.IsNullOrEmpty(node.Class)) return;
        var names = node.Class.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in names)
        {
            if (!Classes.TryGetValue(name, out var s)) continue;
            MergeInto(node, s);
        }
    }

    private static void MergeInto(UiNode dst, UiNode src)
    {
        dst.Background      ??= src.Background;
        dst.Border          ??= src.Border;
        dst.BorderThickness ??= src.BorderThickness;
        dst.Color           ??= src.Color;
        dst.Scale           ??= src.Scale;
        dst.Fill            ??= src.Fill;
        dst.Align           ??= src.Align;
        dst.ColorIdle       ??= src.ColorIdle;
        dst.ColorHover      ??= src.ColorHover;
        dst.ColorPressed    ??= src.ColorPressed;
        dst.Anchor          ??= src.Anchor;
        dst.Justify         ??= src.Justify;
        dst.Layout          ??= src.Layout;
        dst.Padding         ??= src.Padding;
        dst.Gap             ??= src.Gap;
        dst.GapRow          ??= src.GapRow;
        dst.GapCol          ??= src.GapCol;
        dst.W               ??= src.W;
        dst.H               ??= src.H;
        dst.Size            ??= src.Size;
        dst.Flex            ??= src.Flex;
    }
}
