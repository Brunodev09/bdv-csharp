using System;
using System.Numerics;
using Silk.NET.Input;

namespace BdvEngine;

/// <summary>
/// Three.js-style orbit camera: left-drag (or arrow keys) to orbit a target, mouse-wheel to zoom.
/// Attach with <c>Camera.AddControls(new OrbitControls(...))</c> and the engine drives it each
/// frame — no more hand-rolled camera trig in game code.
/// </summary>
public sealed class OrbitControls : ICameraController
{
    public Vector3 Target;
    public float Distance;
    public float Yaw;
    public float Pitch;

    public float MinPitch = 0.05f, MaxPitch = 1.50f;
    public float MinDistance = 1.5f, MaxDistance = 80f;
    public float RotateSpeed = 0.007f, KeySpeed = 1.6f, ZoomSpeed = 1.5f;

    private Vector2 _lastMouse;
    private bool _haveMouse;

    public OrbitControls(Vector3? target = null, float distance = 10f, float yaw = 0.7f, float pitch = 0.5f)
    {
        Target = target ?? Vector3.Zero;
        Distance = distance;
        Yaw = yaw;
        Pitch = pitch;
    }

    public void Update(Camera camera, double dt)
    {
        var mouse = InputManager.GetMousePosition();
        if (!_haveMouse) { _lastMouse = mouse; _haveMouse = true; }
        var delta = mouse - _lastMouse;
        _lastMouse = mouse;

        // Keep tracking the cursor even while the UI owns it, so releasing an editor panel or a
        // gizmo handle doesn't snap the camera by the accumulated delta.
        bool uiOwnsMouse = InputManager.UiWantsMouse;

        if (InputManager.IsLeftDown && !uiOwnsMouse)
        {
            Yaw -= delta.X * RotateSpeed;
            Pitch -= delta.Y * RotateSpeed;
        }

        float k = (float)dt;
        if (InputManager.IsKeyDown(Key.Left))  Yaw   += k * KeySpeed;
        if (InputManager.IsKeyDown(Key.Right)) Yaw   -= k * KeySpeed;
        if (InputManager.IsKeyDown(Key.Up))    Pitch += k * KeySpeed;
        if (InputManager.IsKeyDown(Key.Down))  Pitch -= k * KeySpeed;
        Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);

        float wheel = InputManager.ConsumeWheelDelta();
        if (uiOwnsMouse) wheel = 0f;              // scrolling a panel must not zoom the world
        if (wheel != 0f) Distance = Math.Clamp(Distance - wheel * ZoomSpeed, MinDistance, MaxDistance);

        float cp = MathF.Cos(Pitch);
        var dir = new Vector3(cp * MathF.Sin(Yaw), MathF.Sin(Pitch), cp * MathF.Cos(Yaw));
        camera.Position = Target + dir * Distance;
        camera.Target = Target;
    }
}
