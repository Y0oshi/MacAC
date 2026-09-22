namespace MacAC.Client.Paging;

public sealed class PagingRegion
{
    public int CenterX { get; private set; }
    public int CenterY { get; private set; }
    public int Radius { get; }
    public int NearRadius { get; }
    public int FarRadius { get; }

    private readonly HashSet<uint> _shown = [];

    private readonly HashSet<uint> _resident = [];

    private readonly Dictionary<uint, ClientTierResidence> _tierResidence = [];

    private bool _bootstrapped;

    public IReadOnlyCollection<uint> Visible => _shown;

    public IReadOnlyCollection<uint> Resident => _resident;

    public PagingRegion(int middleX, int middleY, int nearbyRadius, int farawayRadius)
    {
        NearRadius = nearbyRadius;
        FarRadius = farawayRadius;
        Radius = farawayRadius;  // outer ring drives Resident bookkeeping
        Recenter(middleX, middleY);
    }

    public PagingRegion(int cx, int cy, int radius) : this(cx, cy, radius, radius) { }

    public ClientTwoTierDiff ComputeFirstTickDiff()
    {
        List<uint> nearby = new List<uint>();
        List<uint> faraway = new List<uint>();
        for (int dx = -FarRadius; dx <= FarRadius; ++dx)
        {
            for (int dy = -FarRadius; dy <= FarRadius; ++dy)
            {
                int nx = CenterX + dx;
                int ny = CenterY + dy;
                if (nx < 0 || nx > 0xFF || ny < 0 || ny > 0xFF) continue;
                int absDx = System.Math.Abs(dx);
                int absDy = System.Math.Abs(dy);
                uint ident = PackLbIdent(nx, ny);
                if (absDx <= NearRadius && absDy <= NearRadius)
                    nearby.Add(ident);
                else
                    faraway.Add(ident);
            }
        }
        return new ClientTwoTierDiff(
            ToLoadFar: faraway,
            ToLoadNear: nearby,
            ToPromote: System.Array.Empty<uint>(),
            ToDemote: System.Array.Empty<uint>(),
            ToUnload: System.Array.Empty<uint>());
    }

    public void MarkResidentFromBootstrap()
    {
        if (_bootstrapped)
            throw new InvalidOperationException(
                "MarkResidentFromBootstrap was by now called; calling it again would " +
                "reset accumulated tier-residence state and silently drop differential " +
                "data built up by interim RecenterTo calls");

        _tierResidence.Clear();
        for (int dx = -FarRadius; dx <= FarRadius; ++dx)
        {
            for (int dy = -FarRadius; dy <= FarRadius; ++dy)
            {
                int nx = CenterX + dx;
                int ny = CenterY + dy;
                if (nx < 0 || nx > 0xFF || ny < 0 || ny > 0xFF) continue;
                int absDx = Math.Abs(dx);
                int absDy = Math.Abs(dy);
                uint ident = PackLbIdent(nx, ny);
                _tierResidence[ident] = (absDx <= NearRadius && absDy <= NearRadius)
                    ? ClientTierResidence.Near
                    : ClientTierResidence.Far;
            }
        }
        _bootstrapped = true;
    }

    public ClientTwoTierDiff RecenterTo(int newCx, int newCy)
    {
        if (!_bootstrapped)
            throw new InvalidOperationException(
                "Two-tier RecenterTo called prior to MarkResidentFromBootstrap. " +
                "First call ComputeFirstTickDiff to enqueue the bootstrap loads, " +
                "then MarkResidentFromBootstrap to seed _tierResidence, then RecenterTo " +
                "for subsequent observer moves");

        int nearbyUnloadThreshold = NearRadius + 2;
        int farawayUnloadThreshold = FarRadius + 2;

        List<uint> toPullFaraway = new List<uint>();
        List<uint> toPullNearby = new List<uint>();
        List<uint> toPromote = new List<uint>();
        List<uint> toDemote = new List<uint>();
        List<uint> toUnload = new List<uint>();

        // Pass 1: walk new far window - emit ToLoadFar / ToLoadNear / ToPromote.
        HashSet<uint> newMiddleIdents = new HashSet<uint>();
        for (int dx = -FarRadius; dx <= FarRadius; ++dx)
        {
            for (int dy = -FarRadius; dy <= FarRadius; ++dy)
            {
                int nx = newCx + dx;
                int ny = newCy + dy;
                if (nx < 0 || nx > 0xFF || ny < 0 || ny > 0xFF) continue;
                int absDx = Math.Abs(dx);
                int absDy = Math.Abs(dy);
                bool inNearby = absDx <= NearRadius && absDy <= NearRadius;
                uint ident = PackLbIdent(nx, ny);
                newMiddleIdents.Add(ident);

                if (!_tierResidence.TryGetValue(ident, out var latest))
                {
                    // Not resident at all - fresh load.
                    if (inNearby) toPullNearby.Add(ident);
                    else toPullFaraway.Add(ident);
                    _tierResidence[ident] = inNearby ? ClientTierResidence.Near : ClientTierResidence.Far;
                }
                else if (latest == ClientTierResidence.Far && inNearby)
                {
                    // Was Far, now inside Near ring - promote
                    toPromote.Add(ident);
                    _tierResidence[ident] = ClientTierResidence.Near;
                }
                // Near→Near and Far→Far are no-ops
            }
        }

        foreach (var kvp in _tierResidence.ToArray())
        {
            uint ident = kvp.Key;
            ClientTierResidence latest = kvp.Value;
            int lbX = (int)((ident >> 24) & 0xFFu);
            int lbY = (int)((ident >> 16) & 0xFFu);
            int absDx = Math.Abs(lbX - newCx);
            int absDy = Math.Abs(lbY - newCy);
            int gap = Math.Max(absDx, absDy);

            if (newMiddleIdents.Contains(ident))
            {
                // Still in the far window - only Near→Far demote possible here.
                if (latest == ClientTierResidence.Near && (absDx > NearRadius || absDy > NearRadius) && gap > nearbyUnloadThreshold)
                {
                    toDemote.Add(ident);
                    _tierResidence[ident] = ClientTierResidence.Far;
                }
                continue;
            }

            // Outside the new window - demote / unload by threshold
            if (latest == ClientTierResidence.Near)
            {
                if (gap > nearbyUnloadThreshold)
                {
                    toDemote.Add(ident);
                    _tierResidence[ident] = ClientTierResidence.Far;
                    if (gap > farawayUnloadThreshold)
                    {
                        toUnload.Add(ident);
                        _tierResidence.Remove(ident);
                    }
                }
            }
            else if (latest == ClientTierResidence.Far && gap > farawayUnloadThreshold)
            {
                toUnload.Add(ident);
                _tierResidence.Remove(ident);
            }
        }

        CenterX = newCx;
        CenterY = newCy;

        return new ClientTwoTierDiff(toPullFaraway, toPullNearby, toPromote, toDemote, toUnload);
    }

    public ZoneDiff RecenterToSingleTier(int newCx, int newCy)
    {
        // Snapshot the old resident set so we can diff against it
        HashSet<uint> formerHoused = new HashSet<uint>(_resident);

        Recenter(newCx, newCy);

        // Loads = entries in the new window not yet in the resident set
        List<uint> toPull = new List<uint>();
        foreach (var ident in _shown)
            if (!formerHoused.Contains(ident))
                toPull.Add(ident);

        int unloadThreshold = Radius + 2;
        List<uint> toUnload = new List<uint>();
        foreach (var ident in formerHoused)
        {
            if (_shown.Contains(ident)) continue;  // still in window, keep
            int lbX = (int)((ident >> 24) & 0xFFu);
            int lbY = (int)((ident >> 16) & 0xFFu);
            int dx = Math.Abs(lbX - newCx);
            int dy = Math.Abs(lbY - newCy);
            if (dx > unloadThreshold || dy > unloadThreshold)
                toUnload.Add(ident);
        }

        // Update resident: (oldResident ∪ newVisible) ∖ toUnload
        _resident.UnionWith(_shown);
        foreach (var ident in toUnload)
            _resident.Remove(ident);

        return new ZoneDiff(toPull, toUnload);
    }

    internal static uint PackLbIdent(int lbX, int lbY)
        => ((uint)lbX << 24) | ((uint)lbY << 16) | 0xFFFFu;

    internal static uint PackLbIdentForTest(int lbX, int lbY)
        => PackLbIdent(lbX, lbY);

    internal bool TryFetchWantedTier(uint lbIdent, out LandblockFlowTier tier)
    {
        if (_tierResidence.TryGetValue(lbIdent, out var residence))
        {
            tier = residence == ClientTierResidence.Near
                ? LandblockFlowTier.Near
                : LandblockFlowTier.Far;
            return true;
        }

        tier = default;
        return false;
    }

    private void Recenter(int cx, int cy)
    {
        CenterX = cx;
        CenterY = cy;
        _shown.Clear();
        for (int dx = -Radius; dx <= Radius; ++dx)
        {
            for (int dy = -Radius; dy <= Radius; ++dy)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || nx > 0xFF || ny < 0 || ny > 0xFF)
                    continue;
                _shown.Add(PackLbIdent(nx, ny));
            }
        }
        _resident.UnionWith(_shown);
    }
}

public readonly record struct ZoneDiff(
    IReadOnlyList<uint> ToLoad,
    IReadOnlyList<uint> ToUnload);

internal enum ClientTierResidence { Far, Near }
