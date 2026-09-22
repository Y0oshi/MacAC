using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Drops a freshly spawned body a short way so it starts on the floor rather than hovering.</summary>
public static class SpawnSettler
{
    public const float SettleGap = 0.5f;
    private const float SettleHop = 0.4f;

    public static bool TrySettle(
        KineticEngine kineticsEngine,
        KineticBody corpus,
        Vector3 realmLocus,
        uint chamberIdent,
        float orbRadius,
        float orbHeight,
        MoverState carrierFlagSet,
        uint movingActorIdent,
        Action strikeTerrain,
        Action departTerrain)
    {
        ArgumentNullException.ThrowIfNull(kineticsEngine);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(strikeTerrain);
        ArgumentNullException.ThrowIfNull(departTerrain);

        if (chamberIdent is 0)
            return false;

        var landing = kineticsEngine.ResolveWithTransition(
            realmLocus,
            realmLocus - new Vector3(0f, 0f, SettleGap),
            chamberIdent,
            orbRadius,
            orbHeight,
            hopUpHeight: SettleHop,
            hopDownHeight: SettleHop,
            isOnTerrain: false,
            corpus,
            carrierFlagSet,
            movingActorIdent);
        if (!landing.Ok || !landing.InContact)
            return false;

        corpus.SealChangeoverLocus(landing.CellId is not 0 ? landing.CellId : chamberIdent, landing.Position);
        KineticObjUpdate.SealSetLocusChangeover(
            corpus,
            landing.InContact,
            landing.OnWalkable,
            landing.CollisionNormalValid,
            landing.CollisionNormal,
            earlierLink: false,
            earlierOnPassable: false,
            strikeTerrain,
            departTerrain);
        return true;
    }
}
