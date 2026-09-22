using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Effects;

public sealed class OnlineActorLightDriver : IDisposable
{
    private readonly OnlineActorCore _onlineActors;
    private readonly ActorEffectPoseRegistry _postures;
    private readonly LightingHookTap _illumination;
    private readonly Func<uint, RigSpec?> _pullRig;
    private readonly HashSet<SimActorKey> _trackedOwners = [];
    private readonly HashSet<SimActorKey> _presentOwners = [];

    public OnlineActorLightDriver(
        OnlineActorCore liveEntities,
        ActorEffectPoseRegistry poses,
        LightingHookTap lighting,
        Func<uint, RigSpec?> loadSetup)
    {
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _postures = poses ?? throw new ArgumentNullException(nameof(poses));
        _illumination = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _pullRig = loadSetup ?? throw new ArgumentNullException(nameof(loadSetup));
        _illumination.OwnerLightingChanged += OnHolderIlluminationAltered;
        _onlineActors.ProjectionVisibilityChanged += OnProjVisAltered;
    }

    public bool Register(uint srvOid)
    {
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            || !capture.IsSpatiallyProjected
            || !capture.IsSpatiallyVisible)

            return false;

        SimActorKey tag = DemandProjTag(capture);
        _trackedOwners.Add(tag);
        _presentOwners.Add(tag);
        _illumination.WithdrawHolder(actor.Id, forgetPhase: false);
        _illumination.BootstrapHolderIllumination(
            actor.Id,
            (capture.FinalKineticsPhase & KineticStateFlags.Lighting) != 0);
        if ((actor.SrcGfxObjRefOrRigIdent & 0xFF000000u) != 0x02000000u)
            return false;

        if (capture.ProjSort is OnlineActorMirrorKind.World && !_postures.RefreshTrunk(actor))
            _postures.BroadcastTriMeshRefs(actor);

        RigSpec? rig = _pullRig(actor.SrcGfxObjRefOrRigIdent);
        if (rig is null
            || rig.Lamps.Count is 0
            || !_illumination.IsHolderIlluminationTurnedOn(actor.Id))
            return false;

        bool isDynamic = (capture.FinalKineticsPhase & KineticStateFlags.Static) == 0;
        var fetched = LightInfoReader.Load(
            rig,
            actor.Id,
            actor.Position,
            actor.Rotation,
            isDynamic,
            actor.ParentCellId ?? 0u,
            tracksHolderPosture: true);
        for (int idx = 0; idx < fetched.Count; ++idx)
            _illumination.EnrollPossessedLamp(fetched[idx]);
        return fetched.Count > 0;
    }

    public void Unregister(uint holderOwnIdent)
    {
        if (_onlineActors.TryFetchCaptureByOwnActorIdent(
                holderOwnIdent,
                out OnlineActorRecord capture)
            && capture.ProjTag is { } tag)

            _presentOwners.Remove(tag);
        _illumination.WithdrawHolder(holderOwnIdent, forgetPhase: false);
    }

    public void OnPhaseAltered(uint srvOid)
    {
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor)

            return;
        _illumination.AssignHolderIllumination(
            actor.Id,
            (capture.FinalKineticsPhase & KineticStateFlags.Lighting) != 0);
    }

    public void Forget(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.ProjTag is not { } tag)
            return;

        _presentOwners.Remove(tag);
        _trackedOwners.Remove(tag);
        _illumination.WithdrawHolder(tag.LocalEntityId);
    }

    public void Refresh() => _illumination.RenewAffixedLamps();

    internal int FollowedHolderTally => _trackedOwners.Count;
    internal int PresentedHolderTally => _presentOwners.Count;

    public void OnAffixedPosturePrimed(uint srvOid)
    {
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.ProjSort is not OnlineActorMirrorKind.Attached
            || capture.WorldEntity is not { } actor
            || capture.ProjTag is not { } tag
            || _presentOwners.Contains(tag))

            return;
        Register(srvOid);
    }

    public void Dispose()
    {
        _illumination.OwnerLightingChanged -= OnHolderIlluminationAltered;
        _onlineActors.ProjectionVisibilityChanged -= OnProjVisAltered;
        foreach (SimActorKey tag in _trackedOwners)
            _illumination.WithdrawHolder(tag.LocalEntityId);
        _trackedOwners.Clear();
        _presentOwners.Clear();
    }

    private void OnHolderIlluminationAltered(uint holderOwnIdent, bool turnedOn)
    {
        if (!_onlineActors.TryFetchCaptureByOwnActorIdent(
                holderOwnIdent,
                out OnlineActorRecord capture)
            || capture.ProjTag is not { } tag
            || !_presentOwners.Contains(tag))

            return;

        if (!turnedOn)
        {
            _illumination.WithdrawHolder(holderOwnIdent, forgetPhase: false);
            return;
        }

        Register(capture.ServerOid);
    }

    private void OnProjVisAltered(OnlineActorRecord capture, bool shown)
    {
        if (capture.WorldEntity is not { } actor)
            return;
        if (capture.ProjSort is OnlineActorMirrorKind.Attached)
        {
            if (!shown)
                Unregister(actor.Id);
            return;
        }
        if (shown)
            Register(capture.ServerOid);
        else
            Unregister(actor.Id);
    }

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} " +
            "has no exact projection key");
    }
}
