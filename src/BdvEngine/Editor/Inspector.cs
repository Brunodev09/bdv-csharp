using System.Numerics;
using System.Reflection;
using ImGuiNET;

namespace BdvEngine;

/// <summary>Give a numeric field a slider with real bounds instead of an unbounded drag:
/// <c>[Range(0, 10)] public float Speed = 3;</c>. Purely cosmetic — it changes the widget, never
/// the value's type or how it serialises.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RangeAttribute : Attribute
{
    public readonly float Min, Max;
    public RangeAttribute(float min, float max) { Min = min; Max = max; }
}

/// <summary>Hide a public field from the inspector (it still serialises).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HideInInspectorAttribute : Attribute { }

/// <summary>
/// Turns any object's public fields into ImGui widgets, by reflection.
///
/// <para>This is the file that makes the editor pay off: it means <b>nobody ever writes tuning
/// UI</b>. Add a public field to a behavior and it gets a slider — the same trick Unity's
/// inspector runs on MonoBehaviour fields. It reads the exact same field set the scene serialiser
/// writes (<see cref="SceneJson.FieldsOf"/>), so anything you can edit here is something that
/// survives a save, and anything it can't show is something that wouldn't have persisted
/// anyway.</para>
/// </summary>
public static class Inspector
{
    private static readonly Dictionary<Type, string[]> _enumNames = new();

    /// <summary>Draw editable widgets for every supported public field on <paramref name="target"/>.
    /// Returns true if the user changed something this frame.</summary>
    /// <param name="seen">Optional set of field names already drawn; names in it are skipped, and
    /// names drawn here are added. Used to show a component's live field rather than the identically
    /// named copy on its data bag — the live one is what actually drives behaviour, and it's the one
    /// the serialiser keeps.</param>
    public static bool DrawFields(object? target, string idScope, HashSet<string>? seen = null)
    {
        if (target == null) return false;
        bool changed = false;
        foreach (var f in SceneJson.FieldsOf(target.GetType()))
        {
            if (f.IsDefined(typeof(HideInInspectorAttribute), inherit: true)) continue;
            if (seen != null && !seen.Add(f.Name)) continue;
            changed |= DrawField(target, f, idScope);
        }
        return changed;
    }

    private static bool DrawField(object target, FieldInfo f, string idScope)
    {
        string label = $"{Pretty(f.Name)}##{idScope}.{f.Name}";
        var range = f.GetCustomAttribute<RangeAttribute>();
        object? cur = f.GetValue(target);

        switch (cur)
        {
            case float fv:
            {
                bool hit = range != null
                    ? ImGui.SliderFloat(label, ref fv, range.Min, range.Max)
                    : ImGui.DragFloat(label, ref fv, 0.01f);
                if (hit) { f.SetValue(target, fv); return true; }
                return false;
            }
            case double dv:
            {
                float t = (float)dv;
                bool hit = range != null
                    ? ImGui.SliderFloat(label, ref t, range.Min, range.Max)
                    : ImGui.DragFloat(label, ref t, 0.01f);
                if (hit) { f.SetValue(target, (double)t); return true; }
                return false;
            }
            case int iv:
            {
                bool hit = range != null
                    ? ImGui.SliderInt(label, ref iv, (int)range.Min, (int)range.Max)
                    : ImGui.DragInt(label, ref iv, 0.2f);
                if (hit) { f.SetValue(target, iv); return true; }
                return false;
            }
            case bool bv:
                if (ImGui.Checkbox(label, ref bv)) { f.SetValue(target, bv); return true; }
                return false;

            case string sv:
            {
                string t = sv ?? "";
                if (ImGui.InputText(label, ref t, 128)) { f.SetValue(target, t); return true; }
                return false;
            }
            case Vector2 v2:
                if (ImGui.DragFloat2(label, ref v2, 0.01f)) { f.SetValue(target, v2); return true; }
                return false;

            case Vector3 v3:
                if (ImGui.DragFloat3(label, ref v3, 0.01f)) { f.SetValue(target, v3); return true; }
                return false;

            case Vector4 v4:
                if (ImGui.DragFloat4(label, ref v4, 0.01f)) { f.SetValue(target, v4); return true; }
                return false;

            case Color c:
            {
                var col = new Vector4(c.RFloat, c.GFloat, c.BFloat, c.AFloat);
                if (ImGui.ColorEdit4(label, ref col))
                {
                    f.SetValue(target, Color.FromFloats(col.X, col.Y, col.Z, col.W));
                    return true;
                }
                return false;
            }
            case Enum e:
            {
                var names = EnumNames(f.FieldType);
                int idx = Array.IndexOf(names, e.ToString());
                if (ImGui.Combo(label, ref idx, names, names.Length) && idx >= 0)
                {
                    f.SetValue(target, Enum.Parse(f.FieldType, names[idx]));
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    private static string[] EnumNames(Type t)
    {
        if (_enumNames.TryGetValue(t, out var n)) return n;
        n = Enum.GetNames(t);
        _enumNames[t] = n;
        return n;
    }

    /// <summary>"BounceDamping" -> "Bounce Damping". Field names are the label; splitting camel
    /// case is the whole difference between this reading as a form and reading as a struct dump.</summary>
    internal static string Pretty(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        sb.Append(char.ToUpperInvariant(name[0]));
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}
