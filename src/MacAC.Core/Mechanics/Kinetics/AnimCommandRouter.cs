namespace MacAC.Mechanics.Kinetics;

public enum AnimCommandRouteKind
{
    None = 0,
    Action,
    Modifier,
    ChatEmote,
    SubState,
    Ignored,
}

public static class AnimCommandRouter
{
    private const uint ActBit = 0x10000000u;
    private const uint ModifierBit = 0x20000000u;
    private const uint SubPhaseBit = 0x40000000u;
    private const uint ClassBitmask = 0xFF000000u;
    private const uint CommsEmoteClassA = 0x12000000u;
    private const uint CommsEmoteClassB = 0x13000000u;

    public static AnimCommandRouteKind Classify(uint wholeDirective)
    {
        if (wholeDirective is 0)
            return AnimCommandRouteKind.None;

        return (wholeDirective & ClassBitmask) switch
        {
            CommsEmoteClassA or CommsEmoteClassB => AnimCommandRouteKind.ChatEmote,
            _ when (wholeDirective & ModifierBit) is not 0 => AnimCommandRouteKind.Modifier,
            _ when (wholeDirective & ActBit) is not 0 => AnimCommandRouteKind.Action,
            _ when (wholeDirective & SubPhaseBit) is not 0 => AnimCommandRouteKind.SubState,
            _ => AnimCommandRouteKind.Ignored,
        };
    }

    public static AnimCommandRouteKind CourseWireDirective(AnimSequencer scheduler, uint latestStyling, ushort wireDirective, float paceMod = 1f)
    {
        return CourseWholeDirective(scheduler, latestStyling, MotionCommandLookup.ReconstructWholeCommand(wireDirective), paceMod);
    }

    /// <summary>Routes a full MotionCommand to the matching sequencer API.</summary>
    public static AnimCommandRouteKind CourseWholeDirective(AnimSequencer scheduler, uint latestStyling, uint wholeDirective, float paceMod = 1f)
    {
        var sort = Classify(wholeDirective);
        switch (sort)
        {
            case AnimCommandRouteKind.Action:
            case AnimCommandRouteKind.Modifier:
            case AnimCommandRouteKind.ChatEmote:
                scheduler.PlayAct(wholeDirective, paceMod);
                break;
            case AnimCommandRouteKind.SubState:
                scheduler.SetCycle(latestStyling, wholeDirective, paceMod);
                break;
        }
        return sort;
    }
}
