using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public static partial class CellHop
{
    public static void FindTransitCellsSphere(
        KineticAssetCache stash,
        CellKinetics latestChamber,
        uint latestChamberIdent,
        Vector3 realmOrbMiddle,
        float orbRadius,
        ICollection<uint> contenders,
        out bool quitBeyond)
    {
        Orb[] orbs = One(realmOrbMiddle, orbRadius);
        FindTransitCellsSphere(stash, latestChamber, latestChamberIdent, orbs, orbs.Length, contenders, out quitBeyond);
    }

    public static void FindTransitCellsSphere(
        KineticAssetCache stash,
        CellKinetics latestChamber,
        uint latestChamberIdent,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        ICollection<uint> contenders,
        out bool quitBeyond)
    {
        quitBeyond = false;

        uint stem = latestChamberIdent & 0xFFFF0000u;
        int orbTally = NetOrbTally(realmOrbs, countOrbs);
        if (orbTally is 0)
            return;

        for (int gatewayOrdinal = 0; gatewayOrdinal < latestChamber.Portals.Count; ++gatewayOrdinal)
        {
            PortalFacts gateway = latestChamber.Portals[gatewayOrdinal];
            if (!TryGetPortalPlane(latestChamber, gatewayOrdinal, gateway, out Plane plane))
                continue;

            if (gateway.OtherCellId == BeyondGateway)
            {
                // An exit to the outdoors: any sphere straddling the plane means we are leaving.
                if (!quitBeyond)
                    quitBeyond = AnyOrbStraddles(latestChamber, plane, realmOrbs, orbTally);
                continue;
            }

            uint anotherIdent = stem | gateway.OtherCellId;
            CaptureUnionSoleSensor(contenders, anotherIdent);

            var another = stash.FetchChamberStruct(anotherIdent);
            if (another is not null && ContactSweep.HasCellContainment(stash, another))
            {
                // The neighbour can answer exactly: ask its containment BSP.
                for (int idx = 0; idx < orbTally; ++idx)
                {
                    Orb orb = realmOrbs[idx];
                    Vector3 own = Vector3.Transform(orb.Center, another.InverseWorldTransform);
                    if (ContactSweep.SphereIntersectsCell(stash, another, own, orb.Radius))
                    {
                        contenders.Add(anotherIdent);
                        break;
                    }
                }
                continue;
            }

            // Otherwise the padded portal plane decides.
            for (int idx = 0; idx < orbTally; ++idx)
            {
                Orb orb = realmOrbs[idx];
                float rad = orb.Radius + EPSILON;
                float distance = OwnHeight(latestChamber, plane, orb.Center);
                if (gateway.PortalSide ? distance > -rad : distance < rad)
                {
                    contenders.Add(anotherIdent);
                    break;
                }
            }
        }
    }

    public static void SeekPassageChambersBbox(
        KineticAssetCache stash,
        CellKinetics latestChamber,
        uint latestChamberIdent,
        IReadOnlyList<ProxyPartBox> realmPieces,
        IReadOnlyList<Orb> realmPieceOrbs,
        ICollection<uint> contenders,
        out bool quitBeyond)
    {
        quitBeyond = false;

        int pieceTally = Math.Min(realmPieces.Count, realmPieceOrbs.Count);
        if (pieceTally is 0)
            return;

        uint stem = latestChamberIdent & 0xFFFF0000u;

        for (int gatewayOrdinal = 0; gatewayOrdinal < latestChamber.Portals.Count; ++gatewayOrdinal)
        {
            PortalFacts gateway = latestChamber.Portals[gatewayOrdinal];
            if (!TryGetPortalPlane(latestChamber, gatewayOrdinal, gateway, out Plane plane))
                continue;

            for (int idx = 0; idx < pieceTally; ++idx)
            {
                // Cheap sphere reject before the box test.
                Orb orb = realmPieceOrbs[idx];
                float rad = orb.Radius + FEpsilon;
                float distance = OwnHeight(latestChamber, plane, orb.Center);
                if (!(gateway.PortalSide ? distance > -rad : distance < rad))
                    continue;

                var piece = realmPieces[idx];
                piece.RefitToLocal(latestChamber.InverseWorldTransform, out Vector3 ownLower, out Vector3 ownUpper);
                if (!Reaches(CellBspProbe.ClassifyBbox(plane, ownLower, ownUpper), CrossingFlank(gateway.PortalSide)))
                    continue;

                if (gateway.OtherCellId == BeyondGateway)
                {
                    quitBeyond = true;
                    break;   // next portal
                }

                uint anotherIdent = stem | gateway.OtherCellId;
                CaptureUnionSoleSensor(contenders, anotherIdent);
                var another = stash.FetchChamberStruct(anotherIdent);
                if (another is null || !ContactSweep.HasCellContainment(stash, another))
                {
                    contenders.Add(anotherIdent);
                    break;   // next portal
                }

                piece.RefitToLocal(another.InverseWorldTransform, out Vector3 destLower, out Vector3 destUpper);
                if (ContactSweep.BoxIntersectsCell(stash, another, destLower, destUpper))
                {
                    contenders.Add(anotherIdent);
                    break;   // next portal
                }
            }
        }
    }

    public static void CheckBuildingTransit(
        KineticAssetCache stash,
        BuildingKinetics structure,
        Vector3 realmOrbMiddle,
        float orbRadius,
        ICollection<uint> contenders)
    {
        CheckBuildingTransit(stash, structure, One(realmOrbMiddle, orbRadius), 1, contenders, out _);
    }

    public static void CheckBuildingTransit(
        KineticAssetCache stash,
        BuildingKinetics structure,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        ICollection<uint> contenders,
        out bool strikesInteriorChamber)
    {
        strikesInteriorChamber = false;
        int orbTally = NetOrbTally(realmOrbs, countOrbs);
        if (orbTally is 0)
            return;

        foreach (BuildingPortalFacts gateway in structure.Portals)
        {
            if (gateway.AnotherPortalId < 0)
                continue;

            CaptureUnionSoleSensor(contenders, gateway.OtherCellId);
            var interior = stash.FetchChamberStruct(gateway.OtherCellId);
            if (interior is null || !ContactSweep.HasCellContainment(stash, interior))
            {
                if (KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    string cause = interior is null ? "cell not cached" : "CellBSP null";
                    Console.WriteLine(FormattableString.Invariant($"[check-bldg] portal->0x{gateway.OtherCellId:X8} skipped: {cause}"));
                }
                continue;
            }

            bool inside = false;
            for (int idx = 0; idx < orbTally && !inside; ++idx)
            {
                Orb orb = realmOrbs[idx];
                Vector3 own = Vector3.Transform(orb.Center, interior.InverseWorldTransform);
                inside = ContactSweep.SphereIntersectsCell(stash, interior, own, orb.Radius);

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[check-bldg] portal->0x{gateway.OtherCellId:X8} sphere#{idx} wpos=({orb.Center.X:F3},{orb.Center.Y:F3},{orb.Center.Z:F3}) lpos=({own.X:F3},{own.Y:F3},{own.Z:F3}) r={orb.Radius:F3} inside={inside}"));
                }
            }

            if (inside)
            {
                strikesInteriorChamber = true;
                contenders.Add(gateway.OtherCellId);
            }
        }
    }

    private static bool AnyOrbStraddles(CellKinetics chamber, in Plane plane, IReadOnlyList<Orb> realmOrbs, int orbTally)
    {
        for (int idx = 0; idx < orbTally; ++idx)
        {
            Orb orb = realmOrbs[idx];
            float pad = orb.Radius + FEpsilon;
            float distance = OwnHeight(chamber, plane, orb.Center);
            if (distance > -pad && distance < pad)
                return true;
        }
        return false;
    }

    // The side of a portal plane a box must reach to count as crossing it
    private static CellBspProbe.FacetFlank CrossingFlank(bool gatewayFlank)
    {
        return gatewayFlank ? CellBspProbe.FacetFlank.Positive : CellBspProbe.FacetFlank.Negative;
    }

    private static bool Reaches(CellBspProbe.FacetFlank sidedness, CellBspProbe.FacetFlank wanted) =>
        sidedness == CellBspProbe.FacetFlank.Straddle || sidedness == wanted;

    private static void CheckBuildingTransitFromParts(
        KineticAssetCache stash,
        BuildingKinetics structure,
        IReadOnlyList<ProxyPartBox> realmPieces,
        IReadOnlyList<Orb> realmPieceOrbs,
        ICollection<uint> contenders,
        uint seedChamberIdent,
        Vector3 chunkOrigin,
        ref bool exteriorAdded)
    {
        int pieceTally = Math.Min(realmPieces.Count, realmPieceOrbs.Count);
        if (pieceTally is 0)
            return;

        foreach (BuildingPortalFacts structureGateway in structure.Portals)
        {
            int reciprocalOrdinal = structureGateway.AnotherPortalId;
            if (reciprocalOrdinal < 0)
                continue;

            CaptureUnionSoleSensor(contenders, structureGateway.OtherCellId);
            var interior = stash.FetchChamberStruct(structureGateway.OtherCellId);
            if (interior is null || !ContactSweep.HasCellContainment(stash, interior))
                continue;

            if (reciprocalOrdinal >= interior.Portals.Count)
            {
                throw new InvalidDataException(
                    $"Building portal to cell 0x{structureGateway.OtherCellId:X8} references reciprocal portal {reciprocalOrdinal}, but the destination has {interior.Portals.Count} portals");
            }

            PortalFacts reciprocal = interior.Portals[reciprocalOrdinal];
            if (!TryGetPortalPlane(interior, reciprocalOrdinal, reciprocal, out Plane plane))
                continue;

            for (int idx = 0; idx < pieceTally; ++idx)
            {
                Orb orb = realmPieceOrbs[idx];
                float pad = orb.Radius + FEpsilon;
                float distance = OwnHeight(interior, plane, orb.Center);
                // Inclusive BUILDING gate: opposite direction to the indoor exit gate
                if (!(reciprocal.PortalSide ? distance <= pad : distance >= -pad))
                    continue;

                realmPieces[idx].RefitToLocal(interior.InverseWorldTransform, out Vector3 lower, out Vector3 upper);
                CellBspProbe.FacetFlank inwardFlank = reciprocal.PortalSide ? CellBspProbe.FacetFlank.Negative : CellBspProbe.FacetFlank.Positive;
                if (!Reaches(CellBspProbe.ClassifyBbox(plane, lower, upper), inwardFlank))
                    continue;
                if (!ContactSweep.BoxIntersectsCell(stash, interior, lower, upper))
                    continue;

                contenders.Add(structureGateway.OtherCellId);
                SeekPassageChambersBbox(stash, interior, structureGateway.OtherCellId, realmPieces, realmPieceOrbs, contenders, out bool quitBeyond);
                if (quitBeyond && !exteriorAdded)
                    exteriorAdded = AppendAllBeyondChambersFromPieces(realmPieces, seedChamberIdent, chunkOrigin, contenders);
                break;
            }
        }
    }
}
