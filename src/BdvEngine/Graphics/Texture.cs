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

    public Texture(string name, int width = 1, int height = 1)
        : this(name, width, height, loadFromAssets: true) { }

    /// <summary>Used for runtime-generated textures (font atlases, render targets, etc.)
    /// that should not be looked up via AssetManager. Initialize pixels via UploadRgba.</summary>
    public static Texture CreateBlank(string name, int width, int height)
        => new(name, width, height, loadFromAssets: false);

    private unsafe Texture(string name, int width, int height, bool loadFromAssets)
    {
        Name = name;
        Width = width;
        Height = height;
        _handle = _gl.GenTexture();
        _subKey = loadFromAssets ? $"{AssetManager.MessageAssetLoaderLoaded}::{name}" : "";

        Bind();
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        Span<byte> tempWhite = stackalloc byte[] { 255, 255, 255, 255 };
        fixed (byte* p = tempWhite)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        if (!loadFromAssets) return;

        Message.Subscribe(_subKey, this);
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

    /// <summary>Replace the texture with raw RGBA pixel data (4 bytes per pixel, row-major).</summary>
    public unsafe void UploadRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"UploadRgba: expected {width * height * 4} bytes, got {rgba.Length}");
        Width = width;
        Height = height;
        Bind();
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = rgba)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        // Nearest filtering matches LoadFromAsset (pixel-art assets stay
        // crisp). The previous Linear default made every procedural
        // texture (built atlases — props, mobs) bilinear-sampled, which
        // visibly softened sprite art at any zoom != 1×. Callers that
        // genuinely want smooth filtering can set it explicitly via GL
        // after upload.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
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
        if (!string.IsNullOrEmpty(_subKey)) Message.Unsubscribe(_subKey, this);
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

    /// <summary>Pre-register a texture under a name so Material can resolve it without
    /// going through the asset loader. Used for runtime-generated atlases (fonts, etc.).</summary>
    public static void Register(string name, Texture texture)
    {
        if (_textures.ContainsKey(name)) return;
        _textures[name] = new Node { Texture = texture, Count = 0 };
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
