using System.Numerics;

namespace BdvEngine;

public struct Color
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public Color(byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    // Derived accessors — excluded from JSON so a serialized Color is just its {R,G,B,A} bytes.
    [System.Text.Json.Serialization.JsonIgnore] public float RFloat => R / 255f;
    [System.Text.Json.Serialization.JsonIgnore] public float GFloat => G / 255f;
    [System.Text.Json.Serialization.JsonIgnore] public float BFloat => B / 255f;
    [System.Text.Json.Serialization.JsonIgnore] public float AFloat => A / 255f;

    public Vector4 ToVector4() => new(RFloat, GFloat, BFloat, AFloat);

    public static Color White   => new(255, 255, 255, 255);
    public static Color Black   => new(0, 0, 0, 255);
    public static Color Red     => new(255, 0, 0, 255);
    public static Color Green   => new(0, 255, 0, 255);
    public static Color Blue    => new(0, 0, 255, 255);
    public static Color Yellow  => new(240, 210, 70, 255);
    public static Color Orange  => new(240, 140, 50, 255);
    public static Color Cyan    => new(70, 200, 220, 255);
    public static Color Magenta => new(220, 80, 200, 255);
    public static Color Purple  => new(160, 90, 220, 255);
    public static Color Gray    => new(140, 140, 148, 255);
    public static Color Grey    => new(140, 140, 148, 255);

    /// <summary>Build a colour from 0..1 floats.</summary>
    public static Color FromFloats(float r, float g, float b, float a = 1f)
        => new((byte)System.Math.Clamp(r * 255f, 0f, 255f), (byte)System.Math.Clamp(g * 255f, 0f, 255f),
               (byte)System.Math.Clamp(b * 255f, 0f, 255f), (byte)System.Math.Clamp(a * 255f, 0f, 255f));
}
