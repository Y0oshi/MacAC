namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class PositionKeeper(IKineticObjHost host)
{
    private readonly IKineticObjHost _hub = host ?? throw new ArgumentNullException(nameof(host));

    public StickyKeeper? Sticky { get; private set; }
    public TetherKeeper? Constraint { get; private set; }

    public void StickTo(uint objectIdent, float radius, float height)
    {
        Sticky ??= new StickyKeeper(_hub);
        Sticky.StickTo(objectIdent, radius, height);
    }

    public void UnStick() => Sticky?.UnStick();

    public uint FetchStickyObjectIdent() => Sticky?.TargetId ?? 0u;

    public void ConstrainTo(Locus mooring, float beginGap, float upperGap)
    {
        Constraint ??= new TetherKeeper(_hub);
        Constraint.ConstrainTo(mooring, beginGap, upperGap);
    }

    public void UnConstrain() => Constraint?.UnConstrain();

    public bool IsFullyConstrained() => Constraint?.IsFullyConstrained() ?? false;

    public void ServiceRefreshMark(TargetFacts details) => Sticky?.ProcessUpdateTarget(details);

    public void AdjustOffset(MotionDeltaPose shift, double quantum)
    {
        Sticky?.AdjustOffset(shift, quantum);
        Constraint?.AdjustOffset(shift, quantum);
    }

    public void UseMoment() => Sticky?.UseTime();
}
