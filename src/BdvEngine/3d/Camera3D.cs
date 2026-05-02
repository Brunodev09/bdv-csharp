using System.Numerics;

namespace BdvEngine;

public sealed class Camera3D
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;
    public float FieldOfView;
    public float Near;
    public float Far;

    public Camera3D()
    {
        Position = new Vector3(0, 2, 5);
        Target = Vector3.Zero;
        Up = Vector3.UnitY;
        FieldOfView = MathF.PI / 4f;
        Near = 0.1f;
        Far = 1000f;
    }

    public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Target, Up);

    public Matrix4x4 GetProjectionMatrix(float aspect)
        => Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Near, Far);
}
