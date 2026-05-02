using Silk.NET.OpenGL;

namespace BdvEngine;

public sealed class Texture : IMessageHandler, IDisposable
{
    private readonly GL _gl = Gfx.Gl;
    private readonly uint _handle;
    private readonly string _subKey;

    public string Name { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsLoaded { get; private set; }

    public unsafe Texture(string name, int width = 1, int height = 1)
    {
        Name = name;
        Width = width;
        Height = height;
        _handle = _gl.GenTexture();
        _subKey = $"{AssetManager.MessageAssetLoaderLoaded}::{name}";
        Message.Subscribe(_subKey, this);

        Bind();
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        Span<byte> tempWhite = stackalloc byte[] { 255, 255, 255, 255 };
        fixed (byte* p = tempWhite)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        var asset = AssetManager.Get<ImageAsset>(name);
        if (asset != null) LoadFromAsset(asset);
    }

    public void OnMessage(Message message)
    {
        if (message.Code == _subKey && message.Context is ImageAsset img)
            LoadFromAsset(img);
    }

    private unsafe void LoadFromAsset(ImageAsset asset)
    {
        if (IsLoaded && Width == asset.Width && Height == asset.Height)
            return;
        Width = asset.Width;
        Height = asset.Height;

        Bind();
        fixed (byte* p = asset.Pixels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        if (IsPow2(Width) && IsPow2(Height))
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
        }
        else
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Nearest);

        IsLoaded = true;
    }

    public void Activate(int unit = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        Bind();
    }

    public void Bind() => _gl.BindTexture(TextureTarget.Texture2D, _handle);
    public void Unbind() => _gl.BindTexture(TextureTarget.Texture2D, 0);

    public void Dispose()
    {
        Message.Unsubscribe(_subKey, this);
        _gl.DeleteTexture(_handle);
    }

    private static bool IsPow2(int v) => (v & (v - 1)) == 0;
}

public static class TextureManager
{
    private sealed class Node { public required Texture Texture; public int Count; }
    private static readonly Dictionary<string, Node> _textures = new();

    public static Texture Get(string name)
    {
        if (!_textures.TryGetValue(name, out var node))
        {
            node = new Node { Texture = new Texture(name), Count = 1 };
            _textures[name] = node;
        }
        else node.Count++;
        return node.Texture;
    }

    public static void Flush(string name)
    {
        if (!_textures.TryGetValue(name, out var node)) return;
        node.Count--;
        if (node.Count < 1)
        {
            node.Texture.Dispose();
            _textures.Remove(name);
        }
    }
}
