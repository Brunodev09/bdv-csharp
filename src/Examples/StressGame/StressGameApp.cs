using BdvEngine;

namespace StressGameApp;

public sealed class StressGame : Game
{
    private int _targetCount = 5000;
    private float _particleSize = 3f;
    private float _gravity = 240f;       // px/s²
    private float _bounceDamping = 0.7f;
    private bool _useCircles = false;
    private bool _paused = false;

    private float[] _x = Array.Empty<float>();
    private float[] _y = Array.Empty<float>();
    private float[] _vx = Array.Empty<float>();
    private float[] _vy = Array.Empty<float>();
    private byte[] _r = Array.Empty<byte>();
    private byte[] _g = Array.Empty<byte>();
    private byte[] _b = Array.Empty<byte>();
    private int _alive;

    private const int WorldWidth = 1280;
    private const int WorldHeight = 720;
    private readonly Random _rng = new(1234);

    public override void Init()
    {
        Resize(_targetCount);
        SpawnAll();
        BuildUI();
    }

    private void Resize(int count)
    {
        Array.Resize(ref _x, count); Array.Resize(ref _y, count);
        Array.Resize(ref _vx, count); Array.Resize(ref _vy, count);
        Array.Resize(ref _r, count); Array.Resize(ref _g, count); Array.Resize(ref _b, count);
        _alive = Math.Min(_alive, count);
    }

    private void SpawnAll()
    {
        for (int i = 0; i < _x.Length; i++) Spawn(i);
        _alive = _x.Length;
    }

    private void Spawn(int i)
    {
        _x[i] = (float)(_rng.NextDouble() * WorldWidth - WorldWidth / 2f);
        _y[i] = (float)(_rng.NextDouble() * WorldHeight - WorldHeight / 2f);
        // px/s velocities (was px/frame at 60Hz, so * 60)
        _vx[i] = (float)(_rng.NextDouble() * 240 - 120);
        _vy[i] = (float)(_rng.NextDouble() * 240 - 120);
        _r[i] = (byte)_rng.Next(80, 255);
        _g[i] = (byte)_rng.Next(80, 255);
        _b[i] = (byte)_rng.Next(80, 255);
    }

    private void BuildUI()
    {
        var p = UI.Panel(UIAnchor.TopLeft);
        UI.Heading(p, "Stress Test");
        UI.Slider(p, "Particles", 100, 50000, _targetCount, v =>
        {
            _targetCount = (int)v;
            if (_targetCount != _x.Length) { Resize(_targetCount); SpawnAll(); }
        });
        UI.Slider(p, "Size", 1, 20, _particleSize, v => _particleSize = v);
        UI.Slider(p, "Gravity (px/s²)", 0, 1200, _gravity, v => _gravity = v);
        UI.Slider(p, "Bounce", 0, 100, _bounceDamping * 100f, v => _bounceDamping = v / 100f);
        UI.Spacer(p);
        UI.Checkbox(p, "Use circles (slower)", false, v => _useCircles = v);
        UI.Checkbox(p, "Paused", false, v => _paused = v);
        UI.Spacer(p);
        UI.Button(p, "Explode!", () =>
        {
            for (int i = 0; i < _alive; i++)
            {
                float dx = _x[i], dy = _y[i];
                float m = MathF.Sqrt(dx * dx + dy * dy);
                if (m == 0) m = 1;
                _vx[i] = dx / m * 480f;
                _vy[i] = dy / m * 480f;
            }
        });
        UI.TextLive(p, () => $"Particles: {_alive}");
    }

    public override void Update(double deltaTime)
    {
        if (_paused) return;

        float dt = (float)deltaTime;
        float halfW = WorldWidth / 2f, halfH = WorldHeight / 2f;
        for (int i = 0; i < _alive; i++)
        {
            _vy[i] += _gravity * dt;
            _x[i] += _vx[i] * dt;
            _y[i] += _vy[i] * dt;

            if (_x[i] < -halfW) { _x[i] = -halfW; _vx[i] = -_vx[i] * _bounceDamping; }
            else if (_x[i] > halfW) { _x[i] = halfW; _vx[i] = -_vx[i] * _bounceDamping; }
            if (_y[i] < -halfH) { _y[i] = -halfH; _vy[i] = -_vy[i] * _bounceDamping; }
            else if (_y[i] > halfH) { _y[i] = halfH; _vy[i] = -_vy[i] * _bounceDamping; }
        }
    }

    private int _frame;
    public override void Render(Shader shader)
    {
        for (int i = 0; i < _alive; i++)
        {
            var col = new Color(_r[i], _g[i], _b[i], 255);
            if (_useCircles)
                Draw.Circle(_x[i], _y[i], _particleSize, col, 8);
            else
                Draw.Rect(_x[i] - _particleSize / 2f, _y[i] - _particleSize / 2f, _particleSize, _particleSize, col);
        }
        if (++_frame == 120) Screenshot.PendingPath = "/tmp/stress.ppm";
    }
}
