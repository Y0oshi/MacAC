using System.Runtime.CompilerServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal readonly record struct DirectionalShadeLandscapeRange(
    uint FirstIndex,
    int IndexCount,
    uint LandblockId = 0u);

internal readonly record struct DirectionalShadeLandscapeGeometry(
    IClientGpuBuffer VertexBuffer,
    IClientGpuBuffer IndexBuffer);

internal sealed class DirectionalShadeLandscapePreparedDraws
{
    private DirectionalShadeLandscapeRange[] _spans = [];
    private DrawElementsIndirectDirective[] _engagedDirectives = [];
    private int _spanTally;
    private int _engagedTally;
    private bool _structure;

    public long SrcCycleSeries { get; private set; }

    public ulong BuildSeries { get; private set; }

    public ulong EngagedSelectionSequence { get; private set; }

    public ReadOnlySpan<DrawElementsIndirectDirective> Commands =>
        _engagedDirectives.AsSpan(0, _engagedTally);

    public ReadOnlySpan<DirectionalShadeLandscapeRange> HousedSpans =>
        _spans.AsSpan(0, _spanTally);

    public long RetainedTempBytes
    {
        get
        {
            return checked(
            (long)_spans.Length
                * Unsafe.SizeOf<DirectionalShadeLandscapeRange>()
            + (long)_engagedDirectives.Length
                * Unsafe.SizeOf<DrawElementsIndirectDirective>());
        }
    }

    public bool TryBegin(long frameSequence, int estimatedDirectives)
    {
        if (_structure)
            throw new InvalidOperationException(
                "A terrain shadow draw build is by now active");
        if (frameSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameSequence));
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedDirectives);
        if (SrcCycleSeries == frameSequence)
            return false;

        SecureCap(estimatedDirectives);
        _spanTally = 0;
        _structure = true;
        return true;
    }

    public void Add(in DirectionalShadeLandscapeRange range)
    {
        if (!_structure)
            throw new InvalidOperationException(
                "Begin a terrain shadow draw build prior to adding ranges");
        if (range.IndexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(range));
        SecureCap(checked(_spanTally + 1));
        _spans[_spanTally++] = range;
    }

    public void Complete(long frameSequence)
    {
        if (!_structure)
            throw new InvalidOperationException(
                "No terrain shadow draw build is active");
        if (frameSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameSequence));
        SrcCycleSeries = frameSequence;
        BuildSeries = checked(BuildSeries + 1);
        _structure = false;
        ReassembleEngagedAll();
    }

    public void Abort()
    {
        _spanTally = 0;
        _engagedTally = 0;
        _structure = false;
    }

    internal void ApplySelection(in CanonLandscapeVisibilityFrame vis)
    {
        IReadOnlySet<uint> shown = vis.CellIds
            ?? CanonLandscapeVisibilityFrame.None.CellIds;
        _engagedTally = 0;
        if (vis.HasCompletedWorldView)
        {
            for (int spanOrdinal = 0; spanOrdinal < _spanTally; ++spanOrdinal)
            {
                var span = _spans[spanOrdinal];
                uint stem = span.LandblockId & 0xFFFF0000u;
                bool chosen = false;
                for (uint lo = 1u; lo <= 64u; ++lo)
                {
                    if (shown.Contains(stem | lo))
                    {
                        chosen = true;
                        break;
                    }
                }
                if (chosen)
                    Emit(in span);
            }
        }
        EngagedSelectionSequence = checked(EngagedSelectionSequence + 1);
    }

    private void ReassembleEngagedAll()
    {
        SecureCap(_spanTally);
        _engagedTally = 0;
        for (int spanOrdinal = 0; spanOrdinal < _spanTally; ++spanOrdinal)
            Emit(in _spans[spanOrdinal]);
        EngagedSelectionSequence = checked(EngagedSelectionSequence + 1);
    }

    private void Emit(in DirectionalShadeLandscapeRange span)
    {
        _engagedDirectives[_engagedTally++] = new DrawElementsIndirectDirective
        {
            Count = checked((uint)span.IndexCount),
            InstTally = 1,
            LeadOrdinal = span.FirstIndex,
            BaseVert = 0,
            BaseInst = 0,
        };
    }

    private void SecureCap(int needed)
    {
        if (_spans.Length >= needed)
            return;
        int cap = _spans.Length is 0 ? 16 : _spans.Length;
        while (cap < needed)
            cap = checked(cap * 2);
        Array.Resize(ref _spans, cap);
        Array.Resize(ref _engagedDirectives, cap);
    }
}

public sealed partial class LandModernPainter
{
    private readonly DirectionalShadeLandscapePreparedDraws
        _directedShadeLandDraws = new();
    private long _directedShadeCycleSeries;
    private uint[] _directedShadeSocketLbs = [];
    private uint[] _directedShadeSocketLeadOrdinals = [];
    private int[] _directedShadeSocketOrdinalCounts = [];
    private bool[] _directedShadeSocketPresent = [];
    private bool _directedShadeWiringCaptureValid;
    private long _directedShadeWiringSeries;

    internal DirectionalShadeLandscapeGeometry FetchDirectedShadeGeo()
    {
        return new(
        _vertVault ?? throw new InvalidOperationException("Terrain has no vertex store"),
        _ordinalVault ?? throw new InvalidOperationException("Terrain has no index store"));
    }

    internal DirectionalShadeLandscapePreparedDraws
        ReadyDirectedShadeDraws(
            in CanonLandscapeVisibilityFrame vis)
    {
        SecureDirectedShadeSocketCap(_sockets.Length);
        bool wiringAltered = !_directedShadeWiringCaptureValid;
        for (int socket = 0; socket < _sockets.Length; ++socket)
        {
            SocketBlob? blob = _sockets[socket];
            bool present = blob is not null;
            if (_directedShadeSocketPresent[socket] != present
                || present
                    && (_directedShadeSocketLbs[socket] != blob!.LbId
                        || _directedShadeSocketLeadOrdinals[socket] != blob.FirstIndex
                        || _directedShadeSocketOrdinalCounts[socket] != blob.IndexCount))

                wiringAltered = true;
        }

        if (wiringAltered)
        {
            _directedShadeWiringSeries = checked(
                _directedShadeWiringSeries + 1);
            _directedShadeLandDraws.TryBegin(
                _directedShadeWiringSeries,
                _alloc.FetchedTally);
            try
            {
                for (int socket = 0; socket < _sockets.Length; ++socket)
                {
                    SocketBlob? blob = _sockets[socket];
                    bool present = blob is not null;
                    _directedShadeSocketPresent[socket] = present;
                    if (!present)
                    {
                        _directedShadeSocketLbs[socket] = 0u;
                        _directedShadeSocketLeadOrdinals[socket] = 0u;
                        _directedShadeSocketOrdinalCounts[socket] = 0;
                        continue;
                    }
                    _directedShadeSocketLbs[socket] = blob!.LbId;
                    _directedShadeSocketLeadOrdinals[socket] = blob.FirstIndex;
                    _directedShadeSocketOrdinalCounts[socket] = blob.IndexCount;
                    var span = new DirectionalShadeLandscapeRange(
                        blob.FirstIndex,
                        blob.IndexCount,
                        blob.LbId);
                    _directedShadeLandDraws.Add(in span);
                }
                _directedShadeLandDraws.Complete(
                    _directedShadeWiringSeries);
                _directedShadeWiringCaptureValid = true;
            }
            catch
            {
                // The snapshot fields are populated while the retained product is built.
                _directedShadeWiringCaptureValid = false;
                _directedShadeLandDraws.Abort();
                throw;
            }
        }

        _directedShadeLandDraws.ApplySelection(in vis);
        return _directedShadeLandDraws;
    }

    private void SecureDirectedShadeSocketCap(int needed)
    {
        if (_directedShadeSocketPresent.Length >= needed)
            return;
        int cap = _directedShadeSocketPresent.Length is 0
            ? 16
            : _directedShadeSocketPresent.Length;
        while (cap < needed)
            cap = checked(cap * 2);
        Array.Resize(ref _directedShadeSocketLbs, cap);
        Array.Resize(ref _directedShadeSocketLeadOrdinals, cap);
        Array.Resize(ref _directedShadeSocketOrdinalCounts, cap);
        Array.Resize(ref _directedShadeSocketPresent, cap);
    }
}
