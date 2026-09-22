using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

public struct DecodedMotionState
{
    private const uint NonFightingStyling = 0x8000003Du;

    public uint ForwardCommand;
    public float ForwardSpeed;
    public uint FlankHopDirective;
    public float FlankHopPace;
    public uint PivotDirective;
    public float PivotPace;
    public uint LatestStyle;

    private List<RawMotionVerb>? _actions;

    public readonly IReadOnlyList<RawMotionVerb> Actions => (IReadOnlyList<RawMotionVerb>?)_actions ?? [];

    public static DecodedMotionState Default()
    {
        return new()
        {
            ForwardCommand = LocomotionDirective.Ready,
            ForwardSpeed = 1.0f,
            FlankHopDirective = 0,
            FlankHopPace = 1.0f,
            PivotDirective = 0,
            PivotPace = 1.0f,
            LatestStyle = NonFightingStyling,
        };
    }

    public void AppendAct(uint locomotion, float pace, uint actStamp, bool autonomous)
    {
        _actions ??= [];
        _actions.Add(new RawMotionVerb((ushort)locomotion, (ushort)actStamp, autonomous, pace));
    }

    public uint DropAct()
    {
        if (_actions is null || _actions.Count is 0)
            return 0;
        uint front = _actions[0].Command;
        _actions.RemoveAt(0);
        return front;
    }

    public readonly uint FetchCountActs() => (uint)(_actions?.Count ?? 0);

    public void ImposeLocomotion(uint locomotion, LocomotionParams p)
    {
        switch (locomotion)
        {
            case LocomotionDirective.TurnRight:
                PivotDirective = locomotion;
                PivotPace = p.Speed;
                return;
            case LocomotionDirective.FlankHopRight:
                FlankHopDirective = locomotion;
                FlankHopPace = p.Speed;
                return;
        }

        if ((locomotion & 0x40000000u) is not 0)
        {
            ForwardCommand = locomotion;
            ForwardSpeed = p.Speed;
        }
        else if (locomotion >= 0x80000000u)   // a style
        {
            ForwardCommand = LocomotionDirective.Ready;
            LatestStyle = locomotion;
        }
        else if ((locomotion & 0x10000000u) is not 0)
        {
            AppendAct(locomotion, p.Speed, p.ActStamp, p.Autonomous);
        }
    }

    public void DropLocomotion(uint locomotion)
    {
        switch (locomotion)
        {
            case LocomotionDirective.TurnRight:
                PivotDirective = 0;
                return;
            case LocomotionDirective.FlankHopRight:
                FlankHopDirective = 0;
                return;
        }

        if ((locomotion & 0x40000000u) is not 0)
        {
            if (locomotion == ForwardCommand)
            {
                ForwardCommand = LocomotionDirective.Ready;
                ForwardSpeed = 1f;
            }
        }
        else if (locomotion >= 0x80000000u && locomotion == LatestStyle)
        {
            LatestStyle = NonFightingStyling;
        }
    }
}
