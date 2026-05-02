using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace BdvEngine;

public abstract class Game3D
{
    public Camera3D Camera { get; internal set; } = null!;
    public abstract void Init();
    public abstract void Update(double deltaTime);
    public abstract void Render(Shader shader);
}

public sealed class Engine3D
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private LitShader _shader = null!;
    private ImGuiController _imgui = null!;
    private readonly Game3D _game;
    private readonly EngineConfig _config;

    public Camera3D Camera { get; } = new();
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));
    public Vector3 LightColor { get; set; } = new(0.9f, 0.9f, 0.85f);
    public Vector3 AmbientColor { get; set; } = new(0.25f, 0.25f, 0.3f);
    public int CurrentFps { get; private set; }
    public int CurrentDrawCalls { get; private set; }
    public int CurrentChunks { get; private set; }

    private double _fpsTimer;
    private int _frameCount;

    public Engine3D(Game3D game, EngineConfig? config = null)
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

        _gl.ClearColor(0.05f, 0.07f, 0.12f, 1f);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader = new LitShader();
        _shader.Use();

        _imgui = new ImGuiController(_gl, _window, _input);
        UI.ApplyDefaultStyle();

        _game.Camera = Camera;
        _game.Init();
        OnResize(_window.Size);
    }

    private void OnUpdate(double delta)
    {
        MessageBus.Update(delta);
        AudioManager.Update();
        _game.Update(delta);
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
            if (_config.ShowStats) Console.WriteLine($"{CurrentFps} FPS | {CurrentDrawCalls} draw calls");
        }

        var size = _window.Size;
        var fb = _window.FramebufferSize;
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = size.X / (float)size.Y;

        _shader.Use();
        _shader.SetUniform("u_proj", Camera.GetProjectionMatrix(aspect));
        _shader.SetUniform("u_view", Camera.GetViewMatrix());
        _shader.SetUniform("u_lightDir", -LightDirection);
        _shader.SetUniform("u_lightColor", LightColor);
        _shader.SetUniform("u_ambientColor", AmbientColor);
        _shader.SetUniform("u_viewPos", Camera.Position);

        _game.Render(_shader);

        SpriteBatcher.Flush();

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
        ImGui.Begin("##bdv_stats3d", flags);
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
        _shader?.Dispose();
        InputManager.Shutdown();
        _input.Dispose();
        _gl.Dispose();
    }
}
