namespace MacAC.Client.Graphics.Batching;

public static class EnvCellMeshPreparationRota
{
    public static void Plan(
        EnvironChamberLandblockAssemble assemble,
        ThingTriMeshKeeper triMeshKeeper)
    {
        HashSet<ulong> scheduled = new HashSet<ulong>();
        foreach (var shell in assemble.Shells)
        {
            if (!scheduled.Add(shell.GeometryId))
                continue;
            _ = triMeshKeeper.PrepareEnvCellGeomMeshDataAsync(
                shell.GeometryId,
                shell.CellId,
                shell.EnvironmentId,
                shell.CellStructure,
                [.. shell.Surfaces]);
        }
    }
}
