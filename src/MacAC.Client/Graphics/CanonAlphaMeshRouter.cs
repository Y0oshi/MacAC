using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics;

// What a routed subset does with the two alpha lists
internal enum CanonAlphaMeshAction
{
    // Rows 1/5 - draw now; never touches a list
    Immediate,

    // Rows 3/4 - append only; no immediate draw this frame
    Append,

    AppendClipAndImmediate,
}

internal readonly record struct CanonAlphaMeshDecision(
    CanonAlphaMeshAction Action,
    CanonAlphaList List,
    bool OverrideClipmap);

internal static class CanonAlphaMeshRouter
{
    internal const byte BitmaskAlphaClan = 0x02;

    internal const byte BitmaskTranslucent = 0x04;

    internal const byte BitmaskClipLookup = 0x08;

    internal const byte BitmaskPositiveStipple = 0x01;

    internal const byte DefaultDelayBitmask = 0x0E;

    internal static byte FabricateSubsetBitmask(
        bool hasAlphaClanBit,
        bool hasClipLookupBit,
        bool hasTranslucentBit,
        bool hasPositiveStippling)
    {
        byte bitmask = hasAlphaClanBit
            ? BitmaskAlphaClan
            : hasClipLookupBit
                ? BitmaskClipLookup
                : hasTranslucentBit
                    ? BitmaskTranslucent
                    : (byte)0;
        if (hasPositiveStippling)
            bitmask |= BitmaskPositiveStipple;
        return bitmask;
    }

    internal static byte ConcealFromSeeThroughSort(SeeThroughKind sort)
    {
        return sort switch
        {
            SeeThroughKind.ClipMap => BitmaskClipLookup,
            SeeThroughKind.Opaque => 0,
            _ => BitmaskAlphaClan, // AlphaBlend, Additive, InvAlpha
        };
    }

    internal static CanonAlphaMeshDecision Course(
        bool currentlyDrawingHeavens,
        byte delayBitmask,
        bool specificsCanvasEngaged,
        bool multiPassAlpha,
        byte subsetBitmask,
        bool matlHasAlpha)
    {
        if (currentlyDrawingHeavens || delayBitmask is 0 || specificsCanvasEngaged)
            return new CanonAlphaMeshDecision(CanonAlphaMeshAction.Immediate, default, false);

        bool clipLookupBitSet = (subsetBitmask & BitmaskClipLookup) is not 0;

        if (multiPassAlpha && clipLookupBitSet)
        {
            return new CanonAlphaMeshDecision(
                CanonAlphaMeshAction.AppendClipAndImmediate, CanonAlphaList.Clip, true);
        }

        if ((delayBitmask & subsetBitmask) is not 0)
        {
            return new CanonAlphaMeshDecision(
                CanonAlphaMeshAction.Append,
                clipLookupBitSet ? CanonAlphaList.Clip : CanonAlphaList.Alpha,
                false);
        }

        if ((delayBitmask & BitmaskTranslucent) is not 0 && matlHasAlpha)
            return new CanonAlphaMeshDecision(CanonAlphaMeshAction.Append, CanonAlphaList.Alpha, false);

        return new CanonAlphaMeshDecision(CanonAlphaMeshAction.Immediate, default, false);
    }
}
