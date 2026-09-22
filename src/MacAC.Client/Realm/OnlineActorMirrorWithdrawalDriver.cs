using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.PluginHosting;

namespace MacAC.Client.Realm;

internal sealed class OnlineActorMirrorWithdrawalDriver(
    OnlineActorCore runtime,
    MissileDriver projectiles,
    RealmPlayPhase worldState,
    RealmSignals worldEvents,
    ProxyRegistry shadows,
    ActorEffectPoseRegistry effectPoses,
    AvatarShadeLedger localPlayerShadow)
{
    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly MissileDriver _missiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
    private readonly RealmPlayPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly RealmSignals _realmSignals = worldEvents ?? throw new ArgumentNullException(nameof(worldEvents));
    private readonly ProxyRegistry _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
    private readonly ActorEffectPoseRegistry _fxPostures = effectPoses ?? throw new ArgumentNullException(nameof(effectPoses));
    private readonly AvatarShadeLedger _ownAvatarShade = localPlayerShadow
            ?? throw new ArgumentNullException(nameof(localPlayerShadow));

    public bool Withdraw(uint srvOid, uint ownAvatarOid)
    {
        return !_runtime.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is null
            ? false
            : Withdraw(capture, ownAvatarOid);
    }

    public bool Withdraw(OnlineActorRecord capture, uint ownAvatarOid)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return !_runtime.IsLatestCapture(capture) || capture.WorldEntity is null
            ? false
            : Withdraw(
            capture,
            capture.PositionAuthorityVersion,
            capture.ProjAlterationVer,
            ownAvatarOid);
    }

    public bool Withdraw(
        OnlineActorRecord capture,
        ulong locusArbiterVer,
        ulong projAlterationVer,
        uint ownAvatarOid)
    {
        var verdict = WithdrawPrecise(
            capture,
            locusArbiterVer,
            projAlterationVer,
            ownAvatarOid);
        return verdict.Failure is not null ? throw verdict.Failure : verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed;
    }

    public ExactMirrorWithdrawalVerdict WithdrawPrecise(
        OnlineActorRecord capture,
        ulong locusArbiterVer,
        ulong projAlterationVer,
        uint ownAvatarOid)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_runtime.IsLatestCapture(capture)
            || capture.WorldEntity is null
            || capture.PositionAuthorityVersion != locusArbiterVer
            || capture.ProjAlterationVer != projAlterationVer)
        {
            return new(
                ExactMirrorWithdrawalDisposition.Superseded,
                Failure: null);
        }

        try
        {
            ExitRealm(capture, ownAvatarOid);
            bool finished = _runtime.WithdrawOnlineActorProj(
                capture,
                locusArbiterVer,
                projAlterationVer);
            return new(
                finished
                    ? ExactMirrorWithdrawalDisposition.Completed
                    : ExactMirrorWithdrawalDisposition.Superseded,
                Failure: null);
        }
        catch (Exception problem)
        {
            bool latest = _runtime.IsLatestCapture(capture);
            ExactMirrorWithdrawalDisposition disposition =
                latest
                && capture.PositionAuthorityVersion == locusArbiterVer
                && capture.ProjAlterationVer == projAlterationVer
                && capture.IsSpatiallyProjected
                    ? ExactMirrorWithdrawalDisposition.Pending
                    : latest
                        && capture.PositionAuthorityVersion == locusArbiterVer
                        && !capture.IsSpatiallyProjected
                            ? ExactMirrorWithdrawalDisposition.Completed
                            : ExactMirrorWithdrawalDisposition.Superseded;
            return new(disposition, problem);
        }
    }

    public void ExitRealm(OnlineActorRecord capture, uint ownAvatarOid)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.WorldEntity is not { } actor)
            return;

        bool keptMissileShade = _missiles.ExitRealm(capture);
        if (!keptMissileShade)
            _shades.Suspend(actor.Id);
        _realmPhase.DropByIdent(actor.Id);
        _realmSignals.DropActor(actor.Id);
        if (capture.ServerOid == ownAvatarOid
            && (!_runtime.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
                || ReferenceEquals(latest, capture)))

            _ownAvatarShade.Clear();

        _fxPostures.Delete(actor.Id);
    }
}

public enum ExactMirrorWithdrawalDisposition
{
    Completed,
    Pending,
    Superseded,
}

public readonly record struct ExactMirrorWithdrawalVerdict(
    ExactMirrorWithdrawalDisposition Disposition,
    Exception? Failure);
