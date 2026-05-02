using System.Numerics;

namespace BdvEngine;

public sealed class Transform
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.One;

    public void CopyFrom(Transform other)
    {
        Position = other.Position;
        Rotation = other.Rotation;
        Scale = other.Scale;
    }

    public Matrix4x4 GetMatrix()
    {
        var rotation =
            Matrix4x4.CreateRotationX(Rotation.X) *
            Matrix4x4.CreateRotationY(Rotation.Y) *
            Matrix4x4.CreateRotationZ(Rotation.Z);

        return Matrix4x4.CreateScale(Scale) * rotation * Matrix4x4.CreateTranslation(Position);
    }

    public void SetFromJson(System.Text.Json.JsonElement json)
    {
        if (json.TryGetProperty("position", out var p)) Position = ReadVec3(p);
        if (json.TryGetProperty("rotation", out var r)) Rotation = ReadVec3(r);
        if (json.TryGetProperty("scale", out var s)) Scale = ReadVec3(s);
    }

    private static Vector3 ReadVec3(System.Text.Json.JsonElement e)
    {
        float x = e.TryGetProperty("x", out var xe) ? xe.GetSingle() : 0f;
        float y = e.TryGetProperty("y", out var ye) ? ye.GetSingle() : 0f;
        float z = e.TryGetProperty("z", out var ze) ? ze.GetSingle() : 0f;
        return new Vector3(x, y, z);
    }
}
