using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Graphics;

public sealed partial class LandModernPainter : IDisposable
{
    private const int VertsPerLb = LandblockTessellation.VertsPerLb;
    private const int OrdinalsPerLb = VertsPerLb;
    private const int VertDims = 40;  // sizeof(TerrainVert)
    private const int OrdinalDims = sizeof(uint);
    private const float LbDims = LandblockTessellation.LbDims;  // 192

    private readonly LandTileset _tileset;

    public LandTileset Atlas => _tileset;
    private readonly GpuRetiredLandscapeSlotAllotter _alloc;
    private readonly GpuSunsetRegister _sunsetRegister;
    private bool _destroyed;

    private SocketBlob?[] _sockets;

    private readonly Dictionary<uint, int> _identToSocket = [];

    private long _globalVboCapOctets;
    private long _globalEboCapOctets;

    private int _dynamicCycleSocket;
    private bool _dynamicCycleBegun;

    internal int DynamicIndirectBufTally => 0;

    // Reusable per-frame buffers
    private readonly List<int> _shownSockets = [];
    private readonly HashSet<uint> _strollShownLbs = [];
    private DrawElementsIndirectDirective[] _deicTemp = [];

    private readonly List<(int Start, int Count)> _chamberExecTemp = [];

    private readonly List<(uint FirstIndex, int Count)> _lotExecTemp = [];

    private readonly HashSet<int> _strollSocketsThisCycle = [];

    // Diag
    public int FetchedSockets => _alloc.FetchedTally;
    public int ShownSockets => _shownSockets.Count;
    public int CapSockets => _alloc.Capacity;

    internal int StrollShownSocketTally => _strollSocketsThisCycle.Count;

    internal int StrollPaintTally { get; private set; }

    public void BeginFrame(int cycleSocket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);
        if (_directedShadeCycleSeries == long.MaxValue)
            throw new InvalidOperationException(
                "Directional-shadow terrain frame identity was exhausted");
        ++_directedShadeCycleSeries;
        _sunsetRegister.ReattemptPendingPublications();
        _dynamicCycleSocket = cycleSocket;
        _dynamicCycleBegun = true;
        _strollSocketsThisCycle.Clear();
        StrollPaintTally = 0;
    }

    public void AppendLbWithTriMesh(uint lbIdent, LandblockTessellationData triMeshBlob, Vector3 realmOrigin)
        => AddLandblock(lbIdent, triMeshBlob, realmOrigin);

    public void AddLandblock(uint lbIdent, LandblockTessellationData meshData, Vector3 realmOrigin)
    {
        ArgumentNullException.ThrowIfNull(meshData);
        if (meshData.Vertices.Length != VertsPerLb)
            throw new ArgumentException(
                $"Wanted {VertsPerLb} vertices, got {meshData.Vertices.Length}",
                nameof(meshData));
        if (meshData.Indices.Length != OrdinalsPerLb)
            throw new ArgumentException(
                $"Wanted {OrdinalsPerLb} indices, got {meshData.Indices.Length}",
                nameof(meshData));

        _alloc.RetryQueuedPublications();

        bool replacing = _identToSocket.TryGetValue(lbIdent, out int replacedSocket);
        int socket = _alloc.Reserve(out var needsExpand);
        bool published = false;
        try
        {
            if (needsExpand)
            {
                int newCap = Math.Max(_alloc.Capacity * 2, socket + 1);
                SecureCap(newCap);
            }

            TerrainVert[] bakedVerts = new TerrainVert[VertsPerLb];
            float zLower = float.MaxValue, zUpper = float.MinValue;
            for (int idx = 0; idx < VertsPerLb; ++idx)
            {
                TerrainVert vert = meshData.Vertices[idx];
                Vector3 realmSpot = vert.Position + realmOrigin;
                bakedVerts[idx] = new TerrainVert(realmSpot, vert.Normal, vert.Data0, vert.Data1, vert.Data2, vert.Data3);
                if (realmSpot.Z < zLower) zLower = realmSpot.Z;
                if (realmSpot.Z > zUpper) zUpper = realmSpot.Z;
            }
            if (zLower == float.MaxValue) { zLower = 0f; zUpper = 0f; }

            uint baseVert = (uint)(socket * VertsPerLb);
            uint[] bakedOrdinals = new uint[OrdinalsPerLb];
            for (int idx = 0; idx < OrdinalsPerLb; ++idx)
                bakedOrdinals[idx] = meshData.Indices[idx] + baseVert;

            PushRhiLb(socket, bakedVerts, bakedOrdinals);

            _sockets[socket] = new SocketBlob
            {
                LbId = lbIdent,
                RealmOrigin = realmOrigin,
                FirstIndex = (uint)(socket * OrdinalsPerLb),
                IndexCount = OrdinalsPerLb,
                AabbLower = new Vector3(realmOrigin.X, realmOrigin.Y, zLower),
                AabbUpper = new Vector3(realmOrigin.X + LbDims, realmOrigin.Y + LbDims, zUpper),
            };
            _identToSocket[lbIdent] = socket;
            published = true;

            if (replacing)
            {
                _sockets[replacedSocket] = null;
                _alloc.ReleaseFollowingGpuUse(replacedSocket);
            }
        }
        finally
        {
            if (!published)
                _alloc.RelinquishUnsubmitted(socket);
        }
    }

    public void RemoveLandblock(uint lbIdent)
    {
        _alloc.RetryQueuedPublications();
        if (!_identToSocket.TryGetValue(lbIdent, out var socket))
            return;
        _identToSocket.Remove(lbIdent);
        _sockets[socket] = null;
        _alloc.ReleaseFollowingGpuUse(socket);
    }

    public void Draw(
        IClientCamera cam,
        FrustumFacets? frustum = null,
        uint? neverPruneLbIdent = null,
        IReadOnlySet<uint>? inLensLandcells = null)
    {
        if (_alloc.FetchedTally is 0) return;

        Matrix4x4 lensProj = cam.View * cam.Projection;

        _strollShownLbs.Clear();
        if (inLensLandcells is not null)
        {
            foreach (uint chamberIdent in inLensLandcells)
            {
                _strollShownLbs.Add(chamberIdent & 0xFFFF0000u);
            }
        }

        _shownSockets.Clear();
        for (int socket = 0; socket < _sockets.Length; ++socket)
        {
            SocketBlob? blob = _sockets[socket];
            if (blob is null) continue;
            if (inLensLandcells is not null
                && !_strollShownLbs.Contains(blob.LbId & 0xFFFF0000u))

                continue;
            if (frustum is not null && blob.LbId != neverPruneLbIdent)
            {
                if (!FrustumPruner.IsAabbShown(frustum.Value, blob.AabbLower, blob.AabbUpper))
                    continue;
            }
            _shownSockets.Add(socket);
        }
        if (_shownSockets.Count is 0) return;

        AssembleIndirectDirectives();
        if (!_dynamicCycleBegun)
            throw new InvalidOperationException("BeginFrame has to be called prior to drawing terrain");
        PaintRhi(lensProj, _shownSockets.Count);
    }

    public void PaintLandChambers(
        Matrix4x4 lensProj,
        IReadOnlyList<(uint LandblockId, int SideCellCount, int CellIndex)> chambers)
    {
        ArgumentNullException.ThrowIfNull(chambers);
        if (chambers.Count is 0) return;
        if (!_dynamicCycleBegun)
            throw new InvalidOperationException("BeginFrame has to be called prior to drawing terrain");

        _lotExecTemp.Clear();
        for (int idx = 0; idx < chambers.Count; ++idx)
        {
            (uint lbIdent, int flankChamberTally, int chamberOrdinal) = chambers[idx];
            uint socketTag = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
            if (!_identToSocket.TryGetValue(socketTag, out int socket))
                continue;
            uint baseLeadOrdinal = (uint)(socket * OrdinalsPerLb);

            _chamberExecTemp.Clear();
            AffixChamberOrdinalExecutions(flankChamberTally, chamberOrdinal, _chamberExecTemp);
            for (int r = 0; r < _chamberExecTemp.Count; ++r)
            {
                (int begin, int tally) = _chamberExecTemp[r];
                _lotExecTemp.Add((baseLeadOrdinal + (uint)begin, tally));
            }
            _strollSocketsThisCycle.Add(socket);
        }

        int directiveTally = _lotExecTemp.Count;
        if (directiveTally is 0) return;

        if (_deicTemp.Length < directiveTally)
            _deicTemp = new DrawElementsIndirectDirective[Math.Max(directiveTally, 64)];
        for (int idx = 0; idx < directiveTally; ++idx)
        {
            (uint leadOrdinal, int tally) = _lotExecTemp[idx];
            _deicTemp[idx] = new DrawElementsIndirectDirective
            {
                Count = (uint)tally,
                InstTally = 1u,
                LeadOrdinal = leadOrdinal,
                BaseVert = 0,            // baked into indices on upload
                BaseInst = 0,
            };
        }
        ++StrollPaintTally;
        PaintRhi(lensProj, directiveTally);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _sunsetRegister.ReattemptPendingPublications();
        TeardownRhi();
    }

    internal static void AffixChamberOrdinalExecutions(
        int sideCellCount, int cellIndex, List<(int Start, int Count)> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);
        if (sideCellCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideCellCount),
                sideCellCount,
                "A landscape LOD grid has to be 1, 2, 4, or 8 cells per side");
        }
        if ((uint)cellIndex >= (uint)(sideCellCount * sideCellCount))
            throw new ArgumentOutOfRangeException(nameof(cellIndex));

        int span = LandblockTessellation.ChambersPerSide / sideCellCount;
        int coarseX = cellIndex / sideCellCount;
        int coarseY = cellIndex % sideCellCount;
        int leadCx = coarseX * span;
        int leadCy = coarseY * span;
        if (span == LandblockTessellation.ChambersPerSide)
        {
            executions.Add((0, VertsPerLb));
            return;
        }
        int execLen = span * LandblockTessellation.VertsPerChamber;
        for (int cy = leadCy; cy < leadCy + span; ++cy)
        {
            int begin = (cy * LandblockTessellation.ChambersPerSide + leadCx) * LandblockTessellation.VertsPerChamber;
            executions.Add((begin, execLen));
        }
    }

    // Builds this frame's DrawElementsIndirectDirective array from the visible slot list
    private void AssembleIndirectDirectives()
    {
        if (_deicTemp.Length < _shownSockets.Count)
            _deicTemp = new DrawElementsIndirectDirective[Math.Max(_shownSockets.Count, 64)];
        for (int idx = 0; idx < _shownSockets.Count; ++idx)
        {
            SocketBlob blob = _sockets[_shownSockets[idx]]!;
            _deicTemp[idx] = new DrawElementsIndirectDirective
            {
                Count = (uint)blob.IndexCount,
                InstTally = 1u,
                LeadOrdinal = blob.FirstIndex,
                BaseVert = 0,            // baked into indices on upload
                BaseInst = 0,
            };
        }
    }

    private void SecureCap(int newCap)
    {
        if (newCap <= _alloc.Capacity)
            return;
        SecureRhiCap(newCap);
    }

    private sealed class SocketBlob
    {
        public uint LbId;
        public Vector3 RealmOrigin;
        public uint FirstIndex;
        public int IndexCount;
        public Vector3 AabbLower;
        public Vector3 AabbUpper;
    }
}
