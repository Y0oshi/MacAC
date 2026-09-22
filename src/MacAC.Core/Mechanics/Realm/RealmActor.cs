using System.Numerics;

namespace MacAC.Mechanics.Realm;

public readonly record struct PartSwap(byte PartIndex, uint GfxObjId);

public sealed class RealmActor
{
    private const float BboxPadding = 5.0f;

    private SwatchOverride? _swatch;
    private IReadOnlyList<PartSwap> _pieceSwaps = [];

    public required uint Id { get; init; }

    public uint ServerGuid { get; init; }

    public required uint SrcGfxObjRefOrRigIdent { get; init; }

    public required Vector3 Position { get; set; }

    public required Quaternion Rotation { get; set; }

    public required IReadOnlyList<TriMeshRef> MeshRefs { get; set; }

    public bool IsPaintShown { get; set; } = true;

    public bool IsAncestorPaintShown { get; set; } = true;

    public IReadOnlyList<Matrix4x4> IndexedPieceXforms { get; private set; } = [];

    public IReadOnlyList<bool> IndexedPieceOnHand { get; private set; } = [];

    public SwatchOverride? SwatchOverride
    {
        get => _swatch;
        init => _swatch = value;
    }

    public IReadOnlyList<PartSwap> PieceSubstitutions
    {
        get => _pieceSwaps;
        init => _pieceSwaps = value;
    }

    public uint? ParentCellId { get; set; }

    public uint? FxChamberIdent { get; set; }

    public uint? VisChamberIdent => ParentCellId ?? FxChamberIdent;

    public bool IsStructureShell { get; init; }

    public uint? StructureShellMooringChamberIdent { get; init; }

    public float Scale { get; init; } = 1.0f;

    public ulong ConcealedPiecesBitmask { get; init; }

    public Vector3 AabbMin { get; private set; }

    public Vector3 AabbMax { get; private set; }

    public bool AabbStale { get; private set; } = true;

    public Vector3 OwnTiedLower { get; private set; }

    public Vector3 OwnTiedUpper { get; private set; }

    public bool HasOwnLimits { get; private set; }

    public void AssignIndexedPiecePostures(IReadOnlyList<Matrix4x4> xforms, IReadOnlyList<bool> onHand)
    {
        ArgumentNullException.ThrowIfNull(xforms);
        ArgumentNullException.ThrowIfNull(onHand);
        if (xforms.Count != onHand.Count)
            throw new ArgumentException("Indexed part pose and availability counts must match");
        IndexedPieceXforms = xforms;
        IndexedPieceOnHand = onHand;
    }

    public void ImposeLooks(IReadOnlyList<TriMeshRef> triMeshRefs, SwatchOverride? swatchOverride, IReadOnlyList<PartSwap> pieceSubstitutions)
    {
        ArgumentNullException.ThrowIfNull(triMeshRefs);
        ArgumentNullException.ThrowIfNull(pieceSubstitutions);
        MeshRefs = triMeshRefs;
        _swatch = swatchOverride;
        _pieceSwaps = pieceSubstitutions;
    }

    public void AssignOwnLimits(Vector3 lower, Vector3 upper)
    {
        OwnTiedLower = lower;
        OwnTiedUpper = upper;
        HasOwnLimits = true;
        AabbStale = true;
    }

    public void SetPosition(Vector3 spot)
    {
        Position = spot;
        AabbStale = true;
    }

    public void RenewAabb()
    {
        Vector3 at = Position;
        if (HasOwnLimits)
        {
            Vector3 lower = default;
            Vector3 upper = default;
            for (int corner = 0; corner < 8; ++corner)
            {
                Vector3 own = new Vector3(
                    (corner & 1) is 0 ? OwnTiedLower.X : OwnTiedUpper.X,
                    (corner & 2) is 0 ? OwnTiedLower.Y : OwnTiedUpper.Y,
                    (corner & 4) is 0 ? OwnTiedLower.Z : OwnTiedUpper.Z);
                Vector3 turned = Vector3.Transform(own, Rotation);
                if (corner is 0)
                {
                    lower = upper = turned;
                }
                else
                {
                    lower = Vector3.Min(lower, turned);
                    upper = Vector3.Max(upper, turned);
                }
            }
            AabbMin = at + lower - new Vector3(BboxPadding);
            AabbMax = at + upper + new Vector3(BboxPadding);
            AabbStale = false;
            return;
        }

        float radius = BboxPadding;
        if (MeshRefs is { } refs)
        {
            radius = RenewAabbBranch(refs, radius);
        }
        AabbMin = at - new Vector3(radius);
        AabbMax = at + new Vector3(radius);
        AabbStale = false;
    }

    private float RenewAabbBranch(IReadOnlyList<TriMeshRef> refs, float radius)
    {
        float reach = 0f;
        foreach (TriMeshRef piece in refs)
            reach = MathF.Max(reach, piece.PartTransform.Translation.Length());
        radius += reach;
        return radius;
    }
}
