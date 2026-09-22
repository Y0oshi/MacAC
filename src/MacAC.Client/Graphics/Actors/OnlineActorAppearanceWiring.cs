using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Wire;

namespace MacAC.Client.Graphics;

internal sealed record OnlineActorAppearancePulseLedger(
    RealmActor Entity,
    OnlineActorMotionLedger? Animation);

internal sealed record OnlineActorAppearanceContactPulse(
    OnlineActorRecord Record,
    RealmActor Entity,
    OnlineActorContactRegistration? Registration);

internal static class OnlineActorAppearanceWiring
{
    public static OnlineActorAppearancePulseLedger? Capture(
        OnlineActorCore core,
        uint srvOid)
    {
        ArgumentNullException.ThrowIfNull(core);
        return !core.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            ? null
            : new OnlineActorAppearancePulseLedger(
            actor,
            capture.AnimationRuntime as OnlineActorMotionLedger);
    }

    public static void RebindAnim(
        OnlineActorMotionLedger anim,
        RealmActor actor,
        RigSpec rig,
        float scaling,
        IReadOnlyList<OnlineMotionPartTemplate> pieceBlueprint,
        IReadOnlyList<bool> pieceReadiness)
    {
        ArgumentNullException.ThrowIfNull(anim);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(pieceBlueprint);
        RebindAnimRest(anim, actor, rig, pieceBlueprint, pieceReadiness, scaling);
    }

    private static void RebindAnimRest(OnlineActorMotionLedger anim, RealmActor actor, RigSpec rig, IReadOnlyList<OnlineMotionPartTemplate> pieceBlueprint, IReadOnlyList<bool> pieceReadiness, float scaling)
    {
        ArgumentNullException.ThrowIfNull(pieceReadiness);
        anim.Entity = actor;
        anim.Setup = rig;
        RebindAnimTail(anim, pieceBlueprint, pieceReadiness, scaling);
    }

    private static void RebindAnimTail(OnlineActorMotionLedger anim, IReadOnlyList<OnlineMotionPartTemplate> pieceBlueprint, IReadOnlyList<bool> pieceReadiness, float scaling)
    {
        anim.Scale = scaling;
        anim.PieceBlueprint = pieceBlueprint;
        anim.PieceReadiness = pieceReadiness;
        anim.DirtyExhibitPostures();
    }

    public static OnlineActorAppearanceContactPulse? ReadyImpact(
        OnlineActorCore core,
        OnlineActorContactAssembler builder,
        RealmActor actor,
        RigSpec rig,
        IReadOnlyList<uint> netPieceGfxObjRefIdents,
        RealmSession.MoverSpawn summon,
        Vector3 realmOrigin)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(netPieceGfxObjRefIdents);
        return !core.TryFetchRecord(summon.Guid, out OnlineActorRecord capture)
            || !ReferenceEquals(capture.WorldEntity, actor)
            ? null
            : new OnlineActorAppearanceContactPulse(
            capture,
            actor,
            builder.Build(
                actor,
                rig,
                netPieceGfxObjRefIdents,
                summon,
                capture.ServerOid,
                capture.Generation,
                capture.WorldEntity!,
                capture.FinalKineticsPhase,
                realmOrigin,
                retainVacantCargo: true));
    }

    public static bool SealImpact(
        OnlineActorCore core,
        ProxyRegistry registry,
        OnlineActorAppearanceContactPulse refresh)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(refresh);
        if (!core.TryFetchRecord(refresh.Record.ServerOid, out OnlineActorRecord latest)
            || !ReferenceEquals(latest, refresh.Record)
            || !ReferenceEquals(latest.WorldEntity, refresh.Entity))

            return false;

        OnlineActorContactAssembler.SettleLooks(
            registry,
            refresh.Entity.Id,
            refresh.Registration,
            suspendIfNew: !latest.IsSpatiallyVisible
                || latest.ProjSort is not OnlineActorMirrorKind.World
                || (latest.FinalKineticsPhase & KineticStateFlags.Hidden) != 0);
        return true;
    }
}
