namespace BdvEngine;

public interface IAsset
{
    string Name { get; }
}

public interface IAssetLoader
{
    IReadOnlyList<string> SupportedExtensions { get; }
    void LoadAsset(string assetName);
}
