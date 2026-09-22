using MacAC.Mechanics.Kinetics;
using MacAC.Wire.Messages;

namespace MacAC.Client.Kinetics;

internal static class InboundInterpretedMotionMint
{
    private const uint NonFightingStyling = 0x8000003Du;
    private const uint Ready = 0x41000003u;

    public static InboundDecodedState Create(
        in ObjectCreation.RemoteMotionState wire,
        uint backupAheadClass = 0x40000000u)
    {
        InboundDecodedState outcome = new InboundDecodedState
        {
            CurrentStyling = wire.Stance is not 0
                ? 0x80000000u | wire.Stance
                : NonFightingStyling,
            ForwardCommand = LocateAhead(wire.ForwardCommand, backupAheadClass),
            ForwardSpeed = wire.ForwardSpeed ?? 1f,
            FlankStepCommand = LocateAxis(wire.SideStepCommand, 0x65000000u),
            FlankStepSpeed = wire.SideStepSpeed ?? 1f,
            PivotCommand = LocateAxis(wire.TurnCommand, 0x65000000u),
            PivotSpeed = wire.TurnSpeed ?? 1f,
        };

        if (wire.Commands is { Count: > 0 } directives)
        {
            var acts = new List<InboundMotionVerb>(directives.Count);
            foreach (var gear in directives)
            {
                uint directive = MotionCommandLookup.ReconstructWholeCommand(gear.Command);
                if (directive is 0)
                    directive = 0x10000000u | gear.Command;

                acts.Add(new InboundMotionVerb(
                    directive,
                    Stamp: gear.PackedSequence & 0x7FFF,
                    Autonomous: (gear.PackedSequence & 0x8000) is not 0,
                    Speed: gear.Speed));
            }

            outcome.Actions = acts;
        }

        return outcome;
    }

    private static uint LocateAhead(ushort? wireDirective, uint backupClass)
    {
        if (wireDirective is not { } directive || directive is 0)
            return Ready;

        uint settled = MotionCommandLookup.ReconstructWholeCommand(directive);
        if (settled is not 0)
            return settled;

        uint directiveClass = backupClass & 0xFF000000u;
        return (directiveClass is not 0 ? directiveClass : 0x40000000u) | directive;
    }

    private static uint LocateAxis(ushort? wireDirective, uint backupClass)
    {
        if (wireDirective is not { } directive || directive is 0)
            return 0;

        uint settled = MotionCommandLookup.ReconstructWholeCommand(directive);
        return settled is not 0 ? settled : backupClass | directive;
    }
}
