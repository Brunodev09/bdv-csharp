using System.Numerics;
using BdvEngine;

namespace My3DGameApp;

public sealed class My3DGame : Game3D
{
    private readonly Scene _scene = new();
    private double _elapsed;

    public override void Init()
    {
        MaterialManager.Register(new Material("crate", "textures/block.png", Color.White));
        MaterialManager.Register(new Material("white", "textures/block.png", new Color(200, 200, 220, 255)));

        var cube = new SimObject(1, "cube");
        cube.Transform.Position = new Vector3(0, 0.5f, 0);
        cube.AddComponent(new MeshComponent(Mesh.Cube(), "crate"));
        _scene.AddObject(cube);

        var child = new SimObject(2, "child");
        child.Transform.Position = new Vector3(2, 0, 0);
        child.Transform.Scale = new Vector3(0.4f);
        child.AddComponent(new MeshComponent(Mesh.Cube(), "crate"));
        cube.AddChild(child);

        var grand = new SimObject(5, "grandchild");
        grand.Transform.Position = new Vector3(1.5f, 0, 0);
        grand.Transform.Scale = new Vector3(0.5f);
        grand.AddComponent(new MeshComponent(Mesh.Sphere(12, 8), "white"));
        child.AddChild(grand);

        var sphere = new SimObject(3, "sphere");
        sphere.Transform.Position = new Vector3(-2, 0.5f, 0);
        sphere.AddComponent(new MeshComponent(Mesh.Sphere(24, 16), "white"));
        _scene.AddObject(sphere);

        var ground = new SimObject(4, "ground");
        ground.Transform.Scale = new Vector3(10, 1, 10);
        ground.AddComponent(new MeshComponent(Mesh.Plane(1), "white"));
        _scene.AddObject(ground);

        _scene.Load();

        var panel = UI.Panel(UIAnchor.TopLeft);
        UI.Heading(panel, "BdvEngine 3D");
        UI.Text(panel, "Parent → child → grandchild hierarchy with Phong lighting");
    }

    public override void Update(double deltaTime)
    {
        _elapsed += deltaTime;
        _scene.Update(deltaTime);

        var cube = _scene.GetObjectByName("cube");
        if (cube != null) cube.Transform.Rotation = new Vector3(0, (float)(_elapsed * 0.8), 0);

        var child = _scene.GetObjectByName("child");
        if (child != null) child.Transform.Rotation = new Vector3(0, (float)(_elapsed * 3), 0);

        var grand = _scene.GetObjectByName("grandchild");
        if (grand != null) grand.Transform.Rotation = new Vector3((float)(_elapsed * 2), 0, 0);

        float a = (float)(_elapsed * 0.3);
        Camera.Position = new Vector3(MathF.Cos(a) * 6f, 3f, MathF.Sin(a) * 6f);
        Camera.Target = new Vector3(0, 0.5f, 0);
    }

    private int _frame;
    public override void Render(Shader shader)
    {
        _scene.Render(shader);
        if (++_frame == 120) Screenshot.PendingPath = "/tmp/my3dgame.ppm";
    }
}
