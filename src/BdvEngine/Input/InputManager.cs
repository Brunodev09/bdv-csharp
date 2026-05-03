using System.Numerics;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;

namespace BdvEngine;

public sealed class MouseContext
{
    public bool LeftDown { get; }
    public bool RightDown { get; }
    public Vector2 Position { get; }

    public MouseContext(bool leftDown, bool rightDown, Vector2 position)
    {
        LeftDown = leftDown;
        RightDown = rightDown;
        Position = position;
    }
}

public static class InputManager
{
    private static IInputContext? _input;
    private static readonly HashSet<Key> _keysDown = new(); // BdvEngine.Key

    private static float _mouseX, _mouseY;
    private static bool _leftDown, _rightDown;
    private static float _wheelDelta;

    public static void Initialize(IInputContext input)
    {
        _input = input;

        foreach (var kb in input.Keyboards)
        {
            kb.KeyDown += (_, key, _) => _keysDown.Add((Key)key);
            kb.KeyUp += (_, key, _) => _keysDown.Remove((Key)key);
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseMove += (_, pos) => { _mouseX = pos.X; _mouseY = pos.Y; };
            mouse.MouseDown += (_, btn) =>
            {
                if (btn == MouseButton.Left) _leftDown = true;
                else if (btn == MouseButton.Right) _rightDown = true;
                Message.Send("MOUSE_DOWN", typeof(InputManager),
                    new MouseContext(_leftDown, _rightDown, GetMousePosition()));
            };
            mouse.MouseUp += (_, btn) =>
            {
                if (btn == MouseButton.Left) _leftDown = false;
                else if (btn == MouseButton.Right) _rightDown = false;
                Message.Send("MOUSE_UP", typeof(InputManager),
                    new MouseContext(_leftDown, _rightDown, GetMousePosition()));
            };
            mouse.Scroll += (_, wheel) => _wheelDelta += wheel.Y;
        }
    }

    public static bool IsKeyDown(Key key) => _keysDown.Contains(key);

    public static float ConsumeWheelDelta()
    {
        var d = _wheelDelta;
        _wheelDelta = 0f;
        return d;
    }

    public static Vector2 GetMousePosition() => new(_mouseX, _mouseY);
    public static bool IsLeftDown => _leftDown;
    public static bool IsRightDown => _rightDown;

    public static void Shutdown()
    {
        _input = null;
        _keysDown.Clear();
    }
}
