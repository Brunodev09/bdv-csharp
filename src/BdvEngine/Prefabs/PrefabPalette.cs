using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BdvEngine.Prefabs;

/// <summary>
/// Global colour → cell mapping. Loaded once at startup; every
/// prefab PNG is decoded against this same table. Colours match on
/// exact <c>RGBA</c> — the artist palette-swatches a bed as
/// #00FF00FF, and every pixel with those bytes becomes a bed cell.
///
/// Fully-transparent pixels (<c>a == 0</c>) are always treated as
/// <see cref="PrefabKind.Empty"/> regardless of the RGB — that way
/// the artist can shape non-rectangular footprints without having
/// to declare a "transparent" swatch.
/// </summary>
public sealed class PrefabPalette
{
    /// <summary>One entry in the palette. Kind decides how the
    /// spawner treats the cell (layered floor + prop, wall blocks
    /// movement, etc.); Id is the string the game side maps to its
    /// tile / prop objects.</summary>
    public readonly struct Entry
    {
        public readonly PrefabKind Kind;
        public readonly string     Id;
        public Entry(PrefabKind k, string id) { Kind = k; Id = id; }
    }

    private readonly Dictionary<uint, Entry> _byRgba = new();

    public IReadOnlyDictionary<uint, Entry> ByRgba => _byRgba;

    public bool TryGet(byte r, byte g, byte b, byte a, out Entry entry)
    {
        if (a == 0) { entry = default; return false; }   // transparent = empty
        uint key = Pack(r, g, b, a);
        if (_byRgba.TryGetValue(key, out entry)) return true;
        // Missing alpha channel in palette declarations (a common
        // artist mistake) — fall back to opaque lookup so #FF0000 in
        // the palette still matches #FF0000FF pixels.
        key = Pack(r, g, b, 255);
        return _byRgba.TryGetValue(key, out entry);
    }

    public static PrefabPalette Load(string jsonPath)
    {
        var raw = File.ReadAllText(jsonPath);
        var doc = JsonSerializer.Deserialize<PaletteDto>(raw, JsonOpts)
                 ?? throw new InvalidDataException($"empty palette: {jsonPath}");
        var p = new PrefabPalette();
        foreach (var kv in doc.Colors)
        {
            if (!TryParseHex(kv.Key, out var r, out var g, out var b, out var a))
                throw new FormatException($"palette colour '{kv.Key}' must be #RRGGBB or #RRGGBBAA");
            var kind = kv.Value.Kind?.ToLowerInvariant() switch
            {
                "floor" => PrefabKind.Floor,
                "wall"  => PrefabKind.Wall,
                "prop"  => PrefabKind.Prop,
                "empty" => PrefabKind.Empty,
                _       => throw new FormatException($"palette kind '{kv.Value.Kind}' is not floor/wall/prop/empty"),
            };
            if (kind == PrefabKind.Empty) continue;   // transparent handled implicitly
            uint key = Pack(r, g, b, a);
            p._byRgba[key] = new Entry(kind, kv.Value.Id ?? "");
        }
        return p;
    }

    // ── Hex + packing helpers ──────────────────────────────────

    public static bool TryParseHex(string hex, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = 0; a = 255;
        if (string.IsNullOrEmpty(hex)) return false;
        var s = hex[0] == '#' ? hex.Substring(1) : hex;
        if (s.Length != 6 && s.Length != 8) return false;
        try
        {
            r = byte.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber);
            g = byte.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber);
            b = byte.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber);
            a = s.Length == 8 ? byte.Parse(s.AsSpan(6, 2), NumberStyles.HexNumber) : (byte)255;
            return true;
        }
        catch (FormatException) { return false; }
    }

    public static uint Pack(byte r, byte g, byte b, byte a)
        => (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    private sealed class PaletteDto
    {
        public Dictionary<string, EntryDto> Colors { get; set; } = new();
    }
    private sealed class EntryDto
    {
        public string? Kind { get; set; }
        public string? Id   { get; set; }
    }
}
