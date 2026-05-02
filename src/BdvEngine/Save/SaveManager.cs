using System.Text.Json;

namespace BdvEngine;

public sealed record SaveListEntry(string Slot, DateTime Timestamp, long SizeBytes);

/// <summary>
/// Filesystem-backed slot save system. JSON-serializes data per named slot
/// under the OS user-data folder for the configured app name.
/// macOS: ~/Library/Application Support/&lt;app&gt;/Saves
/// Linux: ~/.config/&lt;app&gt;/Saves
/// Windows: %AppData%/&lt;app&gt;/Saves
/// </summary>
public static class SaveManager
{
    public const string MessageSaveWritten = "MESSAGE_SAVE_WRITTEN";
    public const string MessageSaveDeleted = "MESSAGE_SAVE_DELETED";

    private static string _appName = "BdvEngine";
    private static string? _root;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public static string AppName => _appName;
    public static string Root => _root ??= ResolveRoot();

    public static void Init(string appName)
    {
        _appName = appName;
        _root = ResolveRoot();
        Directory.CreateDirectory(_root);
    }

    public static void Save<T>(string slot, T data)
    {
        EnsureRoot();
        string path = SlotPath(slot);
        string json = JsonSerializer.Serialize(data, _json);
        // Atomic write: stage to .tmp then rename.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
        Message.Send(MessageSaveWritten, null, new { Slot = slot, Size = json.Length });
    }

    public static T? Load<T>(string slot)
    {
        EnsureRoot();
        string path = SlotPath(slot);
        if (!File.Exists(path)) return default;
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, _json);
    }

    public static bool Exists(string slot)
    {
        EnsureRoot();
        return File.Exists(SlotPath(slot));
    }

    public static void Delete(string slot)
    {
        EnsureRoot();
        string path = SlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Message.Send(MessageSaveDeleted, null, new { Slot = slot });
        }
    }

    public static List<SaveListEntry> List()
    {
        EnsureRoot();
        var entries = new List<SaveListEntry>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
        {
            var fi = new FileInfo(file);
            entries.Add(new SaveListEntry(Path.GetFileNameWithoutExtension(fi.Name), fi.LastWriteTimeUtc, fi.Length));
        }
        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries;
    }

    public static void Clear()
    {
        EnsureRoot();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json")) File.Delete(file);
    }

    private static void EnsureRoot()
    {
        if (_root == null)
        {
            _root = ResolveRoot();
            Directory.CreateDirectory(_root);
        }
    }

    private static string ResolveRoot()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(baseDir, _appName, "Saves");
    }

    private static string SlotPath(string slot)
    {
        // Sanitize slot name to a safe filename (allow letters, digits, dash, underscore, dot).
        var safe = new string(slot.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_').ToArray());
        return Path.Combine(Root, safe + ".json");
    }
}
