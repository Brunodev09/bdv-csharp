namespace BdvEngine;

public static class AssetManager
{
    public const string MessageAssetLoaderLoaded = "MESSAGE_ASSET_LOADER_LOADED";

    private static readonly List<IAssetLoader> _loaders = new();
    private static readonly Dictionary<string, IAsset> _pool = new();

    public static string BasePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "assets");

    public static void Init()
    {
        if (_loaders.Count > 0) return;
        _loaders.Add(new ImageAssetLoader());
        _loaders.Add(new JsonAssetLoader());
    }

    public static void Register(IAssetLoader loader) => _loaders.Add(loader);

    public static void OnLoaded(IAsset asset)
    {
        _pool[asset.Name] = asset;
        Message.SendCritical($"{MessageAssetLoaderLoaded}::{asset.Name}", typeof(AssetManager), asset);
    }

    public static void LoadAsset(string assetName)
    {
        string ext = Path.GetExtension(assetName).TrimStart('.').ToLowerInvariant();
        foreach (var loader in _loaders)
        {
            if (loader.SupportedExtensions.Contains(ext))
            {
                loader.LoadAsset(assetName);
                return;
            }
        }
        Console.WriteLine($"AssetManager: no loader for extension '{ext}' (asset {assetName}).");
    }

    public static bool IsLoaded(string assetName) => _pool.ContainsKey(assetName);

    public static IAsset? Get(string assetName)
    {
        if (_pool.TryGetValue(assetName, out var a)) return a;
        LoadAsset(assetName);
        return _pool.TryGetValue(assetName, out a) ? a : null;
    }

    public static T? Get<T>(string assetName) where T : class, IAsset
        => Get(assetName) as T;

    internal static string ResolvePath(string assetName)
        => Path.IsPathRooted(assetName) ? assetName : Path.Combine(BasePath, assetName);
}
