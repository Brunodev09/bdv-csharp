#:project ../src/BdvEngine/BdvEngine.csproj
// A whole 2D prototype in ONE file. Run it:
//   dotnet run sketches/hello2d.cs
//   dotnet run sketches/hello2d.cs -- --shot out.png
using BdvEngine;
using System;
using System.Numerics;

double t = 0;

Sketch.Run(
    // 2D = an orthographic camera; X/Y pan, Zoom scales. Here we frame a 1280x720 area.
    setup: w =>
    {
        w.Camera.Orthographic();
        w.Camera.X = 640;
        w.Camera.Y = 360;
        w.Environment.Sky = new Vector3(0.10f, 0.12f, 0.16f);
    },
    update: (w, dt) => t += dt,
    // Immediate-mode 2D drawing (Draw.* / SpriteBatcher.*), in world coords under the camera.
    draw: w =>
    {
        for (int i = 0; i < 9; i++)
        {
            float x = 190 + i * 105;
            float h = 130 + 95 * MathF.Sin((float)t * 1.5f + i * 0.6f);
            Draw.Rect(x, 520 - h, 70, h, new Color((byte)(60 + i * 20), 150, 225));
        }
        Draw.Rect(150, 520, 960, 8, new Color(210, 214, 226));   // ground line
    }
);
