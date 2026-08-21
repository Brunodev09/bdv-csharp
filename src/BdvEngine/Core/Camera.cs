using System.Numerics;

namespace BdvEngine;

public enum ProjectionMode { Perspective, Orthographic }

/// <summary>
/// The one camera. Orthographic vs perspective is a <see cref="ProjectionMode"/>, not a separate
/// class — the same object drives a 2D or a 3D world, and carries both the 2D pan/zoom state
/// (<see cref="X"/>/<see cref="Y"/>/<see cref="Zoom"/>) and the 3D state
/// (<see cref="Position"/>/<see cref="Target"/>/FOV). Replaces the old Camera + Camera3D.
/// </summary>
public sealed class Camera
{
    public ProjectionMode Mode = ProjectionMode.Perspective;

    // ── 2D (orthographic) state — pan + zoom ──
    public float X { get; set; }
    public float Y { get; set; }
    public float Zoom { get; set; } = 1f;

    // ── 3D (perspective) state ──
    public Vector3 Position = new(0, 3, 8);
    public Vector3 Target = Vector3.Zero;
    public Vector3 Up = Vector3.UnitY;
    public float FieldOfView = MathF.PI / 4f;   // radians
    public float Near = 0.3f;
    public float Far = 500f;

    /// <summary>Switch to perspective (3D). Fluent.</summary>
    public Camera Perspective(float fovDegrees = 60f, float near = 0.3f, float far = 500f)
    {
        Mode = ProjectionMode.Perspective;
        FieldOfView = fovDegrees * MathF.PI / 180f;
        Near = near;
        Far = far;
        return this;
    }

    /// <summary>Switch to orthographic (2D) — the projection then uses <see cref="X"/>/<see cref="Y"/>/
    /// <see cref="Zoom"/>. Fluent.</summary>
    public Camera Orthographic()
    {
        Mode = ProjectionMode.Orthographic;
        return this;
    }

    /// <summary>Aim the camera at a world point (3D).</summary>
    public void LookAt(Vector3 target) => Target = target;

    private ICameraController? _controls;
    public bool HasControls => _controls != null;

    /// <summary>Attach a controller (e.g. <see cref="OrbitControls"/>) that drives this camera from
    /// input each frame. The engine calls <see cref="UpdateControls"/> during update.</summary>
    public void AddControls(ICameraController controls) => _controls = controls;

    public void UpdateControls(double dt) => _controls?.Update(this, dt);

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 ProjectionMatrix(int viewportWidth, int viewportHeight)
    {
        if (Mode == ProjectionMode.Perspective)
        {
            float aspect = viewportHeight == 0 ? 1f : viewportWidth / (float)viewportHeight;
            return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Near, Far);
        }
        return GetProjection(viewportWidth, viewportHeight);
    }

    // ── 2D projection + conversions (formerly Camera) ──

    public Matrix4x4 GetProjection(int viewportWidth, int viewportHeight)
    {
        float halfW = viewportWidth / 2f / Zoom;
        float halfH = viewportHeight / 2f / Zoom;
        return Matrix4x4.CreateOrthographicOffCenter(
            X - halfW, X + halfW,
            Y + halfH, Y - halfH,
            -100f, 100f);
    }

    public Vector2 ScreenToWorld(float screenX, float screenY, int vw, int vh)
        => new(X + (screenX - vw / 2f) / Zoom, Y + (screenY - vh / 2f) / Zoom);

    public Vector2 WorldToScreen(float worldX, float worldY, int vw, int vh)
        => new((worldX - X) * Zoom + vw / 2f, (worldY - Y) * Zoom + vh / 2f);

    // ── 3D picking / projection ──

    /// <summary>Build a world-space ray through a screen pixel (top-left origin), for picking.</summary>
    public Ray ScreenRay(float screenX, float screenY, int viewportWidth, int viewportHeight)
    {
        var vp = ViewMatrix * ProjectionMatrix(viewportWidth, viewportHeight);
        if (!Matrix4x4.Invert(vp, out var inv))
            return new Ray(Position, Vector3.Normalize(Target - Position));

        float ndcX = 2f * screenX / viewportWidth - 1f;
        float ndcY = 1f - 2f * screenY / viewportHeight;   // screen Y is down; NDC Y is up
        var far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inv);
        var farW = new Vector3(far.X, far.Y, far.Z) / far.W;

        if (Mode == ProjectionMode.Perspective)
            return new Ray(Position, Vector3.Normalize(farW - Position));

        var near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), inv);
        var nearW = new Vector3(near.X, near.Y, near.Z) / near.W;
        return new Ray(nearW, Vector3.Normalize(farW - nearW));
    }

    /// <summary>Project a 3D world point to screen pixels (top-left origin). <paramref name="inFront"/>
    /// is false when the point is behind the camera. For anchoring 2D UI to 3D positions.</summary>
    public Vector2 WorldToScreen(Vector3 world, int viewportWidth, int viewportHeight, out bool inFront)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), ViewMatrix * ProjectionMatrix(viewportWidth, viewportHeight));
        inFront = clip.W > 1e-4f;
        if (!inFront) return new Vector2(-1f, -1f);
        float sx = (clip.X / clip.W * 0.5f + 0.5f) * viewportWidth;
        float sy = (1f - (clip.Y / clip.W * 0.5f + 0.5f)) * viewportHeight;
        return new Vector2(sx, sy);
    }
}

/// <summary>A world-space ray (for picking / physics). Direction is expected to be unit length.</summary>
public readonly struct Ray
{
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;
    public Ray(Vector3 origin, Vector3 direction) { Origin = origin; Direction = direction; }
    public Vector3 At(float t) => Origin + Direction * t;
}

/// <summary>Drives a <see cref="Camera"/> from input each frame — implemented by
/// <see cref="OrbitControls"/> and friends. Attach with <see cref="Camera.AddControls"/>.</summary>
public interface ICameraController
{
    void Update(Camera camera, double dt);
}
