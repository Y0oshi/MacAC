using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>Finds the part frames a motion table shows before any motion has played.</summary>
public static class MotionTableStance
{
    public static IReadOnlyList<Pose>? DefaultPhasePieceCycles(MotionBook table, Func<uint, MotionClip?> pullAnim)
    {
        if (table is null)
            return null;
        if (!table.StyleDefaults.TryGetValue(table.DefaultStyle, out MotionId substate))
            return null;

        int cycleTag = (int)(((uint)table.DefaultStyle << 16) | ((uint)substate & 0xFFFFFFu));
        if (!table.Cycles.TryGetValue(cycleTag, out var cycle) || cycle.Clips.Count is 0)
            return null;

        ClipRef lead = cycle.Clips[0];
        MotionClip? anim = pullAnim(lead.ClipId);
        if (anim is null || anim.Frames.Count is 0)
            return null;

        int cycleOrdinal = Math.Clamp((int)lead.LowFrame, 0, anim.Frames.Count - 1);
        List<Pose> cycles = anim.Frames[cycleOrdinal].Poses;
        return cycles.Count > 0 ? cycles : null;
    }
}
