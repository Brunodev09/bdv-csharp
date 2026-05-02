using System.Numerics;

namespace BdvEngine;

public sealed class Camera2D
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Zoom { get; set; } = 1f;

    public Matrix4x4 GetProjection(int viewportWidth, int viewportHeight)
    {
        float halfW = viewportWidth / 2f / Zoom;
        float halfH = viewportHeight / 2f / Zoom;
        return Matrix4x4.CreateOrthographicOffCenter(
            X - halfW,  X + halfW,
            Y + halfH,  Y - halfH,
            -100f, 100f);
    }

    public Vector2 ScreenToWorld(float screenX, float screenY, int vw, int vh)
        => new(X + (screenX - vw / 2f) / Zoom, Y + (screenY - vh / 2f) / Zoom);

    public Vector2 WorldToScreen(float worldX, float worldY, int vw, int vh)
        => new((worldX - X) * Zoom + vw / 2f, (worldY - Y) * Zoom + vh / 2f);
}
