namespace BdvEngine;

public enum ParticleShape { Rect, Circle }

public sealed class ParticleConfig
{
    public int MaxParticles = 200;
    public float SpawnRate = 50f;
    public double LifetimeMin = 0.5;
    public double LifetimeMax = 1.5;
    public float SpeedMin = 50f;
    public float SpeedMax = 200f;
    public float Direction = -MathF.PI / 2f;
    public float Spread = MathF.PI;
    public float SizeMin = 2f;
    public float SizeMax = 6f;
    public Color ColorStart = new(255, 200, 50, 255);
    public Color ColorEnd = new(255, 50, 0, 255);
    public byte AlphaStart = 255;
    public byte AlphaEnd = 0;
    public float Gravity = 0f;
    public ParticleShape Shape = ParticleShape.Rect;
    public bool Emitting = true;
}

public sealed class ParticleEmitter
{
    private struct Particle
    {
        public float X, Y, Vx, Vy, Size;
        public double Age, Lifetime;
    }

    public float X;
    public float Y;
    public bool Emitting;
    private readonly ParticleConfig _cfg;
    private readonly List<Particle> _particles = new();
    private double _spawnAccumulator;
    private readonly Random _rng = new();

    public int Count => _particles.Count;

    public ParticleEmitter(float x, float y, ParticleConfig? config = null)
    {
        X = x; Y = y;
        _cfg = config ?? new ParticleConfig();
        Emitting = _cfg.Emitting;
    }

    public void Burst(int count)
    {
        for (int i = 0; i < count; i++) Spawn();
    }

    public void Update(double deltaTime)
    {
        if (Emitting)
        {
            _spawnAccumulator += deltaTime;
            double interval = 1.0 / _cfg.SpawnRate;
            while (_spawnAccumulator >= interval)
            {
                _spawnAccumulator -= interval;
                Spawn();
            }
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += deltaTime;
            if (p.Age >= p.Lifetime)
            {
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }
            p.Vy += _cfg.Gravity * (float)deltaTime;
            p.X += p.Vx * (float)deltaTime;
            p.Y += p.Vy * (float)deltaTime;
            _particles[i] = p;
        }
    }

    public void Render()
    {
        foreach (var p in _particles)
        {
            float t = (float)(p.Age / p.Lifetime);
            byte r = (byte)(_cfg.ColorStart.R + (_cfg.ColorEnd.R - _cfg.ColorStart.R) * t);
            byte g = (byte)(_cfg.ColorStart.G + (_cfg.ColorEnd.G - _cfg.ColorStart.G) * t);
            byte b = (byte)(_cfg.ColorStart.B + (_cfg.ColorEnd.B - _cfg.ColorStart.B) * t);
            byte a = (byte)(_cfg.AlphaStart + (_cfg.AlphaEnd - _cfg.AlphaStart) * t);
            var color = new Color(r, g, b, a);
            float half = p.Size / 2f;

            if (_cfg.Shape == ParticleShape.Circle)
                Draw.Circle(p.X, p.Y, half, color, 8);
            else
                Draw.Rect(p.X - half, p.Y - half, p.Size, p.Size, color);
        }
    }

    private void Spawn()
    {
        if (_particles.Count >= _cfg.MaxParticles) return;
        float angle = _cfg.Direction + ((float)_rng.NextDouble() - 0.5f) * _cfg.Spread;
        float speed = Lerp(_cfg.SpeedMin, _cfg.SpeedMax, (float)_rng.NextDouble());
        _particles.Add(new Particle
        {
            X = X, Y = Y,
            Vx = MathF.Cos(angle) * speed,
            Vy = MathF.Sin(angle) * speed,
            Size = Lerp(_cfg.SizeMin, _cfg.SizeMax, (float)_rng.NextDouble()),
            Age = 0,
            Lifetime = LerpD(_cfg.LifetimeMin, _cfg.LifetimeMax, _rng.NextDouble()),
        });
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static double LerpD(double a, double b, double t) => a + (b - a) * t;
}
