namespace MacAC.Mechanics.Geometry;

public static class EntityFleshingRules
{
    public static bool ShouldKeepEntity(int triMeshRefTally, int rigLampTally) =>
        triMeshRefTally > 0 || rigLampTally > 0;
}
