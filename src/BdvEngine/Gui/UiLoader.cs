using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BdvEngine.Gui;

/// <summary>
/// JSON → <see cref="Element"/> tree builder. Supported node types:
/// <list type="bullet">
///   <item><c>Panel</c> / <c>Div</c> — plain absolute container.</item>
///   <item><c>Row</c> — <see cref="FlexContainer"/> with row flow.</item>
///   <item><c>Column</c> — <see cref="FlexContainer"/> with column flow.</item>
///   <item><c>Section</c> — Column preset with background + padding + border.</item>
///   <item><c>Grid</c> / <c>Table</c> — <see cref="GridContainer"/>.</item>
///   <item><c>Label</c>, <c>Button</c>, <c>ProgressBar</c>, <c>ScrollView</c>.</item>
/// </list>
/// The loader is stateless; <see cref="Load"/> and <see cref="Build"/>
/// can be called any number of times (used by <see cref="HotReloadableUi"/>
/// for live reloads).
/// </summary>
public static class UiLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    public static Element Load(string path, Font? font, UiEventRegistry events, UiStyleSheet? styles = null)
    {
        var json = File.ReadAllText(path);
        var node = JsonSerializer.Deserialize<UiNode>(json, Options)
                   ?? throw new InvalidDataException($"empty JSON: {path}");
        return Build(node, font, events, styles);
    }

    public static Element Build(UiNode node, Font? font, UiEventRegistry events, UiStyleSheet? styles = null)
    {
        styles?.Apply(node);

        Element el;
        switch (node.Type)
        {
            case "Panel":
            case "Div":
                el = MakeContainer(node);
                break;

            case "Row":     el = MakeFlex(node, FlexDirection.Row);    break;
            case "Column":  el = MakeFlex(node, FlexDirection.Column); break;
            case "Section":
                var sec = MakeFlex(node, FlexDirection.Column);
                // Section = Column with a subtle default backdrop so
                // it reads as a card. Node-supplied Background wins.
                if (sec is Panel sp && !sp.Background.HasValue)
                    sp.Background = ParseColor("#0F1218EB");
                el = sec;
                break;

            case "Grid":
            case "Table":
                el = MakeGrid(node);
                break;

            case "Label":
                var lbl = new Label(node.X, node.Y, node.Text ?? "");
                if (node.Scale.HasValue) lbl.Scale = node.Scale.Value;
                if (node.Color != null) lbl.TextColor = ParseColor(node.Color);
                if (node.Align != null) lbl.Align = ParseAlign(node.Align);
                if (node.Wrap == true)    lbl.WordWrap = true;
                if (node.AutoFit == false) lbl.NoAutoFit();
                if (node.Rich == true)    lbl.RichText = true;
                if (font != null) lbl.Font = font;
                lbl.Width = node.Width;
                lbl.Height = node.Height;
                el = lbl;
                break;

            case "Button":
                var btn = new Button(node.X, node.Y, node.Width, node.Height, node.Text ?? "");
                if (font != null) btn.WithFont(font, node.Scale ?? 0.22f);
                if (node.OnClick != null) btn.OnClick(events.Get(node.OnClick));
                if (node.ColorIdle    != null
                 || node.ColorHover   != null
                 || node.ColorPressed != null)
                {
                    var idle    = node.ColorIdle    != null ? ParseColor(node.ColorIdle)    : btn.BgIdle;
                    var hover   = node.ColorHover   != null ? ParseColor(node.ColorHover)   : btn.BgHover;
                    var pressed = node.ColorPressed != null ? ParseColor(node.ColorPressed) : btn.BgPressed;
                    btn.WithColors(idle, hover, pressed);
                }
                el = btn;
                break;

            case "ProgressBar":
                var pb = new ProgressBar(node.X, node.Y, node.Width, node.Height, node.Max ?? 100f);
                if (font != null) pb.WithFont(font, node.Scale ?? 0.18f);
                if (node.Fill != null) pb.WithFill(ParseColor(node.Fill));
                if (node.Value.HasValue) pb.Value = node.Value.Value;
                if (node.Text != null) pb.Label = node.Text;
                el = pb;
                break;

            case "ScrollView":
                var sv = new ScrollView(node.X, node.Y, node.Width, node.Height);
                if (node.ContentHeight.HasValue) sv.ContentHeight = node.ContentHeight.Value;
                el = sv;
                break;

            default:
                throw new InvalidDataException($"unknown UI node type: '{node.Type}'");
        }

        if (node.Anchor  != null) el.AnchorTo(ParseAnchor(node.Anchor));
        if (node.Visible.HasValue)  el.Visible  = node.Visible.Value;
        if (node.Pickable.HasValue) el.Pickable = node.Pickable.Value;
        if (node.Name != null) el.Name = node.Name;

        // ── Child sizing hints so a flex/grid parent can size this
        //    child on either axis without the child having to know
        //    what kind of container it lives in.
        if (node.Size != null) el.SizeSpec  = Sizing.Parse(node.Size);
        if (node.W    != null) el.WidthSpec  = Sizing.Parse(node.W);
        if (node.H    != null) el.HeightSpec = Sizing.Parse(node.H);
        if (node.Flex.HasValue) el.WidthSpec = el.HeightSpec = Sizing.Flex(node.Flex.Value);

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var childEl = Build(child, font, events, styles);
                if (el is ScrollView svParent) svParent.Add(childEl);
                else                            el.Add(childEl);
            }
        }
        return el;
    }

    // ── Container factories ─────────────────────────────────────

    private static Panel MakeContainer(UiNode node)
    {
        var p = new Panel(node.X, node.Y, node.Width, node.Height);
        if (node.Background != null) p.Background = ParseColor(node.Background);
        if (node.Border     != null) p.Border     = ParseColor(node.Border);
        if (node.BorderThickness.HasValue) p.BorderThickness = node.BorderThickness.Value;
        return p;
    }

    private static Element MakeFlex(UiNode node, FlexDirection direction)
    {
        var f = new FlexContainer(node.X, node.Y, node.Width, node.Height)
        {
            Direction = direction,
        };
        if (node.Background != null) f.Background = ParseColor(node.Background);
        if (node.Border     != null) f.Border     = ParseColor(node.Border);
        if (node.BorderThickness.HasValue) f.BorderThickness = node.BorderThickness.Value;
        if (node.Gap.HasValue) f.Gap = node.Gap.Value;
        if (node.Justify != null) f.Justify = ParseJustify(node.Justify);
        if (node.Align   != null) f.Align   = ParseFlexAlign(node.Align);
        if (node.Padding != null) f.Padding = ParsePadding(node.Padding);
        return f;
    }

    private static Element MakeGrid(UiNode node)
    {
        var g = new GridContainer(node.X, node.Y, node.Width, node.Height);
        if (node.Background != null) g.Background = ParseColor(node.Background);
        if (node.Border     != null) g.Border     = ParseColor(node.Border);
        if (node.BorderThickness.HasValue) g.BorderThickness = node.BorderThickness.Value;
        if (node.Columns.HasValue) g.Columns = node.Columns.Value;
        if (node.ColumnTemplates != null)
        {
            g.ColumnTemplates.Clear();
            foreach (var t in node.ColumnTemplates) g.ColumnTemplates.Add(Sizing.Parse(t));
        }
        if (node.RowHeight.HasValue) g.RowHeight = node.RowHeight.Value;
        if (node.Gap.HasValue)    { g.GapRow = g.GapCol = node.Gap.Value; }
        if (node.GapRow.HasValue) g.GapRow = node.GapRow.Value;
        if (node.GapCol.HasValue) g.GapCol = node.GapCol.Value;
        if (node.Padding != null) g.Padding = ParsePadding(node.Padding);
        return g;
    }

    // ── Parsers ─────────────────────────────────────────────────

    public static Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.White;
        if (hex[0] == '#') hex = hex.Substring(1);
        if (hex.Length != 6 && hex.Length != 8)
            throw new FormatException($"colour '{hex}' must be #RRGGBB or #RRGGBBAA");
        byte r = byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber);
        byte g = byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber);
        byte b = byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber);
        byte a = hex.Length == 8 ? byte.Parse(hex.AsSpan(6, 2), NumberStyles.HexNumber) : (byte)255;
        return new Color(r, g, b, a);
    }

    public static Anchor ParseAnchor(string s)
    {
        if (Enum.TryParse<Anchor>(s, ignoreCase: true, out var a)) return a;
        throw new FormatException($"unknown anchor: '{s}'");
    }

    public static TextAlign ParseAlign(string s)
    {
        if (Enum.TryParse<TextAlign>(s, ignoreCase: true, out var a)) return a;
        throw new FormatException($"unknown text align: '{s}'");
    }

    public static FlexAlign ParseFlexAlign(string? s)
    {
        return s?.ToLowerInvariant() switch
        {
            "start"    => FlexAlign.Start,
            "center"   => FlexAlign.Center,
            "end"      => FlexAlign.End,
            "stretch"  => FlexAlign.Stretch,
            _          => FlexAlign.Start,
        };
    }

    public static FlexJustify ParseJustify(string? s)
    {
        return s?.ToLowerInvariant() switch
        {
            "start"          => FlexJustify.Start,
            "center"         => FlexJustify.Center,
            "end"            => FlexJustify.End,
            "space-between"  => FlexJustify.SpaceBetween,
            "space-around"   => FlexJustify.SpaceAround,
            "space-evenly"   => FlexJustify.SpaceEvenly,
            _                => FlexJustify.Start,
        };
    }

    /// <summary>Parse a padding declaration: number OR JsonElement
    /// array of 4 numbers.</summary>
    public static Insets ParsePadding(object raw)
    {
        if (raw is JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.Number:
                    return Insets.All((float)je.GetDouble());
                case JsonValueKind.Array:
                    if (je.GetArrayLength() == 4)
                    {
                        float top    = (float)je[0].GetDouble();
                        float right  = (float)je[1].GetDouble();
                        float bottom = (float)je[2].GetDouble();
                        float left   = (float)je[3].GetDouble();
                        return new Insets(top, right, bottom, left);
                    }
                    if (je.GetArrayLength() == 2)
                    {
                        float v = (float)je[0].GetDouble();
                        float h = (float)je[1].GetDouble();
                        return Insets.VH(v, h);
                    }
                    break;
            }
        }
        return Insets.Zero;
    }
}
