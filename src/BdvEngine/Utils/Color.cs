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

    public float RFloat => R / 255f;
    public float GFloat => G / 255f;
    public float BFloat => B / 255f;
    public float AFloat => A / 255f;

    public Vector4 ToVector4() => new(RFloat, GFloat, BFloat, AFloat);

    public static Color White => new(255, 255, 255, 255);
    public static Color Black => new(0, 0, 0, 255);
    public static Color Red   => new(255, 0, 0, 255);
    public static Color Green => new(0, 255, 0, 255);
    public static Color Blue  => new(0, 0, 255, 255);
}
