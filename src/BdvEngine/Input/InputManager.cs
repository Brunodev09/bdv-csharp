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
    private static readonly HashSet<Key> _pressedThisFrame = new();
    private static readonly System.Text.StringBuilder _typedChars = new();

    private static float _mouseX, _mouseY;
    private static bool _leftDown, _rightDown;
    private static float _wheelDelta;

    public static void Initialize(IInputContext input)
    {
        _input = input;

        foreach (var kb in input.Keyboards)
        {
            kb.KeyDown += (_, key, _) => { var k = (Key)key; if (_keysDown.Add(k)) _pressedThisFrame.Add(k); };
            kb.KeyUp += (_, key, _) => _keysDown.Remove((Key)key);
            kb.KeyChar += (_, c) => _typedChars.Append(c);
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
    /// <summary>True only on the frame the key transitioned from up to down. Cleared
    /// at the end of each engine update via <see cref="EndFrame"/>.</summary>
    public static bool WasKeyPressed(Key key) => _pressedThisFrame.Contains(key);

    /// <summary>Drain the typed-character buffer (for text input fields). Returns the
    /// string of printable chars typed since the last call, then clears it.</summary>
    public static string ConsumeTypedString()
    {
        if (_typedChars.Length == 0) return "";
        var s = _typedChars.ToString();
        _typedChars.Clear();
        return s;
    }

    public static float ConsumeWheelDelta()
    {
        var d = _wheelDelta;
        _wheelDelta = 0f;
        return d;
    }

    /// <summary>Called by the engine at the end of each Update tick. Clears edge state
    /// so WasKeyPressed only fires on the actual transition frame.</summary>
    public static void EndFrame()
    {
        _pressedThisFrame.Clear();
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
