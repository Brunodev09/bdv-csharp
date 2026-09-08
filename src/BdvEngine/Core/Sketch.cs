using System;
using Silk.NET.Maths;

namespace BdvEngine;

/// <summary>
/// Minimal-ceremony entry point for prototyping — no <see cref="Game"/> subclass, no boilerplate.
/// Write <c>setup</c> / <c>update</c> / <c>draw</c> lambdas and call <see cref="Run"/>. Pairs with
/// .NET 10 single-file programs so a whole prototype is ONE <c>.cs</c> you run directly:
/// <code>
/// #:project ../src/BdvEngine/BdvEngine.csproj
/// using BdvEngine; using System.Numerics;
/// Sketch.Run(w => {
///     w.Camera.Perspective(60); w.Camera.Position = new(4, 4, 7); w.Camera.LookAt(Vector3.Zero);
///     w.Add(new DirectionalLight(new(-.5f, -1, -.3f)));
///     w.Add(Primitives.Cube()).At(0, .5f, 0).Material(Materials.Pbr(Color.Red, metallic: 1));
/// });
/// </code>
/// Command-line flags (parsed automatically; pass after <c>--</c>):
///   <c>--shot &lt;path.png&gt;</c> render a few frames, save a PNG, exit (headless preview);
///   <c>--frames &lt;n&gt;</c> which frame to capture (default 30);
///   <c>--size &lt;WxH&gt;</c> window size; <c>--title &lt;text&gt;</c> window title.
/// So the AI loop is: write scene.cs → <c>dotnet run scene.cs -- --shot out.png</c> → read out.png → iterate.
/// </summary>
public static class Sketch
{
    /// <param name="setup">Build the scene once (camera, lights, objects). Gets the World.</param>
    /// <param name="update">Optional per-frame logic. Gets (World, deltaSeconds).</param>
    /// <param name="draw">Optional immediate-mode 2D pass (SpriteBatcher / Draw), in the camera's
    /// projection — for 2D sketches set <c>w.Camera.Orthographic()</c> in setup and draw here.</param>
    public static void Run(
        Action<World> setup,
        Action<World, double>? update = null,
        Action<World>? draw = null,
        Action<World>? hud = null,
        string title = "BdvEngine Sketch",
        int width = 1280,
        int height = 720)
    {
        var args = Environment.GetCommandLineArgs();
        string? shot = ArgVal(args, "--shot");
        int frames = int.TryParse(ArgVal(args, "--frames"), out var fr) ? fr : 30;
        title = ArgVal(args, "--title") ?? title;

        var sizeArg = ArgVal(args, "--size");
        if (sizeArg != null)
        {
            var p = sizeArg.ToLowerInvariant().Split('x');
            if (p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h))
            {
                width = w;
                height = h;
            }
        }

        var config = new EngineConfig
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            ShowStats = true,
            CapturePath = shot,
            CaptureFrame = frames,
        };
        new Engine(new SketchGame(setup, update, draw, hud), config).Run();
    }

    private static string? ArgVal(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag)
                return args[i + 1];
        return null;
    }

    private sealed class SketchGame : Game
    {
        private readonly Action<World> _setup;
        private readonly Action<World, double>? _update;
        private readonly Action<World>? _draw;
        private readonly Action<World>? _hud;

        public SketchGame(Action<World> setup, Action<World, double>? update, Action<World>? draw, Action<World>? hud)
        {
            _setup = setup;
            _update = update;
            _draw = draw;
            _hud = hud;
        }

        public override void Init() => _setup(World);
        public override void Update(double dt) => _update?.Invoke(World, dt);
        public override void Render(Shader shader) => _draw?.Invoke(World);
        // Runs AFTER the engine has flushed the scene sprites — the right place for a post-scene
        // pass like the lighting multiply (which must land on already-rendered pixels).
        public override void OnHud() => _hud?.Invoke(World);
    }
}
