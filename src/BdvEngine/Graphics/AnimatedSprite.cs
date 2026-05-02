using Silk.NET.OpenGL;

namespace BdvEngine;

public sealed class AnimatedSprite : Sprite, IMessageHandler
{
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private readonly int _frameCount;
    private int[] _frameSequence;

    private double _frameTime = 0.333;
    private double _currentTime;
    private int _currentFrame;

    private bool _assetLoaded;
    private int _assetWidth = 2;
    private int _assetHeight = 2;

    private (float UMinX, float UMinY, float UMaxX, float UMaxY)[] _frameUVs = Array.Empty<(float, float, float, float)>();
    private readonly string _subKey;

    public AnimatedSprite(string name, string materialName, float width, float height,
        int frameWidth, int frameHeight, int frameCount, int[] frameSequence)
        : base(name, materialName, width, height)
    {
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _frameCount = frameCount;
        _frameSequence = frameSequence;

        _subKey = $"{AssetManager.MessageAssetLoaderLoaded}::{Material.DiffuseTextureName}";
        Message.Subscribe(_subKey, this);

        var asset = AssetManager.Get<ImageAsset>(Material.DiffuseTextureName);
        if (asset != null)
        {
            _assetLoaded = true;
            _assetWidth = asset.Width;
            _assetHeight = asset.Height;
            CalculateUVs();
        }
    }

    public void OnMessage(Message message)
    {
        if (message.Code == _subKey && message.Context is ImageAsset asset)
        {
            _assetLoaded = true;
            _assetWidth = asset.Width;
            _assetHeight = asset.Height;
            CalculateUVs();
        }
    }

    public void SetFrameSequence(int[] seq)
    {
        if (_frameSequence.Length == seq.Length && _frameSequence.SequenceEqual(seq)) return;
        _frameSequence = seq;
        _currentFrame = 0;
        _currentTime = 0;
    }

    public void SetFrameTime(double seconds) => _frameTime = seconds;
    public int[] FrameSequence => _frameSequence;

    public override void Update(double deltaTime)
    {
        if (!_assetLoaded) return;
        _currentTime += deltaTime;
        if (_currentTime > _frameTime)
        {
            _currentFrame = (_currentFrame + 1) % _frameSequence.Length;
            _currentTime = 0;
            UploadFrameUVs();
        }
    }

    private void CalculateUVs()
    {
        int colsPerRow = Math.Max(1, _assetWidth / _frameWidth);
        var list = new List<(float, float, float, float)>();
        for (int i = 0; i < _frameCount; i++)
        {
            int col = i % colsPerRow;
            int row = i / colsPerRow;
            float u  = col * _frameWidth / (float)_assetWidth;
            float v  = row * _frameHeight / (float)_assetHeight;
            float uM = (col + 1) * _frameWidth / (float)_assetWidth;
            float vM = (row + 1) * _frameHeight / (float)_assetHeight;
            list.Add((u, v, uM, vM));
        }
        _frameUVs = list.ToArray();
        UploadFrameUVs();
    }

    private void UploadFrameUVs()
    {
        if (_frameUVs.Length == 0 || _vertices.Count == 0) return;

        int frameIdx = _frameSequence[_currentFrame];
        if (frameIdx >= _frameUVs.Length) return;
        var f = _frameUVs[frameIdx];

        // Vertices order matches Sprite.Load: (0,0) (0,H) (W,H) (W,H) (W,0) (0,0)
        _vertices[0] = new Vertex(0, 0, 0,    f.UMinX, f.UMinY);
        _vertices[1] = new Vertex(0, Height, 0, f.UMinX, f.UMaxY);
        _vertices[2] = new Vertex(Width, Height, 0, f.UMaxX, f.UMaxY);
        _vertices[3] = new Vertex(Width, Height, 0, f.UMaxX, f.UMaxY);
        _vertices[4] = new Vertex(Width, 0, 0,    f.UMaxX, f.UMinY);
        _vertices[5] = new Vertex(0, 0, 0,        f.UMinX, f.UMinY);
    }

    public override void Dispose()
    {
        Message.Unsubscribe(_subKey, this);
        base.Dispose();
    }
}
