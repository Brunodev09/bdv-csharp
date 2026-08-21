using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace BdvEngine;

/// <summary>Boot configuration for the <see cref="Engine"/> — title, window size, target FPS,
/// and whether to show the on-screen stats overlay.</summary>
public sealed class EngineConfig
{
    public int TargetFps { get; set; } = 60;
    public bool ShowStats { get; set; } = false;
    public string Title { get; set; } = "BdvEngine";
    public Vector2D<int> Size { get; set; } = new(1600, 900);

    /// <summary>If set, the engine renders <see cref="CaptureFrame"/> frames, saves a PNG here,
    /// then closes — the headless "preview" mode for AI/scripted prototyping. Usually set via the
    /// <c>--shot &lt;path&gt;</c> command-line arg through <see cref="Sketch"/>.</summary>
    public string? CapturePath { get; set; }

    /// <summary>Frame at which <see cref="CapturePath"/> is captured (lets the scene settle first).</summary>
    public int CaptureFrame { get; set; } = 30;
}

/// <summary>
/// Base class for a unified game. ONE base for 2D and 3D — the camera's projection mode decides.
/// There is NO <c>Render(Shader)</c> override: the engine owns the loop and renders the World's
/// scene itself. Author code lives in <see cref="Init"/> / <see cref="Update"/>, with optional
/// 2D screen-space drawing in <see cref="OnHud"/>.
/// </summary>
public abstract class Game
{
    /// <summary>Scene + camera + environment for this level. Assigned by the engine before Init.</summary>
    public World World { get; internal set; } = null!;

    /// <summary>Shorthand for <c>World.Camera</c>.</summary>
    public Camera Camera => World.Camera;

    public int ViewportWidth { get; internal set; }
    public int ViewportHeight { get; internal set; }

    public abstract void Init();
    public abstract void Update(double deltaTime);

    /// <summary>Optional 2D screen-space pass, drawn on top of the 3D scene each frame. Issue
    /// <c>SpriteBatcher.*</c> / <c>Draw.*</c> calls here — proves the 2D lane coexists with 3D in
    /// one engine. (Phase 8 lets 2D sprites live in the scene graph as billboards too.)</summary>
    public virtual void OnHud() { }

    /// <summary>2D immediate-mode render hook (parity with the old 2D <c>Game</c>). Called once per
    /// frame when the camera is <see cref="ProjectionMode.Orthographic"/>, with the default sprite
    /// shader bound and the camera's ortho projection set. Draw via <c>SpriteBatcher</c> / <c>Draw</c>
    /// / <c>Scene.Render(shader)</c>. No-op for 3D games (the engine renders the scene's meshes).</summary>
    public virtual void Render(Shader shader) { }

    /// <summary>Fired after the window resizes.</summary>
    public virtual void OnResize() { }

    public virtual void OnShutdown() { }
}

/// <summary>
/// The one unified engine. Owns the window / GL / input / render loop AND the <see cref="World"/>,
/// so the game never pumps the scene or writes a render call. Subsumes both the old <c>Engine</c>
/// and <c>Engine3D</c>. Each frame: update → rebake transforms → 3D pass (lit, depth-tested) →
/// 2D pass (screen-space sprites) → ImGui overlay.
/// </summary>
public sealed class Engine
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private MeshRenderer _meshRenderer = null!;
    private DefaultShader _defaultShader = null!;   // 2D sprite shader for the ortho render path
    private ImGuiController _imgui = null!;

    private readonly Game _game;
    private readonly EngineConfig _config;
    private readonly World _world = new();

    public int CurrentFps { get; private set; }
    public int CurrentDrawCalls { get; private set; }

    private double _fpsTimer;
    private int _frameCount;
    private int _captureFrame;

    public Engine(Game game, EngineConfig? config = null)
    {
        _game = game;
        _config = config ?? new EngineConfig();
    }

    public void Run()
    {
        var options = WindowOptions.Default;
        options.Title = _config.Title;
        options.Size = _config.Size;
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL, ContextProfile.Core,
            ContextFlags.ForwardCompatible | ContextFlags.Debug,
            new APIVersion(4, 1));
        options.PreferredDepthBufferBits = 24;
        options.VSync = true;
        options.FramesPerSecond = 0;
        options.UpdatesPerSecond = 0;

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += _ => _game.OnResize();
        _window.Closing += OnClosing;
        _window.Run();
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        Gfx.Gl = _gl;
        _input = _window.CreateInput();
        InputManager.Initialize(_input);
        AssetManager.Init();
        AudioManager.Init();
        Registrations.RegisterDefaults();

        Console.WriteLine($"GL_VERSION:  {_gl.GetStringS(StringName.Version)}");
        Console.WriteLine($"GL_RENDERER: {_gl.GetStringS(StringName.Renderer)}");

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        _meshRenderer = new MeshRenderer();
        _defaultShader = new DefaultShader();

        _imgui = new ImGuiController(_gl, _window, _input);
        UI.ApplyDefaultStyle();

        _game.World = _world;
        _game.Init();
        _world.Scene.Load();   // GL context exists now — safe to upload meshes/textures
    }

    private void OnUpdate(double delta)
    {
        Time.Advance(delta);
        MessageBus.Update(delta);
        AudioManager.Update();

        var size = _window.Size;
        _game.ViewportWidth = size.X;
        _game.ViewportHeight = size.Y;

        _world.Camera.UpdateControls(delta);   // orbit/fly controls drive the camera from input
        _game.Update(delta);
        _world.Scene.Update(delta);            // runs behaviors + components
        InputManager.EndFrame();
    }

    private void OnRender(double delta)
    {
        GLStats.Reset();

        _frameCount++;
        _fpsTimer += delta;
        if (_fpsTimer >= 1.0)
        {
            CurrentFps = _frameCount;
            _frameCount = 0;
            _fpsTimer -= 1.0;
        }

        var size = _window.Size;
        var fb = _window.FramebufferSize;
        Gfx.WindowWidth = size.X;
        Gfx.WindowHeight = size.Y;
        Gfx.FramebufferWidth = fb.X;
        Gfx.FramebufferHeight = fb.Y;

        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        _gl.DepthMask(true);
        var sky = _world.Environment.Sky;
        _gl.ClearColor(sky.X, sky.Y, sky.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Rebake transforms AFTER all updates so mutations this frame render THIS frame.
        _world.Scene.RebakeMatrices();

        // ── ONE render path for 2D and 3D. Ortho vs perspective is only the camera's projection,
        //    not a branch. Every frame: (1) the scene renderer draws the scene's meshes/billboards
        //    — empty for a pure-2D game; (2) the immediate-mode Render(shader) hook draws in the
        //    camera's projection — empty for a pure-3D game; (3) a screen-space overlay (OnHud).
        //    A game populates whichever hook(s) it uses; the engine never asks "is this 2D or 3D".

        var proj = _world.Camera.ProjectionMatrix(size.X, size.Y);

        // (1) Scene pass — meshes + billboards, depth-tested.
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _meshRenderer.Render(_world.Scene, _world.Camera, _world.Environment, size.X, size.Y);

        // (2) Immediate-mode pass in the camera's projection — SpriteBatcher manages its own depth.
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        Draw.Projection = proj;
        _defaultShader.Use();
        _defaultShader.SetUniform("u_proj", proj);
        _game.Render(_defaultShader);
        SpriteBatcher.Flush();
        Draw.Flush(_defaultShader);

        // (3) Screen-space overlay (always screen ortho) for HUD / UI.
        Draw.Projection = Matrix4x4.CreateOrthographicOffCenter(0, size.X, size.Y, 0, -100f, 100f);
        _game.OnHud();
        SpriteBatcher.Flush();

        // ── ImGui overlay ──
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(size.X, size.Y);
        io.DisplayFramebufferScale = new Vector2(fb.X / (float)size.X, fb.Y / (float)size.Y);
        _imgui.Update((float)delta);
        UI.Render(size.X, size.Y);
        if (_config.ShowStats) DrawStatsOverlay(size.X);
        _imgui.Render();
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);

        if (Screenshot.PendingPath != null)
        {
            Screenshot.CaptureFullPpm(Screenshot.PendingPath);
            Console.WriteLine($"Saved {Screenshot.PendingPath}");
            Screenshot.PendingPath = null;
        }

        // Headless preview: render a few frames, save a PNG, and close.
        if (_config.CapturePath != null && ++_captureFrame >= _config.CaptureFrame)
        {
            Screenshot.CapturePng(_config.CapturePath);
            Console.WriteLine($"[capture] saved {_config.CapturePath} ({fb.X}x{fb.Y})");
            _window.Close();
        }

        CurrentDrawCalls = GLStats.DrawCalls;
    }

    private void DrawStatsOverlay(int viewportW)
    {
        ImGui.SetNextWindowPos(new Vector2(viewportW - 220 - 8, 8), ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
                  | ImGuiWindowFlags.AlwaysAutoResize;
        ImGui.Begin("##unified_stats", flags);
        var color = CurrentFps >= 55 ? new Vector4(0.3f, 1f, 0.3f, 1f)
                  : CurrentFps >= 30 ? new Vector4(1f, 0.85f, 0.3f, 1f)
                                     : new Vector4(1f, 0.4f, 0.4f, 1f);
        ImGui.TextColored(color, $"{CurrentFps} FPS");
        ImGui.Text($"{CurrentDrawCalls} draw calls");
        ImGui.End();
    }

    private void OnClosing()
    {
        _game.OnShutdown();
        AudioManager.Shutdown();
        _imgui?.Dispose();
        _meshRenderer?.Dispose();
        _defaultShader?.Dispose();
        InputManager.Shutdown();
        _input.Dispose();
        _gl.Dispose();
    }
}
