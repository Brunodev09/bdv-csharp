using System.Text.Json;

namespace BdvEngine;

public sealed class JsonAsset : IAsset
{
    public string Name { get; }
    public JsonDocument Document { get; }
    public JsonElement Root => Document.RootElement;

    public JsonAsset(string name, JsonDocument document)
    {
        Name = name;
        Document = document;
    }
}

public sealed class JsonAssetLoader : IAssetLoader
{
    private static readonly string[] _exts = { "json" };
    public IReadOnlyList<string> SupportedExtensions => _exts;

    public void LoadAsset(string assetName)
    {
        string path = AssetManager.ResolvePath(assetName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"JsonAssetLoader: file not found: {path}");
            return;
        }

        string text = File.ReadAllText(path);
        var doc = JsonDocument.Parse(text);
        AssetManager.OnLoaded(new JsonAsset(assetName, doc));
    }
}
