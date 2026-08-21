using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>A ground grid (the Three.js <c>GridHelper</c>) — an unlit line mesh on the XZ plane.
/// Add via <c>World.Add(GridHelper.Create())</c>.</summary>
public static class GridHelper
{
    public static SimObject Create(float size = 20f, int divisions = 20, Color? color = null, int id = 990001)
    {
        float half = size / 2f, step = size / divisions;
        var v = new List<float>((divisions + 1) * 4 * Mesh.FloatsPerVertex);

        void Line(float ax, float az, float bx, float bz)
        {
            // pos(3) + normal(3, unused by unlit) + uv(2)
            v.AddRange(new[] { ax, 0f, az, 0f, 1f, 0f, 0f, 0f });
            v.AddRange(new[] { bx, 0f, bz, 0f, 1f, 0f, 0f, 0f });
        }

        for (int i = 0; i <= divisions; i++)
        {
            float p = -half + i * step;
            Line(p, -half, p, half);   // lines along Z
            Line(-half, p, half, p);   // lines along X
        }

        var mesh = new Mesh(v.ToArray()) { Primitive = PrimitiveType.Lines };
        var obj = new SimObject(id, "grid");
        obj.AddComponent(new MeshComponent(mesh, Materials.Unlit(color ?? new Color(64, 74, 90))));
        return obj;
    }
}

/// <summary>Origin axes (the Three.js <c>AxesHelper</c>) — three unlit coloured bars: X red,
/// Y green, Z blue. Add via <c>World.Add(AxesHelper.Create())</c>.</summary>
public static class AxesHelper
{
    public static SimObject Create(float length = 2f, float thickness = 0.05f, int id = 990010)
    {
        var root = new SimObject(id, "axes");
        Bar(root, id + 1, "x", new Vector3(length / 2f, 0, 0), new Vector3(length, thickness, thickness), new Color(230, 70, 70));
        Bar(root, id + 2, "y", new Vector3(0, length / 2f, 0), new Vector3(thickness, length, thickness), new Color(70, 210, 90));
        Bar(root, id + 3, "z", new Vector3(0, 0, length / 2f), new Vector3(thickness, thickness, length), new Color(80, 130, 240));
        return root;
    }

    private static void Bar(SimObject root, int id, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var o = new SimObject(id, name);
        o.Transform.Position = pos;
        o.Transform.Scale = scale;
        o.AddComponent(new MeshComponent(Mesh.Cube(), Materials.Unlit(color)));
        root.AddChild(o);
    }
}
