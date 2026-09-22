using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

public static class DollActorAssembler
{
    public const uint DollSrvOid = 0xDA11_D011u;

    public const uint DollRasterizeIdent = 0xDA11_D012u;

    private const float _bearingDeg = 191.367905f;
    private static readonly float _bearingRad = -_bearingDeg * (MathF.PI / 180f);
    private static readonly Quaternion _dollSpin =
        Quaternion.CreateFromAxisAngle(new Vector3(0f, 0f, 1f), _bearingRad);

    public static RealmActor Build(
        uint rigIdent,
        IReadOnlyList<TriMeshRef> triMeshRefs,
        uint? baseSwatchIdent = null,
        IReadOnlyList<(uint SubPaletteId, byte Offset, byte Length)>? subSwatches = null,
        IReadOnlyList<(byte PartIndex, uint GfxObjId)>? pieceSubstitutions = null)
    {
        // Only build when there are sub-palette overlays - same gate as GameWindow.
        SwatchOverride? swatchOverride = null;
        if (subSwatches is { Count: > 0 } spRoster)
        {
            var spans = new SwatchOverride.SubPaletteSpan[spRoster.Count];
            for (int idx = 0; idx < spRoster.Count; ++idx)
                spans[idx] = new SwatchOverride.SubPaletteSpan(
                    spRoster[idx].SubPaletteId,
                    spRoster[idx].Offset,
                    spRoster[idx].Length);
            swatchOverride = new SwatchOverride(
                BasePaletteId: baseSwatchIdent ?? 0u,
                SubPalettes: spans);
        }

        PartSwap[] actorPieceSubstitutions;
        if (pieceSubstitutions is null or { Count: 0 })
        {
            actorPieceSubstitutions = [];
        }
        else
        {
            actorPieceSubstitutions = new PartSwap[pieceSubstitutions.Count];
            for (int idx = 0; idx < pieceSubstitutions.Count; ++idx)
                actorPieceSubstitutions[idx] = new PartSwap(
                    pieceSubstitutions[idx].PartIndex,
                    pieceSubstitutions[idx].GfxObjId);
        }

        return new RealmActor
        {
            Id = DollRasterizeIdent,
            ServerGuid = DollSrvOid,
            SrcGfxObjRefOrRigIdent = rigIdent,
            Position = Vector3.Zero,
            Rotation = _dollSpin,
            MeshRefs = triMeshRefs,
            SwatchOverride = swatchOverride,
            PieceSubstitutions = actorPieceSubstitutions,
            ParentCellId = null,
        };
    }
}
