namespace BdvEngine;

public static class Collision
{
    public static bool RectRect(
        float ax, float ay, float aw, float ah,
        float bx, float by, float bw, float bh)
        => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    public static bool CircleCircle(
        float ax, float ay, float ar,
        float bx, float by, float br)
    {
        float dx = ax - bx, dy = ay - by;
        float radSum = ar + br;
        return dx * dx + dy * dy < radSum * radSum;
    }

    public static bool PointRect(
        float px, float py,
        float rx, float ry, float rw, float rh)
        => px >= rx && px <= rx + rw && py >= ry && py <= ry + rh;

    public static bool PointCircle(
        float px, float py,
        float cx, float cy, float cr)
    {
        float dx = px - cx, dy = py - cy;
        return dx * dx + dy * dy < cr * cr;
    }

    public static bool CircleRect(
        float cx, float cy, float cr,
        float rx, float ry, float rw, float rh)
    {
        float nearestX = MathF.Max(rx, MathF.Min(cx, rx + rw));
        float nearestY = MathF.Max(ry, MathF.Min(cy, ry + rh));
        float dx = cx - nearestX, dy = cy - nearestY;
        return dx * dx + dy * dy < cr * cr;
    }

    public static bool LineRect(
        float x1, float y1, float x2, float y2,
        float rx, float ry, float rw, float rh)
    {
        if (PointRect(x1, y1, rx, ry, rw, rh)) return true;
        if (PointRect(x2, y2, rx, ry, rw, rh)) return true;

        if (LineLine(x1, y1, x2, y2, rx, ry, rx + rw, ry)) return true;
        if (LineLine(x1, y1, x2, y2, rx + rw, ry, rx + rw, ry + rh)) return true;
        if (LineLine(x1, y1, x2, y2, rx, ry + rh, rx + rw, ry + rh)) return true;
        if (LineLine(x1, y1, x2, y2, rx, ry, rx, ry + rh)) return true;

        return false;
    }

    public static bool LineLine(
        float x1, float y1, float x2, float y2,
        float x3, float y3, float x4, float y4)
    {
        float denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
        if (denom == 0f) return false;

        float ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
        float ub = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3)) / denom;

        return ua >= 0f && ua <= 1f && ub >= 0f && ub <= 1f;
    }

    public readonly record struct Mtv(float X, float Y);

    public static Mtv? RectOverlap(
        float ax, float ay, float aw, float ah,
        float bx, float by, float bw, float bh)
    {
        float overlapX = MathF.Min(ax + aw - bx, bx + bw - ax);
        float overlapY = MathF.Min(ay + ah - by, by + bh - ay);

        if (overlapX <= 0f || overlapY <= 0f) return null;

        if (overlapX < overlapY)
            return new Mtv(ax + aw / 2 < bx + bw / 2 ? -overlapX : overlapX, 0);
        else
            return new Mtv(0, ay + ah / 2 < by + bh / 2 ? -overlapY : overlapY);
    }

    public static float RayRect(
        float originX, float originY,
        float dirX, float dirY,
        float rx, float ry, float rw, float rh)
    {
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;

        if (dirX != 0f)
        {
            float t1 = (rx - originX) / dirX;
            float t2 = (rx + rw - originX) / dirX;
            tmin = MathF.Max(tmin, MathF.Min(t1, t2));
            tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        }
        else if (originX < rx || originX > rx + rw) return -1f;

        if (dirY != 0f)
        {
            float t1 = (ry - originY) / dirY;
            float t2 = (ry + rh - originY) / dirY;
            tmin = MathF.Max(tmin, MathF.Min(t1, t2));
            tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        }
        else if (originY < ry || originY > ry + rh) return -1f;

        if (tmax >= tmin && tmax >= 0f) return tmin >= 0f ? tmin : tmax;
        return -1f;
    }
}
