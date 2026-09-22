namespace MacAC.Client.Graphics.Tenancy;

internal sealed partial class TenancyKeeper(TenancyAllowanceKnobs? budgets = null)
{
    private sealed class AssetSocket
    {
        public required RuntimeTypeHandle AssetKind { get; init; }
        public required ushort Generation { get; init; }
        public required TenancyAssetKey Key { get; init; }
        public required ulong RealmGen { get; init; }
        public AssetTenancyPhase State { get; set; }
        public TenancyPriority Priority { get; set; }
        public long PreviousUsedFrame { get; set; }
        public long ReassemblePrice { get; set; }
        public TenancyCharges Charges { get; set; }
        public string? Failure { get; set; }
        public HashSet<HolderTicket> Holders { get; } = [];
    }

    private sealed class HolderSocket
    {
        public required ushort Generation { get; init; }
        public required TenancyOwnerKind Kind { get; init; }
        public required ulong LogicalIdent { get; init; }
        public required ulong RealmGeneration { get; init; }
        public HashSet<AssetRef> Assets { get; } = [];
    }

    private readonly record struct TypedAssetTag(
        RuntimeTypeHandle AssetType,
        TenancyAssetKey Key);

    private readonly List<AssetSocket?> _holdings = [null];

    private readonly List<ushort> _assetGenerations = [0];

    private readonly Stack<uint> _releaseHoldings = [];

    private readonly Dictionary<TypedAssetTag, uint> _assetByTag = [];

    private readonly List<HolderSocket?> _holders = [null];

    private readonly List<ushort> _holderGenerations = [0];

    private readonly Stack<uint> _releaseHolders = [];

    private readonly List<ITenancyDomainSource> _domainSrcs = [];

    private readonly List<TenancyObservation> _observationTemp = [];

    private readonly List<TenancyDomainCapture> _captureTemp = [];

    public AssetHnd<TAsset> Request<TAsset>(
        TenancyAssetKey tag,
        TenancyPriority precedence,
        ulong realmGen,
        long cycle,
        long reassemblePrice = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycle);
        ArgumentOutOfRangeException.ThrowIfNegative(reassemblePrice);
        var kind = typeof(TAsset).TypeHandle;
        TypedAssetTag typedTag = new TypedAssetTag(kind, tag);
        if (_assetByTag.TryGetValue(typedTag, out uint extantOrdinal))
        {
            AssetSocket extant = _holdings[checked((int)extantOrdinal)]
                ?? throw new InvalidOperationException(
                    "Residency key index didn't resolve to a live slot");
            if (IsTerminal(extant.State))
            {
                RecycleAsset(extantOrdinal, extant);
            }
            else
            {
                extant.Priority = Max(extant.Priority, precedence);
                extant.PreviousUsedFrame = Math.Max(extant.PreviousUsedFrame, cycle);
                extant.ReassemblePrice = Math.Max(extant.ReassemblePrice, reassemblePrice);
                return new AssetHnd<TAsset>(
                    extantOrdinal,
                    extant.Generation);
            }
        }

        uint ordinal = ReserveOrdinal(_holdings, _assetGenerations, _releaseHoldings);
        ushort gen = UpcomingGen(_assetGenerations, ordinal);
        AssetSocket socket = new AssetSocket
        {
            AssetKind = kind,
            Generation = gen,
            Key = tag,
            RealmGen = realmGen,
            State = AssetTenancyPhase.Requested,
            Priority = precedence,
            PreviousUsedFrame = cycle,
            ReassemblePrice = reassemblePrice,
        };
        _holdings[checked((int)ordinal)] = socket;
        _assetByTag.Add(typedTag, ordinal);
        return new AssetHnd<TAsset>(ordinal, gen);
    }

    public AssetTenancy<TAsset> Acquire<TAsset>(
        AssetHnd<TAsset> hnd,
        HolderTicket holder,
        TenancyPriority precedence,
        long cycle)
    {
        AssetSocket asset = FetchAsset(hnd);
        HolderSocket holderSocket = FetchHolder(holder);
        if (asset.State is AssetTenancyPhase.Retiring
            or AssetTenancyPhase.Cancelled
            or AssetTenancyPhase.Missing
            or AssetTenancyPhase.Corrupt
            or AssetTenancyPhase.Failed)
        {
            throw new InvalidOperationException(
                $"Can't acquire an asset in {asset.State} state.");
        }
        if (holderSocket.RealmGeneration != asset.RealmGen
            && holderSocket.Kind is not TenancyOwnerKind.Session
                and not TenancyOwnerKind.UserInterface
                and not TenancyOwnerKind.Tooling)
        {
            throw new InvalidOperationException(
                "A world-scoped owner can't acquire an asset from another generation");
        }

        AssetTenancy<TAsset> tenancy = new AssetTenancy<TAsset>(hnd, holder);
        if (asset.Holders.Add(holder))
            holderSocket.Assets.Add(hnd.Untyped);
        asset.Priority = Max(asset.Priority, precedence);
        asset.PreviousUsedFrame = Math.Max(asset.PreviousUsedFrame, cycle);
        return tenancy;
    }

    public bool Release<TAsset>(AssetTenancy<TAsset> tenancy)
    {
        if (!TryFetchAsset(tenancy.Handle.Untyped, out AssetSocket asset)
            || !TryFetchHolder(tenancy.Owner, out HolderSocket holder))

            return false;

        bool removed = asset.Holders.Remove(tenancy.Owner);
        if (removed)
        {
            holder.Assets.Remove(tenancy.Handle.Untyped);
            if (asset.Holders.Count is 0)
                asset.Priority = TenancyPriority.Speculative;
        }
        return removed;
    }

    public bool Touch<TAsset>(
        AssetHnd<TAsset> hnd,
        TenancyPriority precedence,
        long cycle)
    {
        if (!TryFetchAsset(hnd.Untyped, out AssetSocket asset))
            return false;
        asset.Priority = Max(asset.Priority, precedence);
        asset.PreviousUsedFrame = Math.Max(asset.PreviousUsedFrame, cycle);
        return true;
    }

    public bool Transition<TAsset>(
        AssetHnd<TAsset> hnd,
        AssetTenancyPhase upcoming,
        TenancyCharges charges,
        long cycle,
        string? miss = null) =>
        Transition(hnd.Untyped, upcoming, charges, cycle, miss);

    public bool CompleteRetirement<TAsset>(AssetHnd<TAsset> hnd) =>
        CompleteRetirement(hnd.Untyped);

    public bool TryFetchSnapshot<TAsset>(
        AssetHnd<TAsset> hnd,
        out TenancyEntryCapture capture) =>
        TryFetchCapture(hnd.Untyped, out capture);

    private AssetSocket FetchAsset<TAsset>(AssetHnd<TAsset> hnd)
    {
        return TryFetchAsset(hnd.Untyped, out AssetSocket socket)
            ? socket
            : throw new InvalidOperationException("Asset handle is stale or not valid");
    }
}
