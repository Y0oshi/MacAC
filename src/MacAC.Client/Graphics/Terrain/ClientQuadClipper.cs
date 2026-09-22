namespace MacAC.Client.Graphics;

internal static class ClientQuadClipper
{
    public static bool TryClip(
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom,
        ref float x,
        ref float y,
        ref float w,
        ref float h,
        ref float u0,
        ref float v0,
        ref float u1,
        ref float v1)
    {
        if (clipRight <= clipLeft || clipBottom <= clipTop || w <= 0f || h <= 0f)
            return false;

        float left = MathF.Max(x, clipLeft);
        float top = MathF.Max(y, clipTop);
        float right = MathF.Min(x + w, clipRight);
        float bottom = MathF.Min(y + h, clipBottom);
        if (right <= left || bottom <= top)
            return false;

        float formerX = x, formerY = y, formerW = w, formerH = h;
        float formerU0 = u0, formerV0 = v0, formerU1 = u1, formerV1 = v1;
        float tx0 = (left - formerX) / formerW;
        float tx1 = (right - formerX) / formerW;
        float ty0 = (top - formerY) / formerH;
        float ty1 = (bottom - formerY) / formerH;

        x = left;
        y = top;
        w = right - left;
        h = bottom - top;
        u0 = formerU0 + (formerU1 - formerU0) * tx0;
        u1 = formerU0 + (formerU1 - formerU0) * tx1;
        v0 = formerV0 + (formerV1 - formerV0) * ty0;
        v1 = formerV0 + (formerV1 - formerV0) * ty1;
        return true;
    }
}
