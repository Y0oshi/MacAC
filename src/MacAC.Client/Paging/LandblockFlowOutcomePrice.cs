using System.Runtime.CompilerServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Landscape;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public readonly record struct LandblockFlowPriceEstimate(
    PagingWorkCost Work,
    long TerrainPayloadBytes,
    int Entities,
    int MeshReferences,
    int VisibilityCells,
    int EnvCellShells,
    int PortalConnections,
    int PortalPolygonVertices,
    int PhysicsEnvCells,
    int PhysicsEnvironments,
    int PhysicsSetups,
    int PhysicsGfxObjects);

public static class LandblockFlowOutcomePrice
{
    private const int ReferenceChargeOctets = 8;
    private const int DictionaryListingChargeOctets = 16;

    public static LandblockFlowPriceEstimate Estimate(
        LandblockFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch
        {
            LandblockFlowOutcome.Fetched fetched =>
                Estimate(fetched.Build, fetched.MeshData),
            LandblockFlowOutcome.Elevated promoted =>
                Estimate(promoted.Build, promoted.MeshData),
            _ => new LandblockFlowPriceEstimate(
                new PagingWorkCost(CompletionAdmissions: 1),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0),
        };
    }

    public static LandblockFlowPriceEstimate Estimate(
        LandblockAssemble assemble,
        LandblockTessellationData triMeshBlob)
    {
        ArgumentNullException.ThrowIfNull(assemble);
        ArgumentNullException.ThrowIfNull(triMeshBlob);

        long landOctets = Add(
            Multiply(triMeshBlob.Vertices.LongLength, Unsafe.SizeOf<TerrainVert>()),
            Multiply(triMeshBlob.Indices.LongLength, sizeof(uint)));
        long keptOctets = landOctets;

        var actors = assemble.Landblock.Entities;
        int triMeshReferences = 0;
        keptOctets = Add(
            keptOctets,
            Multiply(actors.Count, ReferenceChargeOctets));
        for (int idx = 0; idx < actors.Count; ++idx)
        {
            int tally = actors[idx].MeshRefs.Count;
            triMeshReferences = SaturatingAppend(triMeshReferences, tally);
            keptOctets = Add(
                keptOctets,
                Multiply(tally, Unsafe.SizeOf<TriMeshRef>()));
        }

        int visChambers = 0;
        int shells = 0;
        int gatewayConnections = 0;
        int gatewayPolygVerts = 0;
        if (assemble.EnvCells is { } environChambers)
        {
            visChambers = environChambers.VisChambers.Length;
            shells = environChambers.Shells.Length;
            keptOctets = Add(
                keptOctets,
                Multiply(
                    (long)visChambers + shells,
                    ReferenceChargeOctets));

            foreach (var chamber in environChambers.VisChambers)
            {
                gatewayConnections = SaturatingAppend(
                    gatewayConnections,
                    chamber.Portals.Count);
                keptOctets = Add(
                    keptOctets,
                    Multiply(
                        chamber.Portals.Count,
                        Unsafe.SizeOf<Graphics.ChamberGatewayDetails>()));
                keptOctets = Add(
                    keptOctets,
                    Multiply(
                        chamber.ClipPlanes.Count,
                        Unsafe.SizeOf<Graphics.GatewayClipFacet>()));
                keptOctets = Add(
                    keptOctets,
                    Multiply(
                        chamber.VisibleCells.Count,
                        sizeof(uint)));

                foreach (System.Numerics.Vector3[] polyg in chamber.PortalPolygons)
                {
                    gatewayPolygVerts = SaturatingAppend(
                        gatewayPolygVerts,
                        polyg.Length);
                    keptOctets = Add(
                        keptOctets,
                        Multiply(
                            polyg.LongLength,
                            Unsafe.SizeOf<System.Numerics.Vector3>()));
                }
            }

            foreach (EnvironChamberShellStance shell in environChambers.Shells)
            {
                keptOctets = Add(
                    keptOctets,
                    Multiply(shell.Surfaces.Length, sizeof(ushort)));
            }
        }

        KineticDatBundle kinetics =
            assemble.Landblock.PhysicsDats ?? KineticDatBundle.Empty;
        int kineticsEnvironChambers = kinetics.EnvCells.Count;
        int kineticsEnvironments = kinetics.Environments.Count;
        int kineticsSetups = kinetics.Setups.Count;
        int kineticsGfxObjects = kinetics.GfxObjs.Count;
        long kineticsListings = (long)kineticsEnvironChambers
            + kineticsEnvironments
            + kineticsSetups
            + kineticsGfxObjects;
        keptOctets = Add(
            keptOctets,
            Multiply(kineticsListings, DictionaryListingChargeOctets));

        return new LandblockFlowPriceEstimate(
            new PagingWorkCost(
                CompletionAdmissions: 1,
                AdoptedCpuBytes: keptOctets,
                EntityOperations: actors.Count,
                GpuUploadBytes: landOctets),
            landOctets,
            actors.Count,
            triMeshReferences,
            visChambers,
            shells,
            gatewayConnections,
            gatewayPolygVerts,
            kineticsEnvironChambers,
            kineticsEnvironments,
            kineticsSetups,
            kineticsGfxObjects);
    }

    private static long Multiply(long tally, int elemOctets)
    {
        return tally <= 0
            ? 0
            : tally > long.MaxValue / elemOctets
                ? long.MaxValue
                : tally * elemOctets;
    }

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static int SaturatingAppend(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;
}
