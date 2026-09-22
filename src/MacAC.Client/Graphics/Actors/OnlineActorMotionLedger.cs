using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed class OnlineActorMotionLedger : IOnlineActorMotionEngine
{
    public required RealmActor Entity;
    RealmActor IOnlineActorMotionEngine.Entity => Entity;
    uint IOnlineActorMotionEngine.CurrentMotion => Sequencer?.CurrentMotion ?? 0u;

    public required RigSpec Setup;
    public required MotionClip Animation;
    public required int LoCycle;
    public required int HighFrame;
    public required float Framerate;
    public required float Scale;
    public required IReadOnlyList<OnlineMotionPartTemplate> PieceBlueprint;
    public required IReadOnlyList<bool> PieceReadiness;
    public float CurrCycle;
    public AnimSequencer? Sequencer;

    public IReadOnlyList<PieceTransform>? ReadiedSeriesCycles;
    public bool SeriesAdvancedPriorAnimPass;
    public readonly Pose TrunkLocomotionTemp = new();
    public readonly MotionDeltaPose TrunkLocomotionDiffTemp = new();
    public readonly List<PieceTransform> SeriesCyclesTemp = [];
    public readonly List<PieceTransform> PlanCyclesTemp = [];

    public readonly List<TriMeshRef> TriMeshRefsTemp = [];
    public readonly List<Matrix4x4> FxPiecePosturesTemp = [];
    public readonly List<Matrix4x4> VisualPiecePosturesTemp = [];
    public bool ExhibitPosturesInitialized;
    public ulong ExhibitRev { get; private set; } = 1UL;

    public double PreviousSeriesProbeMoment;
    public double PreviousPieceProbeMoment;

    public void DirtyExhibitPostures()
    {
        ++ExhibitRev;
        if (ExhibitRev is 0UL)
            ++ExhibitRev;
        ExhibitPosturesInitialized = false;
        VisualPiecePosturesTemp.Clear();
        FxPiecePosturesTemp.Clear();
    }

    public IReadOnlyList<PieceTransform> GrabSeriesCycles(
        IReadOnlyList<PieceTransform> src)
    {
        ArgumentNullException.ThrowIfNull(src);
        SeriesCyclesTemp.Clear();
        for (int idx = 0; idx < src.Count; ++idx)
            SeriesCyclesTemp.Add(src[idx]);
        return SeriesCyclesTemp;
    }

    public IReadOnlyList<PieceTransform> GrabPlanCycles(
        IReadOnlyList<PieceTransform> src)
    {
        ArgumentNullException.ThrowIfNull(src);
        PlanCyclesTemp.Clear();
        for (int idx = 0; idx < src.Count; ++idx)
            PlanCyclesTemp.Add(src[idx]);
        return PlanCyclesTemp;
    }
}

internal readonly record struct OnlineMotionPartTemplate(
    uint GfxObjId,
    IReadOnlyDictionary<uint, uint>? SurfaceOverrides,
    bool IsDrawable);
