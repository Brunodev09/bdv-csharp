using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// Low-level JSON value helpers shared by <see cref="SceneSerializer"/> — colour/vector encoding
/// and the reflection bridge that reads and writes a live component's public fields.
///
/// <para><b>Colour encoding.</b> Every colour, whether stored as a <see cref="Color"/> (bytes) or a
/// <see cref="Vector3"/> (0..1 floats), is written as <c>"#RRGGBB"</c> (or <c>"#RRGGBBAA"</c> when
/// not fully opaque) — one convention across the file, matching the GUI JSON format. Float colours
/// therefore quantise to 8 bits on the first save; every save after that is byte-stable. Non-colour
/// vectors (positions, directions, offsets) are written as <c>{"x":..,"y":..,"z":..}</c> objects and
/// stay exact.</para>
/// </summary>
internal static class SceneJson
{
    // ── colours ──────────────────────────────────────────────────────────────

    public static string ToHex(Color c)
        => c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    public static string ToHex(Vector3 v) => ToHex(Color.FromFloats(v.X, v.Y, v.Z));

    public static Color ParseColor(JsonElement e, Color fallback)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            var f = ReadFloats(e);
            if (f.Length >= 3)
                return Color.FromFloats(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1f);
            return fallback;
        }
        if (e.ValueKind != JsonValueKind.String) return fallback;
        var s = e.GetString();
        if (string.IsNullOrEmpty(s)) return fallback;
        s = s.TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return fallback;
        try
        {
            byte r = byte.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte a = s.Length == 8
                ? byte.Parse(s.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : (byte)255;
            return new Color(r, g, b, a);
        }
        catch { return fallback; }
    }

    public static Vector3 ParseColor3(JsonElement e, Vector3 fallback)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            var f = ReadFloats(e);
            return f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : fallback;
        }
        var c = ParseColor(e, Color.FromFloats(fallback.X, fallback.Y, fallback.Z));
        return new Vector3(c.RFloat, c.GFloat, c.BFloat);
    }

    // ── vectors ──────────────────────────────────────────────────────────────

    // Vectors are WRITTEN as {"x":..,"y":..,"z":..} and READ from either that or [x, y, z].
    // The object form is the one every existing per-component SetFromJson reader already parses
    // (Transform, RotationBehavior, ...), so components round-trip through the builder registry
    // unchanged — that compatibility is worth more than the terser array spelling.
    public static void WriteVec3(Utf8JsonWriter w, string name, Vector3 v)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", v.X);
        w.WriteNumber("y", v.Y);
        w.WriteNumber("z", v.Z);
        w.WriteEndObject();
    }

    public static void WriteVec2(Utf8JsonWriter w, string name, Vector2 v)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", v.X);
        w.WriteNumber("y", v.Y);
        w.WriteEndObject();
    }

    public static void WriteVec4(Utf8JsonWriter w, string name, Vector4 v)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", v.X);
        w.WriteNumber("y", v.Y);
        w.WriteNumber("z", v.Z);
        w.WriteNumber("w", v.W);
        w.WriteEndObject();
    }

    /// <summary>Accepts <c>{"x":..,"y":..,"z":..}</c> (canonical) or <c>[x, y, z]</c>.</summary>
    public static Vector3 ParseVec3(JsonElement e, Vector3 fallback)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            var f = ReadFloats(e);
            return f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : fallback;
        }
        if (e.ValueKind == JsonValueKind.Object)
            return new Vector3(
                e.TryGetProperty("x", out var x) ? x.GetSingle() : fallback.X,
                e.TryGetProperty("y", out var y) ? y.GetSingle() : fallback.Y,
                e.TryGetProperty("z", out var z) ? z.GetSingle() : fallback.Z);
        return fallback;
    }

    public static Vector4 ParseVec4(JsonElement e, Vector4 fallback)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            var f = ReadFloats(e);
            return f.Length >= 4 ? new Vector4(f[0], f[1], f[2], f[3]) : fallback;
        }
        if (e.ValueKind == JsonValueKind.Object)
            return new Vector4(
                e.TryGetProperty("x", out var x) ? x.GetSingle() : fallback.X,
                e.TryGetProperty("y", out var y) ? y.GetSingle() : fallback.Y,
                e.TryGetProperty("z", out var z) ? z.GetSingle() : fallback.Z,
                e.TryGetProperty("w", out var wv) ? wv.GetSingle() : fallback.W);
        return fallback;
    }

    public static float[] ReadFloats(JsonElement arr)
    {
        var list = new List<float>();
        foreach (var el in arr.EnumerateArray())
            if (el.ValueKind == JsonValueKind.Number) list.Add(el.GetSingle());
        return list.ToArray();
    }

    // ── reflection bridge over live components / behaviors ───────────────────

    private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();

    /// <summary>Public instance fields of a type, cached — reflection is cheap here but this runs
    /// per node on load, and the inspector will call it per frame in Phase 2.</summary>
    public static FieldInfo[] FieldsOf(Type t)
    {
        if (_fieldCache.TryGetValue(t, out var cached)) return cached;
        var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                      .Where(f => IsSupported(f.FieldType))
                      .ToArray();
        _fieldCache[t] = fields;
        return fields;
    }

    /// <summary>Types the generic field bridge round-trips. Anything else (a <see cref="Mesh"/>,
    /// a <see cref="Material"/>, a list) is skipped — components holding those are serialised
    /// natively by <see cref="SceneSerializer"/> instead.</summary>
    public static bool IsSupported(Type t)
        => t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(double)
           || t == typeof(string) || t.IsEnum
           || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)
           || t == typeof(Color);

    /// <summary>Collect an object's public field values into <paramref name="into"/>, keyed by
    /// camelCase name. Later calls overwrite earlier ones — the serialiser relies on that to let a
    /// live component field win over the same-named value on its data bag.</summary>
    public static void CollectFields(object? source, Dictionary<string, object?> into)
    {
        if (source == null) return;
        foreach (var f in FieldsOf(source.GetType()))
            into[CamelCase(f.Name)] = f.GetValue(source);
        // Data bags expose Name as a property, not a field; the serialiser writes it separately.
    }

    /// <summary>Apply JSON values back onto an object's public fields. Unknown keys are ignored, so
    /// a hand-edited file can carry comments-by-omission and forward-compatible extras.</summary>
    public static void ApplyFields(object? target, JsonElement json)
    {
        if (target == null || json.ValueKind != JsonValueKind.Object) return;
        foreach (var f in FieldsOf(target.GetType()))
        {
            if (!json.TryGetProperty(CamelCase(f.Name), out var el)) continue;
            ApplyField(f, target, el);
        }
    }

    /// <summary>Apply one JSON value to one field. <paramref name="target"/> is null for a STATIC
    /// field, which is what the tunables registry uses.</summary>
    public static void ApplyField(FieldInfo f, object? target, JsonElement el)
    {
        var v = ReadValue(el, f.FieldType, f.GetValue(target));
        if (v != null) f.SetValue(target, v);
    }

    public static void WriteValue(Utf8JsonWriter w, string name, object? value)
    {
        switch (value)
        {
            case null: break;
            case bool b: w.WriteBoolean(name, b); break;
            case int i: w.WriteNumber(name, i); break;
            case float f: w.WriteNumber(name, f); break;
            case double d: w.WriteNumber(name, d); break;
            case string s: w.WriteString(name, s); break;
            case Color c: w.WriteString(name, ToHex(c)); break;
            case Vector2 v2: WriteVec2(w, name, v2); break;
            case Vector3 v3: WriteVec3(w, name, v3); break;
            case Vector4 v4: WriteVec4(w, name, v4); break;
            case Enum e: w.WriteString(name, e.ToString()); break;
        }
    }

    private static object? ReadValue(JsonElement el, Type t, object? current)
    {
        try
        {
            if (t == typeof(bool)) return el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : current;
            if (t == typeof(int)) return el.ValueKind == JsonValueKind.Number ? el.GetInt32() : current;
            if (t == typeof(float)) return el.ValueKind == JsonValueKind.Number ? el.GetSingle() : current;
            if (t == typeof(double)) return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : current;
            if (t == typeof(string)) return el.ValueKind == JsonValueKind.String ? el.GetString() : current;
            if (t == typeof(Color)) return ParseColor(el, current is Color cc ? cc : Color.White);
            if (t == typeof(Vector3)) return ParseVec3(el, current is Vector3 c3 ? c3 : Vector3.Zero);
            if (t == typeof(Vector2))
            {
                var d = current is Vector2 c2 ? c2 : Vector2.Zero;
                var v = ParseVec3(el, new Vector3(d.X, d.Y, 0));
                return new Vector2(v.X, v.Y);
            }
            if (t == typeof(Vector4)) return ParseVec4(el, current is Vector4 c4 ? c4 : Vector4.Zero);
            // Enums parse case-insensitively so hand-written "circle" matches ColliderShape.Circle.
            if (t.IsEnum && el.ValueKind == JsonValueKind.String)
                return Enum.TryParse(t, el.GetString(), ignoreCase: true, out var ev) ? ev : current;
        }
        catch { /* malformed value for this field — keep what the builder produced */ }
        return current;
    }

    public static string CamelCase(string s)
        => string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
