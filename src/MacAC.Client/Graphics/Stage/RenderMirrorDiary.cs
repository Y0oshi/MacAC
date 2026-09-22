namespace MacAC.Client.Graphics.Stage;

internal sealed class RenderMirrorDiary(RenderStageEpoch gen)
{
    private readonly List<RenderMirrorDiff> _diffs = [];
    private ulong _upcomingSeries = 1;

    public RenderStageEpoch Generation { get; private set; } = gen;
    public int Count => _diffs.Count;
    public int Capacity => _diffs.Capacity;

    public void Register(in RenderMirrorRecord capture)
    {
        _diffs.Add(RenderMirrorDiff.Register(
            Generation,
            UpcomingSeries(),
            in capture));
    }

    public void Update(
        RenderMirrorDiffKind sort,
        in RenderMirrorRecord capture)
    {
        _diffs.Add(RenderMirrorDiff.Update(
            sort,
            Generation,
            UpcomingSeries(),
            in capture));
    }

    public void Unregister(
        RenderMirrorId ident,
        RasterizeHolderIncarnation incarnation)
    {
        _diffs.Add(RenderMirrorDiff.Unregister(
            Generation,
            UpcomingSeries(),
            ident,
            incarnation));
    }

    public void AffixDifference(
        in RenderMirrorRecord preceding,
        in RenderMirrorRecord current)
    {
        if (preceding.Id != current.Id
            || preceding.OwnerIncarnation != current.OwnerIncarnation
            || preceding.ProjectionClass != current.ProjectionClass)
        {
            throw new ArgumentException(
                "A channel update can't change projection identity, incarnation, or class",
                nameof(current));
        }

        if (preceding.Transform != current.Transform
            || preceding.Bounds != current.Bounds
            || preceding.SortKey != current.SortKey
            || preceding.Source.TransformFingerprint
                != current.Source.TransformFingerprint)

            Update(RenderMirrorDiffKind.UpdateTransform, in current);

        if (preceding.MeshSet != current.MeshSet
            || preceding.Material != current.Material
            || preceding.DegradeState != current.DegradeState
            || preceding.EntityPayload != current.EntityPayload
            || preceding.Source.GeometryFingerprint
                != current.Source.GeometryFingerprint
            || preceding.Source.AppearanceFingerprint
                != current.Source.AppearanceFingerprint)

            Update(RenderMirrorDiffKind.UpdateAppearance, in current);

        if (preceding.Residency != current.Residency)
            Update(RenderMirrorDiffKind.Rebucket, in current);
        if (preceding.Flags != current.Flags)
            Update(RenderMirrorDiffKind.UpdateFlags, in current);
    }

    public RenderDiffApplyResult EmptyTo(IRenderStage tableau)
    {
        ArgumentNullException.ThrowIfNull(tableau);
        if (tableau.Generation != Generation)
        {
            throw new InvalidOperationException(
                $"Journal {Generation} can't drain into scene {tableau.Generation}.");
        }

        var outcome =
            tableau.Apply(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_diffs));
        _diffs.Clear();
        return outcome;
    }

    public void Clear(RenderStageEpoch replacementGeneration)
    {
        if (replacementGeneration.CompareTo(Generation) <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementGeneration),
                replacementGeneration,
                "A replacement journal generation must advance");
        }

        _diffs.Clear();
        Generation = replacementGeneration;
    }

    internal ReadOnlySpan<RenderMirrorDiff> Pending =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_diffs);

    private ulong UpcomingSeries()
    {
        return _upcomingSeries == ulong.MaxValue
            ? throw new InvalidOperationException(
                "Render projection journal sequence exhausted")
            : _upcomingSeries++;
    }
}
