using MacAC.Mechanics.Kinetics;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class MotionStatePacker
{
    private const uint LatestGripTagBit = 0x001u;
    private const uint LatestStylingBit = 0x002u;
    private const uint AheadDirectiveBit = 0x004u;
    private const uint AheadGripTagBit = 0x008u;
    private const uint AheadPaceBit = 0x010u;
    private const uint SidestepDirectiveBit = 0x020u;
    private const uint SidestepGripTagBit = 0x040u;
    private const uint SidestepPaceBit = 0x080u;
    private const uint PivotDirectiveBit = 0x100u;
    private const uint PivotGripTagBit = 0x200u;
    private const uint PivotPaceBit = 0x400u;
    private const int ActTallyShift = 11;

    public static void Pack(DatagramScribe scribe, CrudeLocomotionPhase phase)
    {
        var d = CrudeLocomotionPhase.Default;
        uint flagSet = (uint)(phase.Actions.Count << ActTallyShift)
            | Bit(phase.CurrentHoldKey != d.CurrentHoldKey, LatestGripTagBit)
            | Bit(phase.CurrentStyle != d.CurrentStyle, LatestStylingBit)
            | Bit(phase.ForwardCommand != d.ForwardCommand, AheadDirectiveBit)
            | Bit(phase.ForwardHoldKey != d.ForwardHoldKey, AheadGripTagBit)
            | Bit(phase.ForwardSpeed != d.ForwardSpeed, AheadPaceBit)
            | Bit(phase.SidestepDirective != d.SidestepDirective, SidestepDirectiveBit)
            | Bit(phase.SidestepGripTag != d.SidestepGripTag, SidestepGripTagBit)
            | Bit(phase.SidestepPace != d.SidestepPace, SidestepPaceBit)
            | Bit(phase.TurnDirective != d.TurnDirective, PivotDirectiveBit)
            | Bit(phase.PivotGripTag != d.PivotGripTag, PivotGripTagBit)
            | Bit(phase.TurnPace != d.TurnPace, PivotPaceBit);

        scribe.EmitUInt32(flagSet);
        PackRest(flagSet, phase, scribe);
    }

    private static void PackRest(uint flagSet, CrudeLocomotionPhase phase, DatagramScribe scribe)
    {
        if ((flagSet & LatestGripTagBit) is not 0) scribe.EmitUInt32((uint)phase.CurrentHoldKey);
        if ((flagSet & LatestStylingBit) is not 0) scribe.EmitUInt32(phase.CurrentStyle);
        PackTail(flagSet, phase, scribe);
    }

    private static void PackTail(uint flagSet, CrudeLocomotionPhase phase, DatagramScribe scribe)
    {
        if ((flagSet & AheadDirectiveBit) is not 0) scribe.EmitUInt32(phase.ForwardCommand);
        if ((flagSet & AheadGripTagBit) is not 0) scribe.EmitUInt32((uint)phase.ForwardHoldKey);
        if ((flagSet & AheadPaceBit) is not 0) scribe.EmitFloat(phase.ForwardSpeed);
        PackCoda(flagSet, phase, scribe);
    }

    private static void PackCoda(uint flagSet, CrudeLocomotionPhase phase, DatagramScribe scribe)
    {
        if ((flagSet & SidestepDirectiveBit) is not 0) scribe.EmitUInt32(phase.SidestepDirective);
        if ((flagSet & SidestepGripTagBit) is not 0) scribe.EmitUInt32((uint)phase.SidestepGripTag);
        if ((flagSet & SidestepPaceBit) is not 0) scribe.EmitFloat(phase.SidestepPace);
        if ((flagSet & PivotDirectiveBit) is not 0) scribe.EmitUInt32(phase.TurnDirective);
        PackCoda2(flagSet, scribe, phase);
    }

    private static void PackCoda2(uint flagSet, DatagramScribe scribe, CrudeLocomotionPhase phase)
    {
        if ((flagSet & PivotGripTagBit) is not 0) scribe.EmitUInt32((uint)phase.PivotGripTag);
        if ((flagSet & PivotPaceBit) is not 0) scribe.EmitFloat(phase.TurnPace);
        foreach (var act in phase.Actions)
        {
            scribe.EmitUInt16(act.Command);
            scribe.EmitUInt16((ushort)((act.Stamp & 0x7FFF) | (act.Autonomous ? 0x8000 : 0)));
        }
    }

    internal static GameActionScribe LocomotionPhase(this GameActionScribe scribe, CrudeLocomotionPhase phase)
    {
        DatagramScribe temp = new DatagramScribe(64);
        Pack(temp, phase);
        return scribe.Bytes(temp.AsSpan());
    }

    private static uint Bit(bool set, uint bit) => set ? bit : 0u;
}
