using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class TargetKeeper(IKineticObjHost host)
{
    public const double ThrottleSecs = 0.5;

    public const double StalenessSecs = 10.0;

    private readonly IKineticObjHost _hub = host ?? throw new ArgumentNullException(nameof(host));

    private TargetFacts? _mark;
    private IKineticObjHost? _markHub;   // exact App incarnation token
    private Dictionary<uint, WatcherFacts>? _voyeurs;
    private double _previousSweep;

    public TargetFacts? MarkFacts => _mark;

    public IReadOnlyDictionary<uint, WatcherFacts>? VoyeurChart => _voyeurs;

    public double FetchMarkQuantum() => _mark?.Quantum ?? 0.0;

    public IKineticObjHost? FetchRelationshipObjective(uint objectIdent) =>
        _mark is { } facts && facts.ObjectId == objectIdent ? _markHub : null;

    public void AssignObjective(uint ctxIdent, uint objectIdent, float radius, double quantum)
    {
        WipeObjective();

        if (objectIdent is 0)
        {
            _hub.HandleUpdateTarget(new TargetFacts(
                ObjectId: 0, Status: TargetPhase.TimedOut,
                TargetPosition: default, InterpolatedPosition: default,
                ContextId: ctxIdent));
            return;
        }

        _mark = new TargetFacts(
            ObjectId: objectIdent, Status: TargetPhase.Undefined,
            TargetPosition: default, InterpolatedPosition: default,
            ContextId: ctxIdent, Radius: radius, Quantum: quantum,
            LastUpdateTime: _hub.CurTime);

        _markHub = _hub.GetObjectA(objectIdent);
        _markHub?.AppendVoyeur(_hub, radius, quantum);
    }

    public void AssignMarkQuantum(double quantum)
    {
        if (_mark is not { } facts)
            return;

        _mark = facts with { Quantum = quantum };
        _markHub ??= _hub.GetObjectA(facts.ObjectId);
        _markHub?.AppendVoyeur(_hub, facts.Radius, quantum);
    }

    public void WipeObjective()
    {
        if (_mark is not { } facts)
            return;

        (_markHub ?? _hub.GetObjectA(facts.ObjectId))?.DropVoyeur(_hub.Id, _hub);
        _mark = null;
        _markHub = null;
    }

    public void TakeRefresh(TargetFacts refresh, IKineticObjHost sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (_mark is not { } facts || facts.ObjectId != refresh.ObjectId || !ReferenceEquals(_markHub, sender))
            return;

        Vector3 bearing = refresh.InterpolatedPosition.Frame.Origin - _hub.Position.Frame.Origin;
        if (ApproachMath.StandardizeVerifySmall(ref bearing))
            bearing = Vector3.UnitZ;

        TargetFacts merged = facts with
        {
            Radius = refresh.Radius,
            Quantum = refresh.Quantum,
            TargetPosition = refresh.TargetPosition,
            InterpolatedPosition = refresh.InterpolatedPosition,
            Velocity = refresh.Velocity,
            Status = refresh.Status,
            InterpolatedHeading = bearing,
            LastUpdateTime = _hub.CurTime,
        };
        _mark = merged;
        _hub.HandleUpdateTarget(merged);

        if (refresh.Status == TargetPhase.ExitWorld)
            WipeObjective();
    }

    public void AttachVoyeur(IKineticObjHost watcher, float radius, double quantum)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        uint watcherIdent = watcher.Id;
        _voyeurs ??= new Dictionary<uint, WatcherFacts>();

        if (_voyeurs.TryGetValue(watcherIdent, out WatcherFacts? recognized))
        {
            if (ReferenceEquals(recognized.WatcherHub, watcher))
            {
                recognized.Radius = radius;
                recognized.Quantum = quantum;
                return;
            }

            _voyeurs.Remove(watcherIdent);
        }

        WatcherFacts voyeur = new WatcherFacts(watcherIdent, radius, quantum, watcher);
        _voyeurs[watcherIdent] = voyeur;
        TransmitVoyeurRefresh(voyeur, _hub.Position, TargetPhase.Ok);
    }

    public bool DeleteVoyeur(uint watcherIdent, IKineticObjHost anticipatedWatcher)
    {
        ArgumentNullException.ThrowIfNull(anticipatedWatcher);
        if (_voyeurs is null
            || !_voyeurs.TryGetValue(watcherIdent, out WatcherFacts? recognized)
            || !ReferenceEquals(recognized.WatcherHub, anticipatedWatcher))

            return false;
        return _voyeurs.Remove(watcherIdent);
    }

    public void ProcessTargetting()
    {
        if (_hub.PhysicsTimerTime - _previousSweep < ThrottleSecs)
            return;

        if (_mark is { Status: TargetPhase.Undefined } waiting && waiting.LastUpdateTime + StalenessSecs < _hub.CurTime)
        {
            TargetFacts timedOut = waiting with { Status = TargetPhase.TimedOut };
            _mark = timedOut;
            _hub.HandleUpdateTarget(timedOut);
        }

        foreach (WatcherFacts voyeur in VoyeursCapture())
            VerifyAndRefreshVoyeur(voyeur);

        _previousSweep = _hub.PhysicsTimerTime;
    }

    public void VerifyAndRefreshVoyeur(WatcherFacts voyeur)
    {
        Locus ahead = FetchInterpolatedLocus(voyeur.Quantum);
        float drift = Vector3.Distance(ahead.Frame.Origin, voyeur.PreviousSentLocus.Frame.Origin);
        if (drift > voyeur.Radius)
            TransmitVoyeurRefresh(voyeur, ahead, TargetPhase.Ok);
    }

    public Locus FetchInterpolatedLocus(double quantum)
    {
        Locus instant = _hub.Position;
        Vector3 origin = instant.Frame.Origin + _hub.Velocity * (float)quantum;
        return new Locus(instant.ObjCellId, origin, instant.Frame.Orientation);
    }

    public void TransmitVoyeurRefresh(WatcherFacts voyeur, Locus spot, TargetPhase condition)
    {
        voyeur.PreviousSentLocus = spot;
        voyeur.WatcherHub.TakeMarkRefresh(
            new TargetFacts(
                ObjectId: _hub.Id,
                Status: condition,
                TargetPosition: _hub.Position,
                InterpolatedPosition: spot,   // the extrapolated position
                ContextId: 0,
                Radius: voyeur.Radius,
                Quantum: voyeur.Quantum,
                Velocity: _hub.Velocity),
            _hub);
    }

    public void AlertVoyeurOfSignal(TargetPhase condition)
    {
        foreach (WatcherFacts voyeur in VoyeursCapture())
            TransmitVoyeurRefresh(voyeur, _hub.Position, condition);
    }

    public void AlertVoyeurOfSignalAndWipe(TargetPhase condition)
    {
        if (_voyeurs is null)
            return;
        AlertVoyeurOfSignal(condition);
        _voyeurs.Clear();
    }

    // A copy of the voyeur list, so callbacks may add or remove voyeurs while we walk it
    private List<WatcherFacts> VoyeursCapture() =>
        _voyeurs is null ? [] : new List<WatcherFacts>(_voyeurs.Values);
}
