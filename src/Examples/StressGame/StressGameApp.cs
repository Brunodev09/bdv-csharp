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

    private Font _font = null!;
    private BdvEngine.Gui.Root _gui = null!;

    private void BuildUI()
    {
        _font = Font.LoadDefault();
        _gui = new BdvEngine.Gui.Root().WithFont(_font);

        var p = new BdvEngine.Gui.Panel(16, 16, 320, 360)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        p.Add(new BdvEngine.Gui.Label(14, 10, "Stress Test").WithScale(0.46f));

        float y = 50;
        p.Add(new BdvEngine.Gui.Label(14, y, "Particles").WithScale(0.28f).WithColor(new Color(180, 190, 210, 255)));
        p.Add(new BdvEngine.Gui.Slider(14, y + 22, 280, 14, 100f, 50000f, _targetCount).OnChange(v =>
        {
            _targetCount = (int)v;
            if (_targetCount != _x.Length) { Resize(_targetCount); SpawnAll(); }
        }));
        y += 50;
        p.Add(new BdvEngine.Gui.Label(14, y, "Size").WithScale(0.28f).WithColor(new Color(180, 190, 210, 255)));
        p.Add(new BdvEngine.Gui.Slider(14, y + 22, 280, 14, 1f, 20f, _particleSize).OnChange(v => _particleSize = v));
        y += 50;
        p.Add(new BdvEngine.Gui.Label(14, y, "Gravity (px/s²)").WithScale(0.28f).WithColor(new Color(180, 190, 210, 255)));
        p.Add(new BdvEngine.Gui.Slider(14, y + 22, 280, 14, 0f, 1200f, _gravity).OnChange(v => _gravity = v));
        y += 50;
        p.Add(new BdvEngine.Gui.Label(14, y, "Bounce").WithScale(0.28f).WithColor(new Color(180, 190, 210, 255)));
        p.Add(new BdvEngine.Gui.Slider(14, y + 22, 280, 14, 0f, 100f, _bounceDamping * 100f).OnChange(v => _bounceDamping = v / 100f));
        y += 46;

        p.Add(new BdvEngine.Gui.Checkbox(14, y, 280, 18, "Use circles (slower)", false).OnChange(v => _useCircles = v));
        y += 24;
        p.Add(new BdvEngine.Gui.Checkbox(14, y, 280, 18, "Paused", false).OnChange(v => _paused = v));
        y += 30;

        p.Add(new BdvEngine.Gui.Button(14, y, 100, 28, "Explode!").WithFont(_font, 0.30f).OnClick(() =>
        {
            for (int i = 0; i < _alive; i++)
            {
                float dx = _x[i], dy = _y[i];
                float m = MathF.Sqrt(dx * dx + dy * dy);
                if (m == 0) m = 1;
                _vx[i] = dx / m * 480f;
                _vy[i] = dy / m * 480f;
            }
        }));
        p.Add(new BdvEngine.Gui.LiveLabel(130, y + 6, () => $"Particles: {_alive}").WithScale(0.30f));
        _gui.Add(p);
    }

    public override void Update(double deltaTime)
    {
        _gui.Update(Camera, ViewportWidth, ViewportHeight);
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

        _gui.Render(Camera, ViewportWidth, ViewportHeight);
    }
}
