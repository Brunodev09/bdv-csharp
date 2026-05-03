using System.Numerics;
using BdvEngine;

namespace MyGameApp;

internal sealed class TintPulseShader : Shader
{
    public TintPulseShader() : base("tint_pulse")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_textCoord;

uniform mat4 u_proj;
uniform mat4 u_transf;

out vec2 v_textCoord;

void main()
{
    gl_Position = u_proj * u_transf * vec4(a_pos, 1.0);
    v_textCoord = a_textCoord;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_textCoord;

uniform sampler2D u_diffuse;
uniform vec4 u_color;
uniform float u_time;

out vec4 fragColor;

void main()
{
    vec4 texColor = texture(u_diffuse, v_textCoord);
    float pulse = (sin(u_time * 3.0) + 1.0) * 0.5;
    vec3 tinted = mix(texColor.rgb, u_color.rgb, pulse * 0.6);
    float dist = distance(v_textCoord, vec2(0.5, 0.5));
    float vignette = smoothstep(0.7, 0.2, dist);
    fragColor = vec4(tinted * vignette, texColor.a * u_color.a);
}";
}

public sealed class MyGame : Game
{
    private Scene _scene = null!;
    private bool _drawShapes = true;
    private KeyboardMovementBehavior _move = null!;
    private Material _crateMaterial = null!;
    private double _elapsed;

    private ParticleEmitter _fireEmitter = null!;
    private ParticleEmitter _sparkEmitter = null!;

    private int _score;

    public override void Init()
    {
        // Match TS: world (0,0) is top-left of viewport.
        Camera.X = 800; Camera.Y = 450;

        MaterialManager.Register(new Material("duck", "textures/duck.png", Color.White));
        MaterialManager.Register(new Material("block", "textures/block.png", Color.White));

        _crateMaterial = new Material("crate", "textures/block.png",
            new Color(255, 100, 200, 255), new TintPulseShader());
        MaterialManager.Register(_crateMaterial);

        _scene = new Scene();

        // Animated duck
        var duck = new SimObject(1, "duck");
        duck.Transform.Position = new Vector3(100, 100, 0);
        duck.Transform.Scale = new Vector3(8, 8, 1);
        duck.AddComponent(new AnimatedSpriteComponent(new AnimatedSpriteComponentData
        {
            Name = "duckSprite",
            MaterialName = "duck",
            FrameWidth = 17, FrameHeight = 12,
            FrameCount = 3,
            FrameSequence = new[] { 0, 1, 2, 1 },
        }));
        var moveData = new KeyboardMovementBehaviorData { Name = "mover", Speed = 150f };
        _move = new KeyboardMovementBehavior(moveData);
        duck.AddBehavior(_move);

        // Crate with custom shader
        var crate = new SimObject(2, "crate");
        crate.Transform.Position = new Vector3(500, 80, 0);
        crate.Transform.Scale = new Vector3(8, 8, 1);
        crate.AddComponent(new AnimatedSpriteComponent(new AnimatedSpriteComponentData
        {
            Name = "crateSprite",
            MaterialName = "crate",
            FrameWidth = 16, FrameHeight = 16,
            FrameCount = 1,
            FrameSequence = new[] { 0 },
        }));

        // Parent/child/grandchild rotation hierarchy
        var parent = new SimObject(10, "parentBlock");
        parent.Transform.Position = new Vector3(900, 300, 0);
        parent.Transform.Scale = new Vector3(4, 4, 1);
        parent.AddComponent(new SpriteComponent(new SpriteComponentData
        { Name = "parentSprite", MaterialName = "block" }));

        var child = new SimObject(11, "childBlock");
        child.Transform.Position = new Vector3(40, 0, 0);
        child.Transform.Scale = new Vector3(0.5f, 0.5f, 1);
        child.AddComponent(new SpriteComponent(new SpriteComponentData
        { Name = "childSprite", MaterialName = "duck" }));

        var grandchild = new SimObject(12, "grandchildBlock");
        grandchild.Transform.Position = new Vector3(30, 0, 0);
        grandchild.Transform.Scale = new Vector3(0.5f, 0.5f, 1);
        grandchild.AddComponent(new SpriteComponent(new SpriteComponentData
        { Name = "gcSprite", MaterialName = "block" }));

        child.AddChild(grandchild);
        parent.AddChild(child);

        _scene.AddObject(duck);
        _scene.AddObject(crate);
        _scene.AddObject(parent);
        _scene.Load();

        BuildUI();

        _fireEmitter = new ParticleEmitter(400, 500, new ParticleConfig
        {
            SpawnRate = 80, MaxParticles = 300,
            LifetimeMin = 0.4, LifetimeMax = 1.2,
            SpeedMin = 40f, SpeedMax = 120f,
            Direction = -MathF.PI / 2f, Spread = MathF.PI / 4f,
            SizeMin = 3f, SizeMax = 8f,
            ColorStart = new Color(255, 220, 50, 255),
            ColorEnd = new Color(255, 30, 0, 255),
            AlphaStart = 255, AlphaEnd = 0,
            Shape = ParticleShape.Circle,
        });

        _sparkEmitter = new ParticleEmitter(700, 500, new ParticleConfig
        {
            SpawnRate = 30, MaxParticles = 100,
            LifetimeMin = 0.3, LifetimeMax = 0.8,
            SpeedMin = 100f, SpeedMax = 300f,
            Direction = -MathF.PI / 2f, Spread = MathF.PI * 2f,
            SizeMin = 1f, SizeMax = 3f,
            ColorStart = new Color(200, 200, 255, 255),
            ColorEnd = new Color(100, 100, 255, 255),
            AlphaStart = 255, AlphaEnd = 0,
            Gravity = 300f,
            Shape = ParticleShape.Rect,
        });
    }

    private Font _font = null!;
    private BdvEngine.Gui.Root _gui = null!;

    private void BuildUI()
    {
        _font = Font.LoadDefault();
        _gui = new BdvEngine.Gui.Root().WithFont(_font);

        var panel = new BdvEngine.Gui.Panel(16, 16, 280, 240)
            .WithBackground(new Color(18, 22, 32, 230))
            .WithBorder(new Color(95, 115, 160, 255), 2f);
        panel.Add(new BdvEngine.Gui.Label(14, 10, "BdvEngine").WithScale(0.46f));
        panel.Add(new BdvEngine.Gui.Label(14, 42, "Arrow keys to move the duck")
            .WithScale(0.28f).WithColor(new Color(180, 190, 210, 255)));
        panel.Add(new BdvEngine.Gui.LiveLabel(14, 72, () => $"Score: {_score}")
            .WithScale(0.32f).WithColor(new Color(255, 240, 180, 255)));
        panel.Add(new BdvEngine.Gui.Button(14, 100, 110, 28, "+10 Score")
            .WithFont(_font, 0.28f).OnClick(() => _score += 10));

        panel.Add(new BdvEngine.Gui.Label(14, 138, "Speed (px/s)").WithScale(0.26f).WithColor(new Color(180, 190, 210, 255)));
        panel.Add(new BdvEngine.Gui.Slider(14, 158, 244, 14, 60f, 1200f, 150f).OnChange(v => _move.Speed = v));
        panel.Add(new BdvEngine.Gui.Checkbox(14, 188, 240, 18, "Show shapes", true).OnChange(v => _drawShapes = v));
        _gui.Add(panel);
    }

    public override void Update(double deltaTime)
    {
        _scene.Update(deltaTime);
        _elapsed += deltaTime;

        _crateMaterial.SetUniform("u_time", (float)_elapsed);

        var parent = _scene.GetObjectByName("parentBlock");
        if (parent != null) parent.Transform.Rotation = new Vector3(0, 0, (float)(_elapsed * 1.5));

        var child = _scene.GetObjectByName("childBlock");
        if (child != null) child.Transform.Rotation = new Vector3(0, 0, (float)(-_elapsed * 3));

        var gc = _scene.GetObjectByName("grandchildBlock");
        if (gc != null) gc.Transform.Rotation = new Vector3(0, 0, (float)(_elapsed * 5));

        _fireEmitter.Update(deltaTime);
        _sparkEmitter.Update(deltaTime);
        _gui.Update(Camera, ViewportWidth, ViewportHeight);
    }

    public override void Render(Shader shader)
    {
        _scene.Render(shader);

        if (_drawShapes)
        {
            Draw.Rect(400, 300, 120, 80, Color.Red);
            Draw.RectOutline(400, 300, 120, 80, Color.White);
            Draw.Circle(700, 350, 50, Color.Green);
            Draw.CircleOutline(700, 350, 60, Color.White);
            Draw.Triangle(800, 250, 850, 350, 750, 350, Color.Blue);
            Draw.Line(50, 400, 300, 400, new Color(255, 255, 0, 255));
            Draw.Ray(50, 450, 1, 0.5f, 200, new Color(0, 255, 255, 255));
            Draw.Point(600, 450, Color.White, 6);
        }

        _fireEmitter.Render();
        _sparkEmitter.Render();

        if (++_frame == 120) Screenshot.PendingPath = "/tmp/mygame.ppm";

        _gui.Render(Camera, ViewportWidth, ViewportHeight);
    }

    private int _frame;
}
