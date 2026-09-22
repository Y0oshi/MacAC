using MacAC.Dat;
using MacAC.Mechanics.Data;
using MacAC.Mechanics.Kinetics;
using DatMotion =  MacAC.Dat.MotionId;

namespace MacAC.Mechanics.Geometry;

/// <summary>Finds the animation a setup plays while standing still.</summary>
public static class GaitResolver
{
    public sealed record RestCycle(
        MotionClip Animation,
        int LowFrame,
        int HighFrame,
        float Framerate);

    public static RestCycle? FetchIdleCycle(
        RigSpec rig,
        IDatRecordSource datFiles,
        IAnimReader animFetcher,
        uint? locomotionChartIdentOverride = null,
        ushort? stanceOverride = null,
        ushort? directiveOverride = null)
    {
        if (Resolve(rig, datFiles, animFetcher, locomotionChartIdentOverride, stanceOverride, directiveOverride)
            is not (MotionClip anim, ClipRef blob))
            return null;

        int previous = anim.Frames.Count - 1;
        int hi = blob.HighFrame < 0 ? previous : Math.Min(blob.HighFrame, previous);
        int lo = Math.Clamp(blob.LowFrame, 0, previous);
        if (lo > hi)
            hi = lo;
        return new RestCycle(anim, lo, hi, blob.Framerate);
    }

    public static MotionFrame? ObtainIdleCycle(
        RigSpec rig,
        IDatRecordSource datFiles,
        IAnimReader animFetcher,
        uint? locomotionChartIdentOverride = null,
        ushort? stanceOverride = null,
        ushort? directiveOverride = null)
    {
        if (Resolve(rig, datFiles, animFetcher, locomotionChartIdentOverride, stanceOverride, directiveOverride)
            is not (MotionClip anim, ClipRef blob))
            return null;

        int cycle = blob.LowFrame;
        if (cycle < 0 || cycle >= anim.Frames.Count)
            cycle = 0;
        return anim.Frames[cycle];
    }

    private static (MotionClip, ClipRef)? Resolve(
        RigSpec rig,
        IDatRecordSource datFiles,
        IAnimReader animFetcher,
        uint? locomotionChartIdentOverride,
        ushort? stanceOverride,
        ushort? directiveOverride)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(animFetcher);

        uint chartIdent = locomotionChartIdentOverride ?? (uint)rig.DefaultMotionBookId;
        if (chartIdent is 0 || datFiles.Get<MotionBook>(chartIdent) is not { } chart)
            return null;

        if (!TrySelectCycle(chart, stanceOverride, directiveOverride, out uint styling, out uint substate))
            return null;

        MotionEntry? locomotion = CycleFor(chart, styling, substate);
        if (locomotion is null || locomotion.Clips.Count is 0)
        {
            // Fall back to the table's own default cycle
            if (!ChartDefault(chart, out styling, out substate))
                return null;
            locomotion = CycleFor(chart, styling, substate);
            if (locomotion is null || locomotion.Clips.Count is 0)
                return null;
        }

        ClipRef blob = locomotion.Clips[0];
        if ((uint)blob.ClipId is 0)
            return null;

        MotionClip? anim = animFetcher.PullAnim((uint)blob.ClipId);
        return anim is { Frames.Count: > 0 } ? (anim, blob) : null;
    }

    private static bool TrySelectCycle(
        MotionBook chart,
        ushort? stanceOverride,
        ushort? directiveOverride,
        out uint styling,
        out uint substate)
    {
        if (stanceOverride is not { } stance || stance is 0)
            return ChartDefault(chart, out styling, out substate);

        styling = stance;
        if (directiveOverride is { } directive && directive is not 0)
        {
            substate = directive;
            return true;
        }
        if (chart.StyleDefaults.TryGetValue((DatMotion)styling, out DatMotion stylingDefault))
        {
            substate = (uint)stylingDefault;
            return true;
        }
        return ChartDefault(chart, out styling, out substate);
    }

    private static bool ChartDefault(MotionBook chart, out uint styling, out uint substate)
    {
        if (chart.StyleDefaults.TryGetValue(chart.DefaultStyle, out DatMotion sub))
        {
            styling = (uint)chart.DefaultStyle;
            substate = (uint)sub;
            return true;
        }
        styling = 0;
        substate = 0;
        return false;
    }

    private static MotionEntry? CycleFor(MotionBook chart, uint styling, uint substate)
    {
        return chart.Cycles.GetValueOrDefault((int)((styling << 16) | (substate & 0xFFFFFF)));
    }
}
