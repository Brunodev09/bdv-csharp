using SilkKey = Silk.NET.Input.Key;

namespace BdvEngine;

// Engine-facing key enum. Values mirror Silk's so we can cast at the boundary
// without a lookup table; consumers don't need to reference Silk directly.
public enum Key
{
    Unknown      = SilkKey.Unknown,

    Space        = SilkKey.Space,
    Apostrophe   = SilkKey.Apostrophe,
    Comma        = SilkKey.Comma,
    Minus        = SilkKey.Minus,
    Period       = SilkKey.Period,
    Slash        = SilkKey.Slash,
    Semicolon    = SilkKey.Semicolon,
    Equal        = SilkKey.Equal,
    LeftBracket  = SilkKey.LeftBracket,
    Backslash    = SilkKey.BackSlash,
    RightBracket = SilkKey.RightBracket,
    GraveAccent  = SilkKey.GraveAccent,

    Number0 = SilkKey.Number0, Number1 = SilkKey.Number1, Number2 = SilkKey.Number2,
    Number3 = SilkKey.Number3, Number4 = SilkKey.Number4, Number5 = SilkKey.Number5,
    Number6 = SilkKey.Number6, Number7 = SilkKey.Number7, Number8 = SilkKey.Number8,
    Number9 = SilkKey.Number9,

    A = SilkKey.A, B = SilkKey.B, C = SilkKey.C, D = SilkKey.D, E = SilkKey.E,
    F = SilkKey.F, G = SilkKey.G, H = SilkKey.H, I = SilkKey.I, J = SilkKey.J,
    K = SilkKey.K, L = SilkKey.L, M = SilkKey.M, N = SilkKey.N, O = SilkKey.O,
    P = SilkKey.P, Q = SilkKey.Q, R = SilkKey.R, S = SilkKey.S, T = SilkKey.T,
    U = SilkKey.U, V = SilkKey.V, W = SilkKey.W, X = SilkKey.X, Y = SilkKey.Y,
    Z = SilkKey.Z,

    Escape       = SilkKey.Escape,
    Enter        = SilkKey.Enter,
    Tab          = SilkKey.Tab,
    Backspace    = SilkKey.Backspace,
    Insert       = SilkKey.Insert,
    Delete       = SilkKey.Delete,
    Right        = SilkKey.Right,
    Left         = SilkKey.Left,
    Down         = SilkKey.Down,
    Up           = SilkKey.Up,
    PageUp       = SilkKey.PageUp,
    PageDown     = SilkKey.PageDown,
    Home         = SilkKey.Home,
    End          = SilkKey.End,
    CapsLock     = SilkKey.CapsLock,
    ScrollLock   = SilkKey.ScrollLock,
    NumLock      = SilkKey.NumLock,
    PrintScreen  = SilkKey.PrintScreen,
    Pause        = SilkKey.Pause,

    F1  = SilkKey.F1,  F2  = SilkKey.F2,  F3  = SilkKey.F3,  F4  = SilkKey.F4,
    F5  = SilkKey.F5,  F6  = SilkKey.F6,  F7  = SilkKey.F7,  F8  = SilkKey.F8,
    F9  = SilkKey.F9,  F10 = SilkKey.F10, F11 = SilkKey.F11, F12 = SilkKey.F12,

    Keypad0 = SilkKey.Keypad0, Keypad1 = SilkKey.Keypad1, Keypad2 = SilkKey.Keypad2,
    Keypad3 = SilkKey.Keypad3, Keypad4 = SilkKey.Keypad4, Keypad5 = SilkKey.Keypad5,
    Keypad6 = SilkKey.Keypad6, Keypad7 = SilkKey.Keypad7, Keypad8 = SilkKey.Keypad8,
    Keypad9 = SilkKey.Keypad9,
    KeypadDecimal  = SilkKey.KeypadDecimal,
    KeypadDivide   = SilkKey.KeypadDivide,
    KeypadMultiply = SilkKey.KeypadMultiply,
    KeypadSubtract = SilkKey.KeypadSubtract,
    KeypadAdd      = SilkKey.KeypadAdd,
    KeypadEnter    = SilkKey.KeypadEnter,
    KeypadEqual    = SilkKey.KeypadEqual,

    ShiftLeft   = SilkKey.ShiftLeft,
    ShiftRight  = SilkKey.ShiftRight,
    ControlLeft = SilkKey.ControlLeft,
    ControlRight= SilkKey.ControlRight,
    AltLeft     = SilkKey.AltLeft,
    AltRight    = SilkKey.AltRight,
    SuperLeft   = SilkKey.SuperLeft,
    SuperRight  = SilkKey.SuperRight,
    Menu        = SilkKey.Menu,
}
