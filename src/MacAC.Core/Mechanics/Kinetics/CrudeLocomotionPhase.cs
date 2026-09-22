using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

public enum HeldKey : uint
{
    Invalid = 0x0,
    None = 0x1,
    Run = 0x2,
}

public readonly record struct RawMotionVerb(
    ushort Command,
    ushort Stamp,
    bool Autonomous,
    float Speed = 1f);

public sealed class CrudeLocomotionPhase
{
    private const uint NonFightingStyling = 0x8000003Du;
    private const uint PrimedDirective = 0x41000003u;
    private const uint IgnoredAheadDirective = 0x44000007u;
    private const uint TurnRight = 0x6500000Du;
    private const uint PivotLeft = 0x6500000Eu;
    private const uint FlankHopRight = 0x6500000Fu;
    private const uint FlankHopLeft = 0x65000010u;
    private const uint SubPhaseBit = 0x40000000u;
    private const uint StylingBit = 0x80000000u;
    private const uint ActBit = 0x10000000u;

    private enum Lane
    {
        Ignore,
        Turn,
        Sidestep,
        Forward,
        Style,
        Action,
    }

    private readonly List<RawMotionVerb> _actions = [];

    public static readonly CrudeLocomotionPhase Default = new();

    public CrudeLocomotionPhase()
    {
    }

    public CrudeLocomotionPhase(CrudeLocomotionPhase another)
    {
        ArgumentNullException.ThrowIfNull(another);
        CurrentHoldKey = another.CurrentHoldKey;
        CurrentStyle = another.CurrentStyle;
        ForwardCommand = another.ForwardCommand;
        ForwardHoldKey = another.ForwardHoldKey;
        ForwardSpeed = another.ForwardSpeed;
        SidestepDirective = another.SidestepDirective;
        SidestepGripTag = another.SidestepGripTag;
        SidestepPace = another.SidestepPace;
        TurnDirective = another.TurnDirective;
        PivotGripTag = another.PivotGripTag;
        TurnPace = another.TurnPace;
        _actions.AddRange(another._actions);
    }

    public HeldKey CurrentHoldKey { get; set; } = HeldKey.None;
    public uint CurrentStyle { get; set; } = NonFightingStyling;
    public uint ForwardCommand { get; set; } = PrimedDirective;
    public HeldKey ForwardHoldKey { get; set; } = HeldKey.Invalid;
    public float ForwardSpeed { get; set; } = 1.0f;
    public uint SidestepDirective { get; set; }
    public HeldKey SidestepGripTag { get; set; } = HeldKey.Invalid;
    public float SidestepPace { get; set; } = 1.0f;
    public uint TurnDirective { get; set; }
    public HeldKey PivotGripTag { get; set; } = HeldKey.Invalid;
    public float TurnPace { get; set; } = 1.0f;

    public IReadOnlyList<RawMotionVerb> Actions
    {
        get => _actions;
        set
        {
            _actions.Clear();
            _actions.AddRange(value);
        }
    }

    public void AppendAction(uint locomotion, float pace, uint actStamp, bool autonomous)
    {
        _actions.Add(new RawMotionVerb((ushort)locomotion, (ushort)actStamp, autonomous, pace));
    }

    public uint DeleteAct()
    {
        if (_actions.Count is 0)
            return 0;
        uint front = _actions[0].Command;
        _actions.RemoveAt(0);
        return front;
    }

    public void ImposeMotion(uint locomotion, LocomotionParams p)
    {
        switch (LaneOf(locomotion))
        {
            case Lane.Turn:
                TurnDirective = locomotion;
                PivotGripTag = GripFor(p);
                TurnPace = p.Speed;
                break;
            case Lane.Sidestep:
                SidestepDirective = locomotion;
                SidestepGripTag = GripFor(p);
                SidestepPace = p.Speed;
                break;
            case Lane.Forward when locomotion != IgnoredAheadDirective:
                ForwardCommand = locomotion;
                ForwardHoldKey = GripFor(p);
                ForwardSpeed = p.Speed;
                break;
            case Lane.Style:
                if (CurrentStyle != locomotion)
                {
                    ForwardCommand = PrimedDirective;
                    CurrentStyle = locomotion;
                }
                break;
            case Lane.Action:
                AppendAction(locomotion, p.Speed, p.ActStamp, p.Autonomous);
                break;
        }
    }

    public void DeleteLocomotion(uint locomotion)
    {
        switch (LaneOf(locomotion))
        {
            case Lane.Turn:
                TurnDirective = 0;
                break;
            case Lane.Sidestep:
                SidestepDirective = 0;
                break;
            case Lane.Forward:
                if (locomotion == ForwardCommand)
                {
                    ForwardCommand = PrimedDirective;
                    ForwardSpeed = 1f;
                }
                break;
            case Lane.Style:
                if (locomotion == CurrentStyle)
                    CurrentStyle = NonFightingStyling;
                break;
        }
    }

    // Which lane a motion command drives, following the retail bit tests
    private static Lane LaneOf(uint locomotion)
    {
        switch (locomotion)
        {
            case TurnRight or PivotLeft:
                return Lane.Turn;
            case FlankHopRight or FlankHopLeft:
                return Lane.Sidestep;
        }

        if ((locomotion & SubPhaseBit) is not 0)
            return Lane.Forward;
        if (locomotion >= StylingBit)
            return Lane.Style;
        return (locomotion & ActBit) is not 0 ? Lane.Action : Lane.Ignore;
    }

    private static HeldKey GripFor(LocomotionParams p) => p.SetHoldKey ? HeldKey.Invalid : p.GripTagToEnact;
}
