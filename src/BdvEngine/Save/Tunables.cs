using System.Reflection;
using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// Marks a STATIC field as live-tunable: it appears in the editor's Tunables panel and persists to
/// <c>tuning.json</c>.
///
/// <para>Static because these are the knobs that aren't per-object — day length, sea level, walk
/// speed, spawn density. Per-object values already have a home: put them on a component and the
/// inspector picks them up for free.</para>
///
/// <para><b>The field cannot be <c>const</c>.</b> A const is substituted into every call site at
/// compile time, so there is no storage to change at runtime; the registry rejects them with a
/// clear message rather than appearing to work. Change <c>const float DayLength</c> to
/// <c>static float DayLength</c> and it becomes tunable.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class TunableAttribute : Attribute
{
    /// <summary>Section heading in the editor. Defaults to the declaring type's name.</summary>
    public string? Group { get; set; }

    /// <summary>Optional slider bounds. Without them the widget is an unbounded drag, which is
    /// right for values with no natural ceiling and wrong for anything you want to sweep.</summary>
    public float Min { get; set; }
    public float Max { get; set; }
    public bool HasRange => Max > Min;

    public TunableAttribute() { }

    public TunableAttribute(float min, float max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>One registered knob.</summary>
public sealed class Tunable
{
    public readonly FieldInfo Field;
    public readonly string Group;
    public readonly string Key;
    public readonly TunableAttribute Attribute;

    internal Tunable(FieldInfo field, TunableAttribute attr)
    {
        Field = field;
        Attribute = attr;
        Group = attr.Group ?? field.DeclaringType?.Name ?? "Tunables";
        // Fully qualified so two types can both have a "Speed" without colliding in tuning.json.
        Key = $"{field.DeclaringType?.Name ?? "?"}.{field.Name}";
    }

    /// <summary>Statics have no instance, so reflection takes null as the target.</summary>
    public object? Value
    {
        get => Field.GetValue(null);
        set => Field.SetValue(null, value);
    }
}

/// <summary>
/// Registry of <see cref="TunableAttribute"/>-marked static fields, and their persistence to
/// <c>tuning.json</c>.
///
/// <para>This is the last piece of "everything tunable becomes data". Scene files cover placed
/// objects, prefabs cover repeated ones, materials cover appearance — this covers the loose
/// constants that live in code and would otherwise still cost a recompile each time you nudged
/// them.</para>
/// </summary>
public static class Tunables
{
    private static readonly List<Tunable> _all = new();

    public static IReadOnlyList<Tunable> All => _all;

    /// <summary>Scan one type for marked static fields.</summary>
    public static void Register(Type type)
    {
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var attr = f.GetCustomAttribute<TunableAttribute>();
            if (attr == null) continue;

            if (f.IsLiteral)   // const
            {
                Console.Error.WriteLine(
                    $"[tunable] {type.Name}.{f.Name} is const and cannot be changed at runtime — " +
                    "make it 'static' instead. Skipped.");
                continue;
            }
            if (f.IsInitOnly)  // readonly
            {
                Console.Error.WriteLine(
                    $"[tunable] {type.Name}.{f.Name} is readonly. Skipped.");
                continue;
            }
            if (!SceneJson.IsSupported(f.FieldType))
            {
                Console.Error.WriteLine(
                    $"[tunable] {type.Name}.{f.Name} is {f.FieldType.Name}, which has no widget " +
                    "or serialiser. Skipped.");
                continue;
            }
            if (_all.Exists(t => t.Field == f)) continue;
            _all.Add(new Tunable(f, attr));
        }
    }

    /// <summary>Scan an entire assembly — the usual call, once at startup.
    /// <c>Tunables.RegisterAll(typeof(MyGame).Assembly)</c>.</summary>
    public static void RegisterAll(Assembly assembly)
    {
        foreach (var t in assembly.GetTypes()) Register(t);
    }

    public static void Clear() => _all.Clear();

    /// <summary>Apply values from a <c>tuning.json</c>. Missing keys keep their code defaults, so a
    /// partial file is fine and a newly added knob doesn't need the file updated first.</summary>
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            int applied = 0;
            foreach (var t in _all)
            {
                if (!doc.RootElement.TryGetProperty(t.Key, out var el)) continue;
                SceneJson.ApplyField(t.Field, null, el);
                applied++;
            }
            Console.WriteLine($"[tunable] applied {applied}/{_all.Count} from {path}");
        }
        catch (Exception e)
        {
            // Keep the code defaults rather than half-applying a broken file.
            Console.Error.WriteLine($"[tunable] load failed for {path}: {e.Message}");
        }
    }

    /// <summary>Write every registered knob out. Keys are sorted so the file is diffable.</summary>
    public static void Save(string path)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var t in _all.OrderBy(t => t.Key, StringComparer.Ordinal))
                SceneJson.WriteValue(w, t.Key, t.Value);
            w.WriteEndObject();
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, buffer.ToArray());
        File.Move(tmp, path, overwrite: true);
        Console.WriteLine($"[tunable] saved {_all.Count} to {path}");
    }
}
