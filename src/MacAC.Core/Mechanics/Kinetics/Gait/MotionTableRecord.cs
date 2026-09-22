using MacAC.Dat;
using DRWMotionCommand =  MacAC.Dat.MotionId;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class MotionTableRecord
{
    private const float Epsilon = 0.000199999995f;
    private const uint StylingBit = 0x80000000u;
    private const uint CycleBit = 0x40000000u;
    private const uint ModifierBit = 0x20000000u;
    private const uint ActBit = 0x10000000u;
    private const uint SubstateBitmask = 0xFFFFFFu;

    // MotionData bitfield: entering this cycle drops every modifier
    private const uint ClearsModifiers = 1u;

    // MotionData bitfield: only reachable from the style's default substate
    private const uint FromDefaultSole = 2u;

    private readonly MotionBook _chart;

    public MotionTableRecord(MotionBook chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        _chart = chart;
    }

    private uint DefaultStyle => (uint)_chart.DefaultStyle;

    public bool IsAllowed(uint contenderSubstate, MotionEntry? contender, LocomotionPhase phase)
    {
        if (contender is null)
            return false;
        if ((contender.Bitfield & FromDefaultSole) is 0 || contenderSubstate == phase.Substate)
            return true;
        return ConsultStylingDefault(phase.Style) == phase.Substate;
    }

    public MotionEntry? GetLink(uint fromStyling, uint fromSubstate, float fromSubstateMod, uint toSubstate, float toSubstateMod)
    {
        if (toSubstateMod < 0f || fromSubstateMod < 0f)
        {
            // Reversed-direction path: link FROM toSubstate TO fromSubstate,
            // else from fromSubstate back to the style default.
            return ConnectListing(Key(fromStyling, toSubstate), fromSubstate)
                ?? ConnectListing(Key(fromStyling, fromSubstate), ConsultStylingDefault(fromStyling));
        }

        // Forward-direction path: link FROM fromSubstate TO toSubstate,
        // else the style's unkeyed link table.
        return ConnectListing(Key(fromStyling, fromSubstate), toSubstate)
            ?? ConnectListing((int)(fromStyling << 16), toSubstate);
    }

    public bool FetchObjectSeries(uint locomotion, LocomotionPhase phase, AnimTrack series, float pace, out uint outBeats, bool haltCall)
    {
        outBeats = 0;

        uint styling = phase.Style;
        uint substate = phase.Substate;
        if (styling is 0 || substate is 0)
            return false;

        uint stylingDefault = ConsultStylingDefault(styling);
        if (locomotion == stylingDefault && !haltCall && (substate & ModifierBit) is not 0)
            return true;

        if ((int)locomotion < 0)
        {
            if (styling == locomotion)
                return true; // already in that style
            if (SwitchStyling(locomotion, phase, series, pace, out outBeats))
                return true;
        }

        if ((locomotion & CycleBit) is not 0 && SwitchCycle(locomotion, phase, series, pace, out outBeats))
            return true;

        if ((locomotion & ActBit) is not 0 && PlayAct(locomotion, phase, series, pace, out outBeats))
            return true;

        if ((locomotion & ModifierBit) is not 0 && ImposeModifier(locomotion, phase, series, pace))
            return true;

        return false;
    }

    /// <summary>Re-applies every active modifier after the cycle underneath them changed.</summary>
    public void ReModify(AnimTrack series, LocomotionPhase phase)
    {
        if (!phase.Modifiers.Any())
            return;

        LocomotionPhase queued = new LocomotionPhase(phase);
        do
        {
            MotionRow front = phase.Modifiers.First();
            uint locomotion = front.Locomotion;
            float paceMod = front.PaceMod;
            phase.DropModifier(front);
            queued.DropModifier(queued.Modifiers.First());

            FetchObjectSeries(locomotion, phase, series, paceMod, out _, haltCall: false);
        } while (queued.Modifiers.Any());
    }

    public bool HaltSeriesLocomotion(uint locomotion, float pace, LocomotionPhase phase, AnimTrack series, out uint outBeats)
    {
        outBeats = 0;

        if ((locomotion & CycleBit) is not 0 && locomotion == phase.Substate)
        {
            FetchObjectSeries(ConsultStylingDefault(phase.Style), phase, series, 1f, out outBeats, haltCall: true);
            return true;
        }

        if ((locomotion & ModifierBit) is 0)
            return false;

        foreach (MotionRow rank in phase.Modifiers)
        {
            if (rank.Locomotion != locomotion)
                continue;

            MotionEntry? modifier = ConsultModifier(phase.Style, locomotion & SubstateBitmask);
            if (modifier is null)
                return false; // matching motion id found but no MotionData anywhere -> give up

            SubtractLocomotion(series, modifier, rank.PaceMod);
            phase.DropModifier(rank);
            return true;
        }

        return false;
    }

    public bool AssignDefaultPhase(LocomotionPhase phase, AnimTrack series, out uint outBeats)
    {
        outBeats = 0;
        if (!_chart.StyleDefaults.TryGetValue(_chart.DefaultStyle, out DRWMotionCommand defaultSubstateCmd))
            return false;

        uint defaultSubstate = (uint)defaultSubstateCmd;
        phase.WipeModifiers();
        phase.WipeActs();

        MotionEntry? cyclic = ConsultCycle(DefaultStyle, defaultSubstate);
        if (cyclic is null)
            return false;

        phase.Style = DefaultStyle;
        phase.Substate = defaultSubstate;
        phase.SubstateMod = 1f;
        outBeats = AnimTally(cyclic) - 1;

        series.WipeKinetics();
        series.WipeAnims();
        AppendLocomotion(series, cyclic, phase.SubstateMod);
        return true;
    }

    public bool DoObjectLocomotion(uint locomotion, LocomotionPhase phase, AnimTrack series, float pace, out uint outBeats)
    {
        return FetchObjectSeries(locomotion, phase, series, pace, out outBeats, haltCall: false);
    }

    public bool HaltObjectLocomotion(uint locomotion, float pace, LocomotionPhase phase, AnimTrack series, out uint outBeats) =>
        HaltSeriesLocomotion(locomotion, pace, phase, series, out outBeats);

    public bool HaltObjectCompletely(LocomotionPhase phase, AnimTrack series, out uint outBeats)
    {
        outBeats = 0;
        bool stoppedAModifier = false;
        while (phase.Modifiers.Any())
        {
            MotionRow rank = phase.Modifiers.First();
            if (!HaltSeriesLocomotion(rank.Locomotion, rank.PaceMod, phase, series, out outBeats))
                break; // defensive: avoid infinite loop if a stop can't unlink (shouldn't happen)
            stoppedAModifier = true;
        }

        return HaltSeriesLocomotion(phase.Substate, phase.SubstateMod, phase, series, out outBeats) || stoppedAModifier;
    }

    internal static bool SameSign(float a, float b) => (a >= 0f) == (b >= 0f);

    internal static void AlterCyclePace(AnimTrack series, MotionEntry? cyclic, float formerPace, float newPace)
    {
        if (MathF.Abs(formerPace) > Epsilon)
        {
            series.MultiplyCyclicAnimFramerate(newPace / formerPace);
            return;
        }
        if (MathF.Abs(newPace) <= Epsilon)
            series.MultiplyCyclicAnimFramerate(0f);
    }

    internal static void AppendLocomotion(AnimTrack series, MotionEntry? locomotion, float paceMod)
    {
        if (locomotion is null)
            return;

        series.AssignVel(locomotion.Velocity * paceMod);
        series.AssignOmega(locomotion.Omega * paceMod);
        foreach (ClipRef data in locomotion.Clips)
        {
            series.AffixAnim(new ClipRef
            {
                ClipId = data.ClipId,
                LowFrame = data.LowFrame,
                HighFrame = data.HighFrame,
                Framerate = data.Framerate * paceMod,
            });
        }
    }

    internal static void FuseLocomotion(AnimTrack series, MotionEntry? locomotion, float paceMod)
    {
        if (locomotion is not null)
            series.FuseKinetics(locomotion.Velocity * paceMod, locomotion.Omega * paceMod);
    }

    internal static void SubtractLocomotion(AnimTrack series, MotionEntry? locomotion, float paceMod)
    {
        if (locomotion is not null)
            series.SubtractKinetics(locomotion.Velocity * paceMod, locomotion.Omega * paceMod);
    }

    private static int Key(uint styling, uint substate) => (int)((styling << 16) | (substate & SubstateBitmask));

    private static uint AnimTally(MotionEntry? locomotion) => (uint)(locomotion?.Clips.Count ?? 0);

    private uint ConsultStylingDefault(uint styling)
    {
        return _chart.StyleDefaults.TryGetValue((DRWMotionCommand)styling, out DRWMotionCommand substate) ? (uint)substate : 0u;
    }

    private MotionEntry? ConsultCycle(uint styling, uint substate)
    {
        return _chart.Cycles.TryGetValue(Key(styling, substate), out MotionEntry? blob) ? blob : null;
    }

    private MotionEntry? ConsultModifier(uint styling, uint modTag, bool stylingSpecific)
    {
        int tag = stylingSpecific ? (int)((styling << 16) | modTag) : (int)modTag;
        return _chart.Modifiers.TryGetValue(tag, out MotionEntry? blob) ? blob : null;
    }

    // Styled modifier first, else the global one
    private MotionEntry? ConsultModifier(uint styling, uint modTag)
    {
        return ConsultModifier(styling, modTag, stylingSpecific: true) ?? ConsultModifier(styling, modTag, stylingSpecific: false);
    }

    private MotionEntry? ConnectListing(int connectTag, uint toSubstate)
    {
        return _chart.Links.TryGetValue(connectTag, out MotionLinkSet? connect) && connect.Entries.TryGetValue((int)toSubstate, out MotionEntry? listing)
            ? listing
            : null;
    }

    // A style change: exit the current cycle, hop (via the default style if needed), enter the new
    // style's default cycle
    private bool SwitchStyling(uint mark, LocomotionPhase phase, AnimTrack series, float pace, out uint outBeats)
    {
        outBeats = 0;
        uint styling = phase.Style;
        uint substate = phase.Substate;
        uint stylingDefault = ConsultStylingDefault(styling);

        MotionEntry? quitConnect = substate != stylingDefault
            ? GetLink(styling, substate, phase.SubstateMod, stylingDefault, pace)
            : null;

        uint markDefault = ConsultStylingDefault(mark);
        if (!_chart.StyleDefaults.ContainsKey((DRWMotionCommand)mark))
            return false;
        MotionEntry? newCycle = ConsultCycle(mark, markDefault);
        if (newCycle is null)
            return false;

        if ((newCycle.Bitfield & ClearsModifiers) is not 0)
            phase.WipeModifiers();

        MotionEntry? hop1 = GetLink(styling, stylingDefault, phase.SubstateMod, mark, pace);
        MotionEntry? hop2 = null;
        if (hop1 is null && mark != styling)
        {
            // Double hop via the default style
            hop1 = GetLink(styling, stylingDefault, 1f, DefaultStyle, 1f);
            hop2 = GetLink(DefaultStyle, ConsultStylingDefault(DefaultStyle), 1f, mark, 1f);
        }

        series.WipeKinetics();
        series.DropCyclicAnims();
        AppendLocomotion(series, quitConnect, pace);
        AppendLocomotion(series, hop1, pace);
        AppendLocomotion(series, hop2, pace);
        AppendLocomotion(series, newCycle, pace);

        phase.Substate = markDefault;
        phase.Style = mark;
        phase.SubstateMod = pace;
        ReModify(series, phase);

        outBeats = AnimTally(newCycle) + AnimTally(hop2) + AnimTally(hop1) + AnimTally(quitConnect) - 1;
        return true;
    }

    // A substate change within the current style, with the fast re-speed path for the same cycle
    private bool SwitchCycle(uint mark, LocomotionPhase phase, AnimTrack series, float pace, out uint outBeats)
    {
        outBeats = 0;
        uint styling = phase.Style;
        uint substate = phase.Substate;

        uint substateIdent = mark & SubstateBitmask;
        MotionEntry? cyclic = ConsultCycle(styling, substateIdent) ?? ConsultCycle(DefaultStyle, substateIdent);
        if (cyclic is null || !IsAllowed(mark, cyclic, phase))
            return false;

        if (mark == substate && SameSign(pace, phase.SubstateMod) && series.HasAnims())
        {
            AlterCyclePace(series, cyclic, phase.SubstateMod, pace);
            SubtractLocomotion(series, cyclic, phase.SubstateMod);
            FuseLocomotion(series, cyclic, pace);
            phase.SubstateMod = pace;
            return true;
        }

        if ((cyclic.Bitfield & ClearsModifiers) is not 0)
            phase.WipeModifiers();

        MotionEntry? connect = GetLink(styling, substate, phase.SubstateMod, mark, pace);
        MotionEntry? hop2 = null;
        if (connect is null || !SameSign(pace, phase.SubstateMod))
        {
            // Route out through the style default and back in.
            uint stylingDefault = ConsultStylingDefault(styling);
            connect = GetLink(styling, substate, phase.SubstateMod, stylingDefault, 1f);
            hop2 = GetLink(styling, stylingDefault, 1f, mark, pace);
        }

        series.WipeKinetics();
        series.DropCyclicAnims();
        if (hop2 is null)
        {
            float signedPace = phase.SubstateMod == 0f || SameSign(phase.SubstateMod, pace) ? pace : -pace;
            AppendLocomotion(series, connect, signedPace);
        }
        else
        {
            AppendLocomotion(series, connect, phase.SubstateMod);
            AppendLocomotion(series, hop2, pace);
        }
        AppendLocomotion(series, cyclic, pace);

        // Leaving a modifier-class substate keeps it active as a modifier
        // unless we are returning to the style default.
        if (substate != mark && (substate & ModifierBit) is not 0 && ConsultStylingDefault(styling) != mark)
            phase.AppendModifierNoVerify(substate, phase.SubstateMod);

        phase.SubstateMod = pace;
        phase.Substate = mark;
        ReModify(series, phase);

        outBeats = AnimTally(cyclic) + AnimTally(hop2) + AnimTally(connect) - 1;
        return true;
    }

    // A one-shot action: play its link then resume the base cycle, routing via the style default when
    // there is no direct link
    private bool PlayAct(uint mark, LocomotionPhase phase, AnimTrack series, float pace, out uint outBeats)
    {
        outBeats = 0;
        uint styling = phase.Style;
        uint substate = phase.Substate;

        MotionEntry? baseCycle = ConsultCycle(styling, substate & SubstateBitmask);
        if (baseCycle is null)
            return false;

        MotionEntry? straight = GetLink(styling, substate, phase.SubstateMod, mark, pace);
        if (straight is not null)
        {
            phase.AttachAct(mark, pace);
            series.WipeKinetics();
            series.DropCyclicAnims();
            AppendLocomotion(series, straight, pace);
            AppendLocomotion(series, baseCycle, phase.SubstateMod);
            ReModify(series, phase);
            outBeats = AnimTally(straight);
            return true;
        }

        uint stylingDefault = ConsultStylingDefault(styling);
        MotionEntry? outHop = GetLink(styling, substate, phase.SubstateMod, stylingDefault, 1f);
        if (outHop is null)
            return false;
        MotionEntry? actConnect = GetLink(styling, stylingDefault, 1f, mark, pace);
        if (actConnect is null)
            return false;
        MotionEntry? baseCycleAgain = ConsultCycle(styling, substate & SubstateBitmask); // same key, re-fetched as retail does
        if (baseCycleAgain is null)
            return false;
        MotionEntry? returnHop = GetLink(styling, stylingDefault, 1f, substate, phase.SubstateMod);

        phase.AttachAct(mark, pace);
        series.WipeKinetics();
        series.DropCyclicAnims();
        AppendLocomotion(series, outHop, 1f);
        AppendLocomotion(series, actConnect, pace);
        AppendLocomotion(series, returnHop, 1f);
        AppendLocomotion(series, baseCycleAgain, phase.SubstateMod);
        ReModify(series, phase);

        outBeats = AnimTally(outHop) + AnimTally(actConnect) + AnimTally(returnHop);
        return true;
    }

    // Layers a modifier's physics onto the current cycle, restarting it if it was already active
    private bool ImposeModifier(uint mark, LocomotionPhase phase, AnimTrack series, float pace)
    {
        MotionEntry? baseCycle = ConsultCycle(phase.Style, phase.Substate & SubstateBitmask);
        if (baseCycle is null || (baseCycle.Bitfield & ClearsModifiers) is not 0)
            return false;

        MotionEntry? modifier = ConsultModifier(phase.Style, mark & SubstateBitmask);
        if (modifier is null)
            return false;

        bool added = phase.AppendModifier(mark, pace);
        if (!added)
        {
            HaltSeriesLocomotion(mark, 1f, phase, series, out _);
            added = phase.AppendModifier(mark, pace);
        }
        if (!added)
            return false;

        FuseLocomotion(series, modifier, pace);
        return true;
    }
}
