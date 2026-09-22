using System.Numerics;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics.Effects;

internal interface IRealmStageMoteVisibility
{
    void FlagShownSceneryChambers(HashSet<uint> chamberIdents);

    void ConcludeCycle();

    void CancelCycle();
}

internal readonly record struct CanonLandscapeVisibilityFrame(
    IReadOnlySet<uint> CellIds,
    bool HasCompletedWorldView)
{
    internal static CanonLandscapeVisibilityFrame None { get; } = new(
        System.Collections.Frozen.FrozenSet<uint>.Empty,
        HasCompletedWorldView: false);
}

public sealed class MoteVisibilityDriver : IRealmStageMoteVisibility
{
    public const float ExtendedSpanMultiplier = 2f;

    private readonly HashSet<uint> _structureChamberIdents = [];
    private readonly HashSet<uint> _finishedChamberIdents = [];
    private Vector3 _structureBeholderLocus;
    private Vector3 _finishedBeholderLocus;
    private bool _cycleOpen;
    private bool _cycleUsesRealmLens;
    private bool _hasFinishedRealmLens;

    public void BeginFrame(Vector3 beholderLocus)
    {
        _structureChamberIdents.Clear();
        _structureBeholderLocus = beholderLocus;
        _cycleUsesRealmLens = false;
        _cycleOpen = true;
    }

    public void EmployRealmLens()
    {
        if (_cycleOpen)
            _cycleUsesRealmLens = true;
    }

    public void FlagShownSceneryChambers(HashSet<uint> cellIds)
    {
        ArgumentNullException.ThrowIfNull(cellIds);
        if (!_cycleOpen || !_cycleUsesRealmLens)
            return;

        foreach (uint chamberIdent in cellIds)
        {
            uint lo = chamberIdent & 0xFFFFu;
            if (lo is 0u or >= 0x0100u)
            {
                throw new ArgumentException(
                    $"Landscape visibility accepts only outdoor land cells; "
                    + $"0x{chamberIdent:X8} isn't one",
                    nameof(cellIds));
            }
        }

        _structureChamberIdents.UnionWith(cellIds);
    }

    public void ConcludeCycle()
    {
        if (!_cycleOpen)
            return;

        _cycleOpen = false;
        if (!_cycleUsesRealmLens)
        {
            _finishedChamberIdents.Clear();
            _finishedBeholderLocus = _structureBeholderLocus;
            _hasFinishedRealmLens = false;
            return;
        }

        _finishedChamberIdents.Clear();
        _finishedChamberIdents.UnionWith(_structureChamberIdents);
        _finishedBeholderLocus = _structureBeholderLocus;
        _hasFinishedRealmLens = true;
    }

    public void CancelCycle()
    {
        _structureChamberIdents.Clear();
        _cycleUsesRealmLens = false;
        _cycleOpen = false;
    }

    public void Apply(MoteSys motes, float spanMultiplier)
    {
        ArgumentNullException.ThrowIfNull(motes);
        motes.ImposeCanonLens(
            _finishedBeholderLocus,
            _finishedChamberIdents,
            _hasFinishedRealmLens,
            spanMultiplier);
    }

    public void Reset()
    {
        _structureChamberIdents.Clear();
        _finishedChamberIdents.Clear();
        _cycleOpen = false;
        _cycleUsesRealmLens = false;
        _hasFinishedRealmLens = false;
        _structureBeholderLocus = default;
        _finishedBeholderLocus = default;
    }

    internal CanonLandscapeVisibilityFrame GrabFinishedSceneryVis() =>
        new(_finishedChamberIdents, _hasFinishedRealmLens);
}
