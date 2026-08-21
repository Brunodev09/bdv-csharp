#:project ../src/BdvEngine/BdvEngine.csproj
// A whole 3D prototype in ONE file. Run it:
//   dotnet run sketches/hello3d.cs                       (opens a window; drag to orbit, wheel to zoom)
//   dotnet run sketches/hello3d.cs -- --shot out.png     (renders a frame to out.png, then exits)
using BdvEngine;
using System.Numerics;

ObjectHandle cube = null!;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 55);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 0.6f, 0), distance: 7, yaw: 0.7f, pitch: 0.5f));
        w.Environment.Sky = new Vector3(0.45f, 0.55f, 0.70f);

        w.Add(new DirectionalLight(new Vector3(-0.5f, -1f, -0.35f)));
        w.AddPointLight(new Vector3(3, 3, 2), Color.White, intensity: 6, range: 14);

        w.Add(GridHelper.Create(20, 20));
        w.Add(Primitives.Plane(20)).At(0, 0, 0).Material(Materials.Standard(new Color(78, 100, 80)));

        w.Add(Primitives.Sphere()).At(-1.7f, 0.6f, 0)
         .Material(Materials.Pbr(new Color(220, 190, 90), metallic: 1f, roughness: 0.25f));

        cube = w.Add(Primitives.Cube()).At(1.2f, 0.5f, 0)
                .Material(Materials.Standard(new Color(210, 120, 60)));
    },
    update: (w, dt) =>
    {
        cube.Object.Transform.Rotation += new Vector3(0, (float)dt, 0);
    }
);
