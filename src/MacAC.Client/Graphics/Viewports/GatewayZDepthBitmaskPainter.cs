using System.Numerics;

namespace MacAC.Client.Graphics;

public sealed partial class GatewayZDepthBitmaskPainter : IDisposable
{
    private readonly AssetTidyCluster _assetList;

    private const int UpperFanVerts = 32;

    internal long DynamicBufCapOctets => 0;

    public void BeginFrame(int cycleSocket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);
        _rhiCycleBegun = true;
    }

    public void PaintZDepthFan(
        ReadOnlySpan<Vector3> realmVerts,
        in Matrix4x4 lensProj,
        ReadOnlySpan<Vector4> planes,
        bool forceFarawayZ)
    {
        if (realmVerts.Length < 3)
            return;
        PaintZDepthFanRhi(realmVerts, in lensProj, planes, forceFarawayZ);
    }

    public void Dispose() => TeardownRhiAssetList();
}
