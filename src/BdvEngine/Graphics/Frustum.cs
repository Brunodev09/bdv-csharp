using System.Numerics;

namespace BdvEngine;

/// <summary>
/// The six clipping planes of a view-projection matrix, for rejecting objects the camera can't see
/// before they cost a draw call.
///
/// <para>Planes are extracted from the matrix rather than from camera angles, so the same code
/// works for the perspective camera, the orthographic 2D camera, and the sun's orthographic shadow
/// frustum — which is why the shadow pass gets culling for free.</para>
/// </summary>
public readonly struct Frustum
{
    // Each plane as (normal.xyz, distance), normals pointing INWARD: a point is inside when
    // dot(normal, p) + d >= 0 for all six.
    private readonly Vector4 _l, _r, _b, _t, _n, _f;

    /// <summary>
    /// Extract from a combined view-projection matrix.
    ///
    /// <para>The engine uses row-vector maths (<c>clip = v * VP</c>), so a clip component is a dot
    /// with a COLUMN of the matrix. The left plane is <c>clip.x + clip.w >= 0</c>, hence column 1
    /// plus column 4, and so on — a row-vector derivation, not the transposed one most references
    /// print.</para>
    /// </summary>
    public Frustum(in Matrix4x4 vp)
    {
        var cx = new Vector4(vp.M11, vp.M21, vp.M31, vp.M41);
        var cy = new Vector4(vp.M12, vp.M22, vp.M32, vp.M42);
        var cz = new Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
        var cw = new Vector4(vp.M14, vp.M24, vp.M34, vp.M44);

        _l = Normalize(cw + cx);
        _r = Normalize(cw - cx);
        _b = Normalize(cw + cy);
        _t = Normalize(cw - cy);
        _n = Normalize(cw + cz);   // GL clip space is -1..1 in Z, so near is w + z
        _f = Normalize(cw - cz);
    }

    private static Vector4 Normalize(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 1e-8f ? p / len : p;
    }

    /// <summary>
    /// Conservative AABB test: false only when the box is entirely outside one plane.
    ///
    /// <para>Uses the box's projection onto each plane normal, which can keep a box that is outside
    /// the frustum but straddles two planes' half-spaces. Being conservative is the right error —
    /// a false keep costs one draw call, a false reject makes geometry pop out of existence.</para>
    /// </summary>
    public bool Intersects(in Bounds b)
    {
        var c = b.Center;
        var e = b.Extents;
        return !Outside(_l, c, e) && !Outside(_r, c, e) && !Outside(_b, c, e)
            && !Outside(_t, c, e) && !Outside(_n, c, e) && !Outside(_f, c, e);
    }

    private static bool Outside(in Vector4 plane, in Vector3 c, in Vector3 e)
    {
        // Distance from the box centre to the plane, minus the box's reach along the plane normal.
        float dist = plane.X * c.X + plane.Y * c.Y + plane.Z * c.Z + plane.W;
        float reach = MathF.Abs(plane.X) * e.X + MathF.Abs(plane.Y) * e.Y + MathF.Abs(plane.Z) * e.Z;
        return dist + reach < 0f;
    }

    /// <summary>
    /// World-space AABB of a local AABB under a transform.
    ///
    /// <para>Takes the centre through the matrix and scales the extents by its absolute values,
    /// rather than transforming all eight corners — same result, a third of the work, and this runs
    /// for every object every frame.</para>
    /// </summary>
    public static Bounds TransformBounds(in Bounds local, in Matrix4x4 m)
    {
        var center = Vector3.Transform(local.Center, m);
        var e = local.Extents;
        var extents = new Vector3(
            MathF.Abs(m.M11) * e.X + MathF.Abs(m.M21) * e.Y + MathF.Abs(m.M31) * e.Z,
            MathF.Abs(m.M12) * e.X + MathF.Abs(m.M22) * e.Y + MathF.Abs(m.M32) * e.Z,
            MathF.Abs(m.M13) * e.X + MathF.Abs(m.M23) * e.Y + MathF.Abs(m.M33) * e.Z);
        return Bounds.FromCenterExtents(center, extents);
    }
}
