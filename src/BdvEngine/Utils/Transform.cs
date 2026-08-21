using System.Numerics;

namespace BdvEngine;

public sealed class Transform
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.One;

    /// <summary>Optional quaternion orientation. When <see cref="UseOrientation"/> is true,
    /// <see cref="GetMatrix"/> uses this instead of the Euler <see cref="Rotation"/> — no gimbal
    /// lock, and "face a direction" becomes a one-liner via <see cref="LookRotation"/>. Euler
    /// stays the default so existing objects are unchanged.</summary>
    public Quaternion Orientation = Quaternion.Identity;
    public bool UseOrientation;

    public void CopyFrom(Transform other)
    {
        Position = other.Position;
        Rotation = other.Rotation;
        Scale = other.Scale;
        Orientation = other.Orientation;
        UseOrientation = other.UseOrientation;
    }

    /// <summary>Orient so local +Z (forward) points along <paramref name="forward"/>. Switches the
    /// transform onto the quaternion path.</summary>
    public void LookRotation(Vector3 forward, Vector3? up = null)
    {
        if (forward.LengthSquared() < 1e-8f) return;
        forward = Vector3.Normalize(forward);
        var u = up ?? Vector3.UnitY;
        var right = Vector3.Cross(u, forward);
        right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
        var trueUp = Vector3.Cross(forward, right);
        var m = new Matrix4x4(
            right.X,  right.Y,  right.Z,  0,
            trueUp.X, trueUp.Y, trueUp.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1);
        Orientation = Quaternion.CreateFromRotationMatrix(m);
        UseOrientation = true;
    }

    public Matrix4x4 GetMatrix()
    {
        var rotation = UseOrientation
            ? Matrix4x4.CreateFromQuaternion(Orientation)
            : Matrix4x4.CreateRotationX(Rotation.X) *
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
