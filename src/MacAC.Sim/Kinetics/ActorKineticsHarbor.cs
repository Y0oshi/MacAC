using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Kinetics;

public sealed class ActorKineticsHarbor : IKineticObjHost
{
    // The callbacks that describe the entity to the keepers
    private readonly record struct Seams(
        Func<Locus> Position,
        Func<Vector3> Velocity,
        Func<float> Radius,
        Func<bool> InContact,
        Func<float?> MinterpMaxSpeed,
        Func<double> CurTime,
        Func<double> PhysicsTimerTime,
        Func<uint, IKineticObjHost?> GetObjectA,
        Action<TargetFacts> HandleUpdateTarget,
        Action InterruptCurrentMovement)
    {
        public Seams Checked()
        {
            ArgumentNullException.ThrowIfNull(Position);
            ArgumentNullException.ThrowIfNull(Velocity);
            ArgumentNullException.ThrowIfNull(Radius);
            ArgumentNullException.ThrowIfNull(InContact);
            ArgumentNullException.ThrowIfNull(MinterpMaxSpeed);
            ArgumentNullException.ThrowIfNull(CurTime);
            ArgumentNullException.ThrowIfNull(PhysicsTimerTime);
            ArgumentNullException.ThrowIfNull(GetObjectA);
            ArgumentNullException.ThrowIfNull(HandleUpdateTarget);
            ArgumentNullException.ThrowIfNull(InterruptCurrentMovement);
            return this;
        }
    }

    private Seams _seams;
    private readonly TargetKeeper _targets;

    public ActorKineticsHarbor(
        uint ident,
        Func<Locus> fetchLocus,
        Func<Vector3> fetchVel,
        Func<float> fetchRadius,
        Func<bool> inLink,
        Func<float?> minterpUpperPace,
        Func<double> curMoment,
        Func<double> kineticsTickerMoment,
        Func<uint, IKineticObjHost?> fetchObjectA,
        Action<TargetFacts> hndRefreshMark,
        Action interruptLatestTravel)
    {
        Id = ident;
        _seams = new Seams(fetchLocus, fetchVel, fetchRadius, inLink, minterpUpperPace, curMoment, kineticsTickerMoment, fetchObjectA, hndRefreshMark, interruptLatestTravel).Checked();
        _targets = new TargetKeeper(this);
        LocusKeeper = new PositionKeeper(this);
    }

    public IKineticObjHost? GetObjectA(uint ident) => _seams.GetObjectA(ident);

    public uint Id { get; }
    public Locus Position => _seams.Position();
    public Vector3 Velocity => _seams.Velocity();
    public float Radius => _seams.Radius();
    public bool InContact => _seams.InContact();
    public float? MinterpMaxSpeed => _seams.MinterpMaxSpeed();
    public double CurTime => _seams.CurTime();
    public double PhysicsTimerTime => _seams.PhysicsTimerTime();

    public TargetKeeper MarkKeeper => _targets;

    public PositionKeeper LocusKeeper { get; }

    public IKineticObjHost? FetchRelationshipMark(uint objectIdent) => _targets.FetchRelationshipObjective(objectIdent);

    public void HandleUpdateTarget(TargetFacts details)
    {
        _seams.HandleUpdateTarget(details);
        LocusKeeper.ServiceRefreshMark(details);
    }

    public void InterruptCurrentMovement() => _seams.InterruptCurrentMovement();

    public void AssignMark(uint ctxIdent, uint objectIdent, float radius, double quantum) => _targets.AssignObjective(ctxIdent, objectIdent, radius, quantum);

    public void WipeMark() => _targets.WipeObjective();

    public void TakeMarkRefresh(TargetFacts details, IKineticObjHost sender) => _targets.TakeRefresh(details, sender);

    public void AppendVoyeur(IKineticObjHost watcher, float radius, double quantum) => _targets.AttachVoyeur(watcher, radius, quantum);

    public void DropVoyeur(uint watcherIdent, IKineticObjHost anticipatedWatcher) => _targets.DeleteVoyeur(watcherIdent, anticipatedWatcher);

    public void ServiceTargetting() => _targets.ProcessTargetting();

    public void AlertQuitRealm() => _targets.AlertVoyeurOfSignal(TargetPhase.ExitWorld);

    public void AlertConcealed() => _targets.AlertVoyeurOfSignalAndWipe(TargetPhase.ExitWorld);

    public void AlertTeleported()
    {
        _targets.WipeObjective();
        _targets.AlertVoyeurOfSignal(TargetPhase.Teleported);
    }

    // Takes another host's accessors for the same entity, keeping this host's keepers
    internal void RebindFrom(ActorKineticsHarbor configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Id != Id)
            throw new ArgumentException("A physics-host configuration must match the existing host GUID", nameof(configuration));
        _seams = configuration._seams.Checked();
    }
}
