using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StbImageSharp;

namespace BdvEngine.Prefabs;

/// <summary>
/// Reads one prefab from disk: a <c>&lt;name&gt;.json</c> that
/// declares the metadata + the pattern filename, plus the referenced
/// PNG that carries the pixel layout. Every unique colour in the
/// PNG is looked up in the global <see cref="PrefabPalette"/>; a
/// colour not in the palette is treated as empty AND logged once
/// per prefab so the artist notices missing swatches.
/// </summary>
public static class PrefabLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <param name="jsonPath">Absolute or CWD-relative path to the
    /// prefab's json descriptor.</param>
    public static Prefab Load(string jsonPath, PrefabPalette palette)
    {
        var raw = File.ReadAllText(jsonPath);
        var meta = JsonSerializer.Deserialize<PrefabDto>(raw, JsonOpts)
                   ?? throw new InvalidDataException($"empty prefab: {jsonPath}");
        var patternPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".",
            meta.Pattern ?? throw new FormatException($"prefab '{jsonPath}' missing 'pattern' field"));
        var grid = DecodePng(patternPath, palette, out int w, out int h);

        var id = Path.GetFileNameWithoutExtension(jsonPath);
        var anchor = meta.Anchor is { Length: 2 }
            ? (meta.Anchor[0], meta.Anchor[1])
            : (w / 2, h - 1);   // default: bottom-centre — good for door-facing prefabs

        var p = new Prefab
        {
            Id       = id,
            Name     = meta.Name     ?? id,
            Category = meta.Category ?? "",
            Width    = w,
            Height   = h,
            Grid     = grid,
            Anchor   = anchor,
            Cost     = meta.Cost      ?? new Dictionary<string, int>(),
            WorkTicks= meta.WorkTicks ?? 0,
            Tags     = meta.Tags      ?? new List<string>(),
            Extras   = meta.Extras    ?? new Dictionary<string, string>(),
        };
        return p;
    }

    /// <summary>Decode PNG pixels into a <see cref="PrefabCell"/> grid
    /// through the palette. Unknown colours become empty cells and
    /// get counted for a single-line summary warning.</summary>
    private static PrefabCell[,] DecodePng(string pngPath, PrefabPalette palette, out int w, out int h)
    {
        using var stream = File.OpenRead(pngPath);
        var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        w = img.Width;
        h = img.Height;
        var grid = new PrefabCell[w, h];
        var unknown = new Dictionary<uint, int>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte r = img.Data[i + 0];
            byte g = img.Data[i + 1];
            byte b = img.Data[i + 2];
            byte a = img.Data[i + 3];
            if (a == 0) { grid[x, y] = PrefabCell.Empty; continue; }
            if (palette.TryGet(r, g, b, a, out var entry))
            {
                grid[x, y] = new PrefabCell(entry.Kind, entry.Id);
            }
            else
            {
                grid[x, y] = PrefabCell.Empty;
                var k = PrefabPalette.Pack(r, g, b, a);
                unknown[k] = unknown.GetValueOrDefault(k) + 1;
            }
        }
        if (unknown.Count > 0)
        {
            Console.Error.Write($"[prefab] {Path.GetFileName(pngPath)}: unknown palette colours (");
            int shown = 0;
            foreach (var kv in unknown)
            {
                if (shown++ > 0) Console.Error.Write(", ");
                Console.Error.Write($"#{kv.Key & 0xFF:X2}{(kv.Key >> 8) & 0xFF:X2}{(kv.Key >> 16) & 0xFF:X2}{(kv.Key >> 24) & 0xFF:X2}×{kv.Value}");
                if (shown >= 6) { Console.Error.Write(", ..."); break; }
            }
            Console.Error.WriteLine(") — those pixels rendered as empty");
        }
        return grid;
    }

    private sealed class PrefabDto
    {
        public string?                       Name      { get; set; }
        public string?                       Category  { get; set; }
        public string?                       Pattern   { get; set; }
        public int[]?                        Anchor    { get; set; }
        public Dictionary<string, int>?      Cost      { get; set; }
        public int?                          WorkTicks { get; set; }
        public List<string>?                 Tags      { get; set; }
        public Dictionary<string, string>?   Extras    { get; set; }
    }
}
