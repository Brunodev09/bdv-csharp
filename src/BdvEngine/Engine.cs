using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace BdvEngine;

public sealed class EngineConfig
{
    public int TargetFps { get; set; } = 60;
    public bool ShowStats { get; set; } = false;
    public string Title { get; set; } = "BdvEngine";
    public Vector2D<int> Size { get; set; } = new(1600, 900);
}

public sealed class Engine
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private DefaultShader _defaultShader = null!;
    private ImGuiController _imgui = null!;
    private readonly Game _game;
    private readonly EngineConfig _config;

    public Camera2D Camera { get; } = new();
    public DefaultShader DefaultShader => _defaultShader;
    public int CurrentFps { get; private set; }
    public int CurrentDrawCalls { get; private set; }
    public int CurrentChunks { get; private set; }

    private double _fpsTimer;
    private int _frameCount;

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
            ContextAPI.OpenGL,
            ContextProfile.Core,
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
        _window.Resize += OnResize;
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
        Console.WriteLine($"GLSL:        {_gl.GetStringS(StringName.ShadingLanguageVersion)}");

        // Best-effort: poll for GL errors each frame and log them.
        // (KHR_debug isn't reliably exposed on macOS; polling is fine for now.)
        _gl.ClearColor(0f, 0f, 0.3f, 1f);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _defaultShader = new DefaultShader();
        _defaultShader.Use();

        _imgui = new ImGuiController(_gl, _window, _input);
        UI.ApplyDefaultStyle();

        _game.Camera = Camera;
        _game.Init();

        OnResize(_window.Size);
    }

    private void OnUpdate(double delta)
    {
        Time.Advance(delta);
        RigidBodyBehavior.BeginFrame();
        MessageBus.Update(delta);
        AudioManager.Update();
        var size = _window.Size;
        _game.ViewportWidth = size.X;
        _game.ViewportHeight = size.Y;
        _game.Update(delta);
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
            if (_config.ShowStats)
                Console.WriteLine($"{CurrentFps} FPS | {CurrentDrawCalls} draw calls");
        }

        var size = _window.Size;          // logical (world-space) size
        var fb = _window.FramebufferSize; // physical pixels (retina-aware)
        Gfx.WindowWidth = size.X;
        Gfx.WindowHeight = size.Y;
        Gfx.FramebufferWidth = fb.X;
        Gfx.FramebufferHeight = fb.Y;

        // Viewport = physical pixels (crisp rendering on retina).
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Projection = logical size (world coordinates stay 1600x900 regardless of DPI).
        var proj = Camera.GetProjection(size.X, size.Y);
        Draw.Projection = proj;

        _defaultShader.Use();
        _defaultShader.SetUniform("u_proj", proj);

        _game.ViewportWidth = size.X;
        _game.ViewportHeight = size.Y;
        _game.Render(_defaultShader);

        SpriteBatcher.Flush();
        Draw.Flush(_defaultShader);

        // ImGui: logical DisplaySize + retina FramebufferScale = correct DPI-aware UI.
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(size.X, size.Y);
        io.DisplayFramebufferScale = new Vector2(fb.X / (float)size.X, fb.Y / (float)size.Y);
        _imgui.Update((float)delta);
        UI.Render(size.X, size.Y);
        if (_config.ShowStats) DrawStatsOverlay(size.X);
        _imgui.Render();
        // ImGui sets its own viewport during render — restore for screenshot capture / next frame.
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);

        if (Screenshot.PendingPath != null)
        {
            Screenshot.CaptureFullPpm(Screenshot.PendingPath);
            Console.WriteLine($"Saved {Screenshot.PendingPath}");
            Screenshot.PendingPath = null;
        }

        CurrentDrawCalls = GLStats.DrawCalls;
        CurrentChunks = GLStats.ChunksRendered;
    }

    private void OnResize(Vector2D<int> size) { /* viewport is set per-frame from FramebufferSize */ }

    private void DrawStatsOverlay(int viewportW)
    {
        ImGui.SetNextWindowPos(new Vector2(viewportW - 220 - 8, 8), ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoSavedSettings
                  | ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoMove
                  | ImGuiWindowFlags.NoFocusOnAppearing
                  | ImGuiWindowFlags.NoNav
                  | ImGuiWindowFlags.AlwaysAutoResize;
        ImGui.Begin("##bdv_stats", flags);
        var color = CurrentFps >= 55 ? new Vector4(0.3f, 1f, 0.3f, 1f)
                  : CurrentFps >= 30 ? new Vector4(1f, 0.85f, 0.3f, 1f)
                                     : new Vector4(1f, 0.4f, 0.4f, 1f);
        ImGui.TextColored(color, $"{CurrentFps} FPS");
        ImGui.Text($"{CurrentDrawCalls} draw calls");
        ImGui.Text($"{CurrentChunks} chunks");
        ImGui.End();
    }

    private void OnClosing()
    {
        AudioManager.Shutdown();
        _imgui?.Dispose();
        _defaultShader?.Dispose();
        InputManager.Shutdown();
        _input.Dispose();
        _gl.Dispose();
    }
}
