using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;
using MacAC.Sim.Realm;

namespace MacAC.Sim.Play;

/// <summary>Closes an open vendor window once the player has walked (or teleported) out of use range.</summary>
public static class SimMerchantRangeProbe
{

    public static void EnforceSpan(SimCore core)
    {
        ArgumentNullException.ThrowIfNull(core);

        MerchantPhase vendor = core.SatchelHolder.Vendor;
        uint merchantIdent = vendor.MerchantIdent;
        if (merchantIdent is 0u)
            return;

        var passage = core.PassageHolder;
        if (passage.HasQueuedWarpBegin || passage.IsWarpEngaged)
        {
            vendor.Close();
            return;
        }

        var actors = core.EntityObjects.Entities;
        uint avatarOid = core.AvatarIdentity.ServerGuid;
        if (avatarOid is 0u
            || !actors.TryFetchEngaged(avatarOid, out SimActorRecord avatar)
            || avatar.Snapshot.Position is not { } avatarLocus)

            return;

        if (!actors.TryFetchEngaged(merchantIdent, out SimActorRecord merchant) || merchant.Snapshot.Position is not { } merchantLocus)
        {
            vendor.Close();
            return;
        }

        var kinetics = core.EntityObjects.Physics;
        bool inSpan = ReachMath.ObjectsInSpan(
            RemotePositionMath.ToRealm(avatarLocus),
            kinetics.LocateObjectChartHub(avatarOid)?.Radius ?? 0f,
            0f,
            RemotePositionMath.ToRealm(merchantLocus),
            kinetics.LocateObjectChartHub(merchantIdent)?.Radius ?? 0f,
            0f,
            merchant.Snapshot.UseRadius ?? 0f,
            useRadii: true,
            ignoreZDiff: false);

        if (!inSpan)
            vendor.Close();
    }

}
