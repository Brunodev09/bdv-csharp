using System.Numerics;
using BdvEngine;

namespace CollisionGameApp;

internal sealed class PlayerControlData : IBehaviorData
{
    public string Name { get; set; } = "playerControl";
    public float Speed = 250f;
    public void SetFromJson(System.Text.Json.JsonElement _) { }
}

internal sealed class PlayerControlBehavior : BaseBehavior
{
    private readonly float _speed;
    public PlayerControlBehavior(PlayerControlData data) : base(data) => _speed = data.Speed;

    public override void Update(double deltaTime)
    {
        float move = _speed * (float)deltaTime;
        var p = _owner.Transform.Position;
        if (InputManager.IsKeyDown(Key.W)) p.Y -= move;
        if (InputManager.IsKeyDown(Key.S)) p.Y += move;
        if (InputManager.IsKeyDown(Key.A)) p.X -= move;
        if (InputManager.IsKeyDown(Key.D)) p.X += move;
        _owner.Transform.Position = p;
    }
}

public sealed class CollisionGame : Game
{
    private readonly Scene _scene = new();
    private SimObject _player = null!;
    private RayCastBehavior _ray = null!;
    private int _frame;

    public override void Init()
    {
        RigidBodyBehavior.ClearAll();
        Camera.X = 400; Camera.Y = 300; Camera.Zoom = 1f;

        var wallColor = new Color(100, 100, 100, 255);
        var walls = new (float X, float Y, float W, float H)[]
        {
            (400,  60, 700, 20), // top
            (400, 540, 700, 20), // bottom
            ( 60, 300,  20, 500), // left
            (740, 300,  20, 500), // right
            (400, 360, 200, 20), // platform
            (210, 260, 120, 20), // shelf
            (600, 210, 100, 20), // shelf
        };
        int id = 0;
        foreach (var w in walls)
            _scene.AddObject(BuildPhysicsObject(id++, $"wall_{id}", w.X, w.Y,
                ColliderShape.Rect, w.W, w.H, 0, wallColor, isStatic: true));

        _player = BuildPhysicsObject(id++, "player", 400, 300,
            ColliderShape.Rect, 50, 50, 0, Color.White, kinematic: true);
        _player.AddBehavior(new PlayerControlBehavior(new PlayerControlData()));
        _ray = new RayCastBehavior(new RayCastBehaviorData());
        _player.AddBehavior(_ray);
        _scene.AddObject(_player);

        var boxes = new (float X, float Y, float Vx, float Vy, Color C)[]
        {
            (200, 150,  80,  50, new Color(200, 150, 50, 255)),
            (500, 400, -60,  70, new Color( 50, 200, 150, 255)),
            (600, 100,  40, -80, new Color(150, 50, 200, 255)),
        };
        foreach (var b in boxes)
            _scene.AddObject(BuildPhysicsObject(id++, $"box_{id}", b.X, b.Y,
                ColliderShape.Rect, 40, 40, 0, b.C, vx: b.Vx, vy: b.Vy));

        var balls = new (float X, float Y, float R, float Vx, float Vy, Color C)[]
        {
            (300, 200, 20,  100,  60, new Color(255,  80,  80, 255)),
            (500, 300, 15,  -70,  90, new Color( 80, 255,  80, 255)),
            (150, 450, 25,   50, -50, new Color( 80,  80, 255, 255)),
            (650, 150, 12,  -90, -60, new Color(255, 255,  80, 255)),
        };
        foreach (var b in balls)
            _scene.AddObject(BuildPhysicsObject(id++, $"ball_{id}", b.X, b.Y,
                ColliderShape.Circle, 0, 0, b.R, b.C, vx: b.Vx, vy: b.Vy));

        _scene.Load();

        BuildGui();
    }

    private Font _font = null!;
    private BdvEngine.Gui.Root _gui = null!;

    private void BuildGui()
    {
        _font = Font.LoadDefault();
        _gui = new BdvEngine.Gui.Root().WithFont(_font);

        var panel = new BdvEngine.Gui.Panel(16, 16, 460, 100)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        panel.Add(new BdvEngine.Gui.Label(14, 10, "Collision Demo").WithScale(0.42f));
        panel.Add(new BdvEngine.Gui.Label(14, 40, "WASD to move | All physics through engine behaviors")
            .WithScale(0.26f).WithColor(new Color(180, 190, 210, 255)));
        panel.Add(new BdvEngine.Gui.LiveLabel(14, 66, () =>
            $"Player: {_player.Transform.Position.X:F0},{_player.Transform.Position.Y:F0}  |  Ray: {(_ray.HasHit ? "HIT" : "miss")}"
        ).WithScale(0.26f).WithColor(new Color(220, 225, 240, 255)));
        _gui.Add(panel);
    }

    private static SimObject BuildPhysicsObject(int id, string name,
        float x, float y, ColliderShape shape,
        float w, float h, float r, Color color,
        float vx = 0, float vy = 0, float gravity = 0,
        bool isStatic = false, bool kinematic = false, float bounce = 0.7f)
    {
        var so = new SimObject(id, name);
        so.Transform.Position = new Vector3(x, y, 0);

        var col = new ColliderComponent(new ColliderComponentData
        {
            Name = "collider",
            Shape = shape,
            Width = w, Height = h, Radius = r,
            IsStatic = isStatic,
        }) { Color = color };
        so.AddComponent(col);

        var rb = new RigidBodyBehavior(new RigidBodyBehaviorData
        {
            Name = "rigidBody",
            Vx = vx, Vy = vy,
            Gravity = gravity,
            BounceDamping = bounce,
            Kinematic = kinematic,
        });
        so.AddBehavior(rb);
        return so;
    }

    public override void Update(double deltaTime)
    {
        var mouse = InputManager.GetMousePosition();
        var world = Camera.ScreenToWorld(mouse.X, mouse.Y, ViewportWidth, ViewportHeight);
        _ray.TargetX = world.X;
        _ray.TargetY = world.Y;
        _scene.Update(deltaTime);
        _gui.Update(Camera, ViewportWidth, ViewportHeight);
    }

    public override void Render(Shader shader)
    {
        _scene.Render(shader);
        if (++_frame == 120) Screenshot.PendingPath = "/tmp/collision.ppm";
        _gui.Render(Camera, ViewportWidth, ViewportHeight);
    }
}
