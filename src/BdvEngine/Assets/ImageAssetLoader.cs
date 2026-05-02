using StbImageSharp;

namespace BdvEngine;

public sealed class ImageAsset : IAsset
{
    public string Name { get; }
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    public ImageAsset(string name, byte[] pixels, int width, int height)
    {
        Name = name;
        Pixels = pixels;
        Width = width;
        Height = height;
    }
}

public sealed class ImageAssetLoader : IAssetLoader
{
    private static readonly string[] _exts = { "png", "jpg", "jpeg", "gif", "bmp", "tga" };
    public IReadOnlyList<string> SupportedExtensions => _exts;

    public void LoadAsset(string assetName)
    {
        string path = AssetManager.ResolvePath(assetName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"ImageAssetLoader: file not found: {path}");
            return;
        }

        using var fs = File.OpenRead(path);
        var image = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
        AssetManager.OnLoaded(new ImageAsset(assetName, image.Data, image.Width, image.Height));
    }
}
