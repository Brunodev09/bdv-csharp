namespace BdvEngine;

public static class GLStats
{
    public static int DrawCalls { get; private set; }
    public static int VerticesDrawn { get; private set; }
    public static int ChunksRendered { get; private set; }

    public static void IncDrawCalls(int verts = 0)
    {
        DrawCalls++;
        VerticesDrawn += verts;
    }

    public static void IncChunks() => ChunksRendered++;

    public static void Reset()
    {
        DrawCalls = 0;
        VerticesDrawn = 0;
        ChunksRendered = 0;
    }
}
