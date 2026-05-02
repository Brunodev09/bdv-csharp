namespace BdvEngine;

public readonly record struct Vertex(float X, float Y, float Z, float U, float V)
{
    public IEnumerable<float> ToFloats()
    {
        yield return X;
        yield return Y;
        yield return Z;
        yield return U;
        yield return V;
    }
}
