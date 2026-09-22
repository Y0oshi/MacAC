using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public interface IEnvCellLandblockHerald
{
    EnvironChamberLandblockBulletin StageBulletin(
        EnvironChamberLandblockAssemble assemble);

    bool StepPrepOne(EnvironChamberLandblockBulletin bulletin);

    void LockBulletin(EnvironChamberLandblockBulletin bulletin);
}

public sealed class EnvironChamberLandblockBulletin
{
    internal EnvironChamberLandblockBulletin(
        object holder,
        EnvironChamberLandblockAssemble assemble)
    {
        Owner = holder;
        Build = assemble;
        Replacement = new EnvironChamberLandblock
        {
            GridX = (int)((assemble.LbIdent >> 24) & 0xFFu),
            GridY = (int)((assemble.LbIdent >> 16) & 0xFFu),
        };
        SumLimits = new BatchBoundingBox(
            new Vector3(float.MaxValue),
            new Vector3(float.MinValue));
    }

    internal object Owner { get; }
    internal EnvironChamberLandblockAssemble Build { get; }
    internal EnvironChamberLandblock Replacement { get; }
    internal BatchBoundingBox SumLimits { get; set; }
    internal int ShellCur { get; set; }
    internal bool PrepSealed { get; set; }
    internal bool BulletinSealed { get; set; }
}
