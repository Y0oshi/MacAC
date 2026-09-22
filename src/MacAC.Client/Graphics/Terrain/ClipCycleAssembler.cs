using System.Numerics;

namespace MacAC.Client.Graphics;

public readonly record struct ClipLensSlice(
    int Slot, Vector4 NdcAabb, Vector4[] Planes, bool NothingVisible = false);

public sealed class ClipCycleAssembly
{
    public ClipCycle Frame { get; private set; } = null!;

    public Dictionary<uint, int> ChamberIdentToSocket { get; } = [];

    public Dictionary<uint, int[]> ChamberIdentToLensSockets { get; } = [];

    public Dictionary<uint, ClipLensSlice[]> ChamberIdentToLensSlices { get; } = [];

    public ClipLensSlice[] BeyondLensSlices { get; private set; } = [];

    public int ExteriorSocket { get; internal set; }
    public bool ExteriorShown { get; internal set; }
    public bool HasBeyondLens { get; internal set; }
    public Vector4 BeyondLensNdcAabb { get; internal set; }

    public int BeyondPlaneTally { get; internal set; }
    public Dictionary<uint, int> PerChamberPlaneCounts { get; } = [];
    public int ScissorBackups { get; internal set; }

    private readonly Dictionary<int, Stack<ClipLensSlice[]>> _sliceArrsByLen = [];
    private readonly Dictionary<int, Stack<int[]>> _socketArrsByLen = [];
    internal List<ClipLensSlice> SliceTemp { get; } = [];
    internal int SliceArrAllocTally { get; private set; }
    internal int SocketArrAllocTally { get; private set; }
    internal const int UpperKeptSliceGearList = 4096;
    internal const int UpperKeptSocketGearList = 8192;
    internal const int UpperKeptArrsPerReservoir = 128;
    internal int KeptSliceGearList { get; private set; }
    internal int KeptSocketGearList { get; private set; }
    internal int KeptSliceArrs { get; private set; }
    internal int KeptSocketArrs { get; private set; }

    internal void Reset(ClipCycle cycle)
    {
        Frame = cycle;
        foreach (ClipLensSlice[] slices in ChamberIdentToLensSlices.Values)
            YieldSlices(slices);
        foreach (int[] sockets in ChamberIdentToLensSockets.Values)
            YieldSockets(sockets);
        if (BeyondLensSlices.Length is not 0)
            YieldSlices(BeyondLensSlices);

        ChamberIdentToSocket.Clear();
        ChamberIdentToLensSockets.Clear();
        ChamberIdentToLensSlices.Clear();
        PerChamberPlaneCounts.Clear();
        BeyondLensSlices = [];
        SliceTemp.Clear();
    }

    internal ClipLensSlice[] DuplicateSlices(List<ClipLensSlice> src)
    {
        if (src.Count is 0)
            return [];
        var outcome = RentSlices(src.Count);
        src.CopyTo(outcome, 0);
        return outcome;
    }

    internal int[] DuplicateSockets(ClipLensSlice[] slices)
    {
        if (slices.Length is 0)
            return [];
        int[] outcome = RentSockets(slices.Length);
        for (int idx = 0; idx < slices.Length; ++idx)
            outcome[idx] = slices[idx].Slot;
        return outcome;
    }

    internal void AssignBeyondLensSlices(ClipLensSlice[] slices) => BeyondLensSlices = slices;

    internal void YieldBeyondLensSlicesForReassembly()
    {
        if (BeyondLensSlices.Length is not 0)
            YieldSlices(BeyondLensSlices);
        BeyondLensSlices = [];
    }

    private ClipLensSlice[] RentSlices(int len)
    {
        if (_sliceArrsByLen.TryGetValue(len, out Stack<ClipLensSlice[]>? reservoir)
            && reservoir.Count is not 0)
        {
            var outcome = reservoir.Pop();
            KeptSliceGearList -= outcome.Length;
            --KeptSliceArrs;
            if (reservoir.Count is 0)
                _sliceArrsByLen.Remove(len);
            return outcome;
        }
        ++SliceArrAllocTally;
        return new ClipLensSlice[len];
    }

    private int[] RentSockets(int len)
    {
        if (_socketArrsByLen.TryGetValue(len, out Stack<int[]>? reservoir)
            && reservoir.Count is not 0)
        {
            int[] outcome = reservoir.Pop();
            KeptSocketGearList -= outcome.Length;
            --KeptSocketArrs;
            if (reservoir.Count is 0)
                _socketArrsByLen.Remove(len);
            return outcome;
        }
        ++SocketArrAllocTally;
        return new int[len];
    }

    private void YieldSlices(ClipLensSlice[] arr)
    {
        System.Array.Clear(arr);
        if (arr.Length > UpperKeptSliceGearList)
            return;
        while (KeptSliceGearList + arr.Length > UpperKeptSliceGearList
               || KeptSliceArrs >= UpperKeptArrsPerReservoir)
        {
            if (!EvictOneSliceArr())
                break;
        }
        if (!_sliceArrsByLen.TryGetValue(arr.Length, out Stack<ClipLensSlice[]>? reservoir))
        {
            reservoir = new Stack<ClipLensSlice[]>();
            _sliceArrsByLen.Add(arr.Length, reservoir);
        }
        reservoir.Push(arr);
        KeptSliceGearList += arr.Length;
        ++KeptSliceArrs;
    }

    private void YieldSockets(int[] arr)
    {
        if (arr.Length > UpperKeptSocketGearList)
            return;
        while (KeptSocketGearList + arr.Length > UpperKeptSocketGearList
               || KeptSocketArrs >= UpperKeptArrsPerReservoir)
        {
            if (!EvictOneSocketArr())
                break;
        }
        if (!_socketArrsByLen.TryGetValue(arr.Length, out Stack<int[]>? reservoir))
        {
            reservoir = new Stack<int[]>();
            _socketArrsByLen.Add(arr.Length, reservoir);
        }
        reservoir.Push(arr);
        KeptSocketGearList += arr.Length;
        ++KeptSocketArrs;
    }

    private bool EvictOneSliceArr()
    {
        int chosenLen = -1;
        foreach ((int len, Stack<ClipLensSlice[]> reservoir) in _sliceArrsByLen)
        {
            if (reservoir.Count is not 0 && len > chosenLen)
                chosenLen = len;
        }
        if (chosenLen < 0)
            return false;
        var chosen = _sliceArrsByLen[chosenLen];
        var evicted = chosen.Pop();
        KeptSliceGearList -= evicted.Length;
        --KeptSliceArrs;
        if (chosen.Count is 0)
            _sliceArrsByLen.Remove(chosenLen);
        return true;
    }

    private bool EvictOneSocketArr()
    {
        int chosenLen = -1;
        foreach ((int len, Stack<int[]> reservoir) in _socketArrsByLen)
        {
            if (reservoir.Count is not 0 && len > chosenLen)
                chosenLen = len;
        }
        if (chosenLen < 0)
            return false;
        var chosen = _socketArrsByLen[chosenLen];
        int[] evicted = chosen.Pop();
        KeptSocketGearList -= evicted.Length;
        --KeptSocketArrs;
        if (chosen.Count is 0)
            _socketArrsByLen.Remove(chosenLen);
        return true;
    }
}

public static class ClipCycleAssembler
{
    public static ClipCycleAssembly CommenceStrollCycle(
        ClipCycle cycle,
        bool exteriorTrunk,
        ClipCycleAssembly? reuseAssembly = null)
    {
        System.ArgumentNullException.ThrowIfNull(cycle);
        cycle.Reset();
        ClipCycleAssembly assembly = reuseAssembly ?? new ClipCycleAssembly();
        assembly.Reset(cycle);

        if (exteriorTrunk)
        {
            CommenceStrollCycleBranch2(assembly);
        }
        else
        {
            CommenceStrollCycleBranch(assembly);
        }

        return assembly;
    }

    private static void CommenceStrollCycleBranch(ClipCycleAssembly assembly)
    {
        assembly.ExteriorSocket = 0;
        assembly.ExteriorShown = false;
        assembly.HasBeyondLens = false;
        CommenceStrollCycleTail(assembly);
    }

    private static void CommenceStrollCycleTail(ClipCycleAssembly assembly)
    {
        assembly.BeyondLensNdcAabb = Vector4.Zero;
        assembly.BeyondPlaneTally = 0;
        assembly.ScissorBackups = 0;
    }

    private static void CommenceStrollCycleBranch2(ClipCycleAssembly assembly)
    {
        var slices = assembly.SliceTemp;
        slices.Clear();
        Vector4 wholeMonitor = new Vector4(-1f, -1f, 1f, 1f);
        slices.Add(new ClipLensSlice(0, wholeMonitor, []));
        CommenceStrollCycleTail2(slices, assembly, wholeMonitor);
    }

    private static void CommenceStrollCycleTail2(List<ClipLensSlice> slices, ClipCycleAssembly assembly, Vector4 wholeMonitor)
    {
        assembly.AssignBeyondLensSlices(assembly.DuplicateSlices(slices));
        assembly.ExteriorSocket = 0;
        CommenceStrollCycleCoda(assembly, wholeMonitor);
    }

    private static void CommenceStrollCycleCoda(ClipCycleAssembly assembly, Vector4 wholeMonitor)
    {
        assembly.ExteriorShown = true;
        assembly.HasBeyondLens = true;
        assembly.BeyondLensNdcAabb = wholeMonitor;
        assembly.BeyondPlaneTally = 0;
        assembly.ScissorBackups = 1;
    }

    public static void ReassembleOutsideViewFromWalk(
        ClipCycleAssembly assembly,
        Stride.StridePortalView beyondLens,
        float viewportWidth,
        float viewRectHeight)
    {
        System.ArgumentNullException.ThrowIfNull(assembly);
        System.ArgumentNullException.ThrowIfNull(beyondLens);
        if (viewportWidth <= 0f || viewRectHeight <= 0f)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(viewportWidth),
                $"viewport {viewportWidth}x{viewRectHeight} - the walk projected its "
                + "views through a real viewport; a non-positive extent here means the "
                + "caller handed a different frame's context (fail-loud rule)");
        }

        ClipCycle cycle = assembly.Frame;
        int lensTally = beyondLens.ViewCount;
        var polys = beyondLens.View.Polys;
        var reservoir = beyondLens.View.Vertices;
        if (polys.Count < lensTally)
        {
            throw new System.InvalidOperationException(
                $"walk outside_view holds {polys.Count} polys for ViewCount={lensTally} — "
                + "the view set's append bookkeeping desynchronized (fail-loud rule)");
        }

        assembly.YieldBeyondLensSlicesForReassembly();

        var beyondSlicesRoster = assembly.SliceTemp;
        beyondSlicesRoster.Clear();
        int beyondUpperPlaneTally = 0;
        bool beyondHasScissorBackup = false;
        int scissorBackups = assembly.ScissorBackups;
        float unionLowerX = float.MaxValue, unionLowerY = float.MaxValue;
        float unionUpperX = float.MinValue, unionUpperY = float.MinValue;

        for (int v = 0; v < lensTally; ++v)
        {
            var strollPoly = polys[v];
            Vector2[] verts = new Vector2[strollPoly.VertexCount];
            for (int kdx = 0; kdx < strollPoly.VertexCount; ++kdx)
            {
                Vector2 px = reservoir[strollPoly.VertexIndex + kdx].Point;
                verts[kdx] = new Vector2(
                    px.X / viewportWidth * 2f - 1f,
                    1f - px.Y / viewRectHeight * 2f);
            }
            LensPolyg poly = new LensPolyg(verts);
            if (!poly.IsEmpty)
            {
                if (poly.LowerX < unionLowerX) unionLowerX = poly.LowerX;
                if (poly.LowerY < unionLowerY) unionLowerY = poly.LowerY;
                if (poly.UpperX > unionUpperX) unionUpperX = poly.UpperX;
                if (poly.UpperY > unionUpperY) unionUpperY = poly.UpperY;
            }

            bool appended = AffixBeyondSlice(
                cycle,
                poly,
                beyondSlicesRoster,
                ref beyondUpperPlaneTally,
                ref beyondHasScissorBackup,
                ref scissorBackups);

            if (!appended)
            {
                beyondSlicesRoster.Add(
                    new ClipLensSlice(0, default, [], NothingVisible: true));
            }
        }

        var beyondLensSlices = assembly.DuplicateSlices(beyondSlicesRoster);
        bool exteriorShown = beyondLensSlices.Length > 0;
        int exteriorSocket = exteriorShown ? beyondLensSlices[0].Slot : 0;

        Vector4 beyondLensNdcAabb = exteriorShown
            ? new Vector4(unionLowerX, unionLowerY, unionUpperX, unionUpperY)
            : Vector4.Zero;

        assembly.AssignBeyondLensSlices(beyondLensSlices);
        assembly.ExteriorSocket = exteriorSocket;
        ReassembleOutsideViewFromWalkRest(assembly, beyondUpperPlaneTally, beyondHasScissorBackup, scissorBackups, exteriorShown, beyondLensNdcAabb);
    }

    private static void ReassembleOutsideViewFromWalkRest(ClipCycleAssembly assembly, int beyondUpperPlaneTally, bool beyondHasScissorBackup, int scissorBackups, bool exteriorShown, Vector4 beyondLensNdcAabb)
    {
        assembly.ExteriorShown = exteriorShown;
        assembly.HasBeyondLens = exteriorShown;
        assembly.BeyondLensNdcAabb = beyondLensNdcAabb;
        assembly.BeyondPlaneTally = beyondHasScissorBackup ? 0 : beyondUpperPlaneTally;
        assembly.ScissorBackups = scissorBackups;
    }

    private static bool AffixBeyondSlice(
        ClipCycle cycle,
        in LensPolyg poly,
        List<ClipLensSlice> beyondSlicesRoster,
        ref int upperPlaneTally,
        ref bool hasScissorBackup,
        ref int scissorBackups)
    {
        ClipFacetGroup cps = ClipFacetGroup.From(poly);
        if (cps.IsNothingShown)
            return false;

        int socket;
        Vector4[] planes;
        if (cps.Count > 0)
        {
            planes = cps.PlaneArr;
            socket = cycle.AppendSlot(planes);
            if (cps.Count > upperPlaneTally)
                upperPlaneTally = cps.Count;
        }
        else
        {
            planes = [];
            socket = 0;
            hasScissorBackup = true;
            ++scissorBackups;
        }

        beyondSlicesRoster.Add(new ClipLensSlice(socket, AabbOf(poly), planes));
        return true;
    }

    private static Vector4 AabbOf(LensPolyg poly) =>
        new(poly.LowerX, poly.LowerY, poly.UpperX, poly.UpperY);

}
