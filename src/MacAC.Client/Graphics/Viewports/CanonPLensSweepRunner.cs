using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Heavens;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics;

internal interface IEnvironChamberImmediatePaintDrain
{
    void PaintImmediate(
        uint chamberIdent,
        EnvironChamberSeeThruCourse course,
        bool specificsCanvasEngaged);
}

internal sealed class CanonPViewCellSource(ChamberVis cells) : ICanonPViewCellSource
{
    private readonly ChamberVis _chambers = cells ?? throw new ArgumentNullException(nameof(cells));

    public FetchedChamber? Find(uint chamberIdent)
    {
        return _chambers.TryFetchChamber(chamberIdent, out FetchedChamber? chamber) ? chamber : null;
    }
}

internal sealed partial class CanonPLensSweepRunner : IEnvironChamberImmediatePaintDrain
{
    private readonly IRealmPassSurface _canvas;

    private readonly IRenderFrameGlLedger _cycleGlPhase;

    private readonly ClipCycle _clipCycle;

    private readonly LandModernPainter? _land;

    private readonly EnvironChamberPainter _environChambers;
    private readonly HeavensPainter? _heavens;

    private readonly MoteSys? _motes;

    private readonly MotePainter? _motePainter;

    private readonly GatewayZDepthBitmaskPainter? _portalDepthMask;

    private readonly CanonAlphaFifo _alpha;

    private readonly LandscapeDrawTelemetryDriver _landTelemetry;

    private readonly HashSet<uint> _noTableauMoteActorIdents = [];

    private readonly EnvironChamberAlphaPaintOrigin _environChamberClipAlphaSrc;

    private readonly EnvironChamberAlphaPaintOrigin _environChamberBlendAlphaSrc;

    internal delegate void RenderImmediateEnvCellRoute(
        uint cellId,
        EnvironChamberSeeThruCourse route,
        bool detailSurfaceActive);

    public CanonPLensSweepRunner(
        IRealmPassSurface surface,
        IRenderFrameGlLedger frameGlState,
        ClipCycle clipFrame,
        LandModernPainter? land,
        EnvironChamberPainter envCells,
        RealmPaintRouter entities,
        HeavensPainter? heavens,
        MoteSys? motes,
        MotePainter? motePainter,
        GatewayZDepthBitmaskPainter? gatewayZDepthBitmask,
        CanonAlphaFifo alpha,
        LandscapeDrawTelemetryDriver terrainDiagnostics)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _cycleGlPhase = frameGlState
            ?? throw new ArgumentNullException(nameof(frameGlState));
        _clipCycle = clipFrame ?? throw new ArgumentNullException(nameof(clipFrame));
        _land = land;
        _environChambers = envCells ?? throw new ArgumentNullException(nameof(envCells));
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _heavens = heavens;
        _motes = motes;
        _motePainter = motePainter;
        _portalDepthMask = gatewayZDepthBitmask;
        _alpha = alpha ?? throw new ArgumentNullException(nameof(alpha));
        _landTelemetry = terrainDiagnostics
            ?? throw new ArgumentNullException(nameof(terrainDiagnostics));
        _environChamberClipAlphaSrc = new EnvironChamberAlphaPaintOrigin(
            _environChambers.PaintSeeThruSequenced,
            EnvironChamberSeeThruCourse.Clip);
        _environChamberBlendAlphaSrc = new EnvironChamberAlphaPaintOrigin(
            _environChambers.PaintSeeThruSequenced,
            EnvironChamberSeeThruCourse.Alpha);
    }

    private readonly List<uint> _singleChamberRosterTemp = new(1);

    internal delegate void RenderEnvCellsByRoute(
        IReadOnlyList<uint> cellIds,
        EnvironChamberSeeThruCourse route,
        bool detailSurfaceActive);

    internal sealed class EnvironChamberAlphaPaintOrigin : ICanonAlphaDrawSource
    {
        private readonly RenderEnvCellsByRoute _rasterizeSeeThruSequenced;
        private readonly EnvironChamberSeeThruCourse _course;
        private readonly Action? _restartWatcher;
        private readonly List<uint> _queuedChamberIdents = [];
        private readonly List<uint> _readiedChamberIdents = [];
        private readonly List<uint> _paintTemp = [];

        internal EnvironChamberAlphaPaintOrigin(
            RenderEnvCellsByRoute renderTransparentOrdered,
            EnvironChamberSeeThruCourse course,
            Action? restartWatcher = null)
        {
            _rasterizeSeeThruSequenced = renderTransparentOrdered
                ?? throw new ArgumentNullException(nameof(renderTransparentOrdered));
            _course = course;
            _restartWatcher = restartWatcher;
        }

        public void ReadyAlphaDraws(ReadOnlySpan<int> tickets)
        {
            _readiedChamberIdents.Clear();
            for (int idx = 0; idx < tickets.Length; ++idx)
                _readiedChamberIdents.Add(_queuedChamberIdents[tickets[idx]]);
        }

        public void SketchReadiedAlphaLot(int leadReadiedPaint, int paintTally)
        {
            if (paintTally <= 0)
                return;
            _paintTemp.Clear();
            for (int idx = 0; idx < paintTally; ++idx)
                _paintTemp.Add(_readiedChamberIdents[leadReadiedPaint + idx]);
            _rasterizeSeeThruSequenced(
                _paintTemp,
                _course,
                detailSurfaceActive: false);
        }

        public void RestartAlphaSubmissions()
        {
            _queuedChamberIdents.Clear();
            _readiedChamberIdents.Clear();
            _paintTemp.Clear();
            _restartWatcher?.Invoke();
        }

        internal int AppendQueuedChamberIdent(uint chamberIdent)
        {
            int ticket = _queuedChamberIdents.Count;
            _queuedChamberIdents.Add(chamberIdent);
            return ticket;
        }

        internal void RevertQueuedChamberIdent(int ticket)
        {
            if (ticket != _queuedChamberIdents.Count - 1)
            {
                throw new InvalidOperationException(
                    "Only the just-reserved EnvCell alpha token can be rolled back");
            }
            _queuedChamberIdents.RemoveAt(ticket);
        }

        internal int QueuedTally => _queuedChamberIdents.Count;
        internal int QueuedCap => _queuedChamberIdents.Capacity;
        internal int ReadiedCap => _readiedChamberIdents.Capacity;
        internal int PaintCap => _paintTemp.Capacity;
    }
}
