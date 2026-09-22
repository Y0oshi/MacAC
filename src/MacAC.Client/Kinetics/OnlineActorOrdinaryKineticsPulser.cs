using System.Collections.Immutable;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Kinetics;

internal sealed class OnlineActorOrdinaryKineticsPulser(
    SimKineticsLedger physics,
    Func<uint, RealmActor, (float Radius, float Height)> getSetupCylinder,
    Func<uint, RealmActor,
                (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)>
            getSetupMoverShape,
    Func<uint, MoverState>? fetchCarrierPvpPhase = null)
{
    private readonly SimPlainKineticsStepper _runtime = new SimPlainKineticsStepper(
            physics ?? throw new ArgumentNullException(nameof(physics)));
    private readonly Func<uint, RealmActor, (float Radius, float Height)>
        _fetchRigCylinder = getSetupCylinder
            ?? throw new ArgumentNullException(nameof(getSetupCylinder));
    private readonly Func<uint, RealmActor,
            (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)>
        _fetchRigCarrierForm = getSetupMoverShape
            ?? throw new ArgumentNullException(nameof(getSetupMoverShape));
    private readonly Func<uint, MoverState> _fetchCarrierPvpPhase = fetchCarrierPvpPhase ?? (static _ => MoverState.None);

    public bool Tick(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        Pose trunkCycle,
        float objectScaling,
        float quantum,
        int onlineMiddleX,
        int onlineMiddleY,
        ulong objectTimerEpoch,
        AnimSequencer? scheduler,
        Action<uint, AnimSequencer> grabAnimTaps)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(trunkCycle);
        ArgumentNullException.ThrowIfNull(grabAnimTaps);
        if (capture.KineticBody is not { } corpus)
            return false;
        var (radius, height) = _fetchRigCylinder(capture.ServerOid, actor);
        var form = _fetchRigCarrierForm(capture.ServerOid, actor);
        bool ExternalHolderValid() =>
            IsLatest(
                core,
                capture,
                actor,
                corpus,
                objectTimerEpoch);

        return !_runtime.TryCommence(
                capture.Canonical,
                trunkCycle,
                objectScaling,
                quantum,
                radius,
                height,
                objectTimerEpoch,
                scheduler,
                grabAnimTaps,
                ExternalHolderValid,
                out SimPlainKineticsCommit seal,
                orbRoster: form.Spheres,
                orbScaling: form.Scale,
                hopUpHeight: form.StepUpHeight,
                hopDownHeight: form.StepDownHeight,
                carrierPvpPhase: _fetchCarrierPvpPhase(capture.ServerOid))
            ? false
            : _runtime.Complete(
            seal,
            onlineMiddleX,
            onlineMiddleY,
            snapshot =>
            {
                if (!ExternalHolderValid())
                    return false;

                actor.SetPosition(snapshot.Position);
                actor.Rotation = snapshot.Orientation;
                actor.ParentCellId = snapshot.FullCellId;
                return ExternalHolderValid();
            });
    }

    private static bool IsLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        KineticBody corpus,
        ulong objectTimerEpoch) =>
        core.IsLatestSpatialTrunkObject(capture)
        && capture.ObjectTimerEpoch == objectTimerEpoch
        && ReferenceEquals(capture.WorldEntity, actor)
        && ReferenceEquals(capture.KineticBody, corpus)
        && capture.RemoteMotionRuntime is null
        && capture.ProjectileRuntime is null;
}
