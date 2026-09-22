
namespace MacAC.Client.Graphics.Tenancy;

internal sealed partial class TenancyKeeper
{
    public TenancyAllowanceKnobs Budgets { get; } = budgets ?? TenancyAllowanceKnobs.Default;

    public int EngagedAssetTally => _assetByTag.Count;

    public int EngagedHolderTally => _holders.Count(holder => holder is not null);

    public void EnrollDomainSrc(ITenancyDomainSource src)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (_domainSrcs.Any(contender => contender.Domain == src.Domain))
        {
            throw new InvalidOperationException(
                $"Residency domain {src.Domain} is by now registered");
        }
        _domainSrcs.Add(src);
    }

    public HolderTicket BuildHolder(
        TenancyOwnerKind sort,
        ulong logicalIdent,
        ulong realmGen)
    {
        uint ordinal = ReserveOrdinal(_holders, _holderGenerations, _releaseHolders);
        ushort gen = UpcomingGen(_holderGenerations, ordinal);
        _holders[checked((int)ordinal)] = new HolderSocket
        {
            Generation = gen,
            Kind = sort,
            LogicalIdent = logicalIdent,
            RealmGeneration = realmGen,
        };
        return new HolderTicket(ordinal, gen);
    }

    public bool RetireHolder(HolderTicket holder)
    {
        if (!TryFetchHolder(holder, out HolderSocket socket))
            return false;

        AssetRef[] holdings = [.. socket.Assets];
        for (int idx = 0; idx < holdings.Length; ++idx)
        {
            if (TryFetchAsset(holdings[idx], out AssetSocket asset))
                asset.Holders.Remove(holder);
        }

        _holders[checked((int)holder.Index)] = null;
        _releaseHolders.Push(holder.Index);
        return true;
    }

    public bool CompleteRetirement(AssetRef asset)
    {
        if (!TryFetchAsset(asset, out AssetSocket socket))
            return false;
        if (socket.State != AssetTenancyPhase.Retiring)
        {
            throw new InvalidOperationException(
                $"Only a retiring asset can become absent; current={socket.State}.");
        }
        if (socket.Holders.Count is not 0)
            throw new InvalidOperationException(
                "A retiring asset retained logical owners");

        RecycleAsset(asset.Index, socket);
        return true;
    }

    public int Drain(TenancyObservationDiary journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _observationTemp.Clear();
        journal.BleedTo(_observationTemp);
        _observationTemp.Sort(static (left, right) =>
        {
            int byOrdinal = left.Asset.Index.CompareTo(right.Asset.Index);
            return byOrdinal is not 0 ? byOrdinal : left.Kind.CompareTo(right.Kind);
        });

        int imposed = 0;
        for (int idx = 0; idx < _observationTemp.Count; ++idx)
        {
            var observation = _observationTemp[idx];
            bool approved = observation.Kind switch
            {
                TenancyObservationKind.Touch =>
                    Touch(
                        observation.Asset,
                        observation.Priority,
                        observation.Frame),
                TenancyObservationKind.Transition =>
                    Transition(
                        observation.Asset,
                        observation.State,
                        observation.Charges,
                        observation.Frame,
                        observation.Failure),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(observation),
                    observation.Kind,
                    "unrecognized residency observation kind"),
            };
            if (approved)
                ++imposed;
        }
        return imposed;
    }

    public IReadOnlyList<TenancyTrimAsk> PickEvictions(
        TenancyDomain domain,
        long octetsToFree,
        int ceilingTally)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(octetsToFree);
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingTally);
        if (octetsToFree is 0 || ceilingTally is 0)
            return Array.Empty<TenancyTrimAsk>();

        List<(uint Index, AssetSocket Slot)> contenders = [];
        for (uint ordinal = 1; ordinal < _holdings.Count; ++ordinal)
        {
            AssetSocket? socket = _holdings[checked((int)ordinal)];
            if (socket is null
                || socket.Key.Domain != domain
                || socket.Holders.Count is not 0
                || socket.State is not (AssetTenancyPhase.Resident
                    or AssetTenancyPhase.Prepared))

                continue;
            contenders.Add((ordinal, socket));
        }

        contenders.Sort(static (left, right) =>
        {
            int ordering = left.Slot.Priority.CompareTo(right.Slot.Priority);
            if (ordering is not 0)
                return ordering;
            ordering = left.Slot.PreviousUsedFrame.CompareTo(right.Slot.PreviousUsedFrame);
            if (ordering is not 0)
                return ordering;
            ordering = left.Slot.ReassemblePrice.CompareTo(right.Slot.ReassemblePrice);
            return ordering is not 0
                ? ordering
                : left.Index.CompareTo(right.Index);
        });

        List<TenancyTrimAsk> reqs = [];
        long chosenOctets = 0;
        for (int idx = 0;
             idx < contenders.Count
                && reqs.Count < ceilingTally
                && chosenOctets < octetsToFree;
             ++idx)
        {
            (uint ordinal, AssetSocket socket) = contenders[idx];
            var sunsetting = ToSunsettingCharges(socket.Charges);
            AssetRef asset = new AssetRef(
                ordinal,
                socket.Generation,
                socket.AssetKind);
            Transition(
                asset,
                AssetTenancyPhase.Retiring,
                sunsetting,
                socket.PreviousUsedFrame);
            reqs.Add(new TenancyTrimAsk(asset, socket.Key, sunsetting));
            chosenOctets = checked(
                chosenOctets + Math.Max(
                    1,
                    sunsetting.RetiringBytes
                    + sunsetting.CommittedCpuBytes));
        }
        return reqs;
    }

    public TenancyCapture SnapCapture()
    {
        _captureTemp.Clear();
        Span<bool> sourced = stackalloc bool[
            Enum.GetValues<TenancyDomain>().Length];
        TenancyCharges sums = default;
        for (int idx = 0; idx < _domainSrcs.Count; ++idx)
        {
            var capture =
                _domainSrcs[idx].GrabResidency();
            _captureTemp.Add(capture);
            sourced[(int)capture.Domain] = true;
            sums += capture.Charges;
        }

        TenancyCharges[] logicalCharges =
            new TenancyCharges[sourced.Length];
        int[] logicalListings = new int[sourced.Length];
        int[] logicalHolders = new int[sourced.Length];
        for (uint ordinal = 1; ordinal < _holdings.Count; ++ordinal)
        {
            AssetSocket? socket = _holdings[checked((int)ordinal)];
            if (socket is null || sourced[(int)socket.Key.Domain])
                continue;
            int domain = (int)socket.Key.Domain;
            logicalCharges[domain] += socket.Charges;
            logicalListings[domain]++;
            logicalHolders[domain] = checked(
                logicalHolders[domain] + socket.Holders.Count);
        }

        for (int domain = 0; domain < logicalListings.Length; ++domain)
        {
            if (logicalListings[domain] is 0)
                continue;
            var charges = logicalCharges[domain];
            _captureTemp.Add(new TenancyDomainCapture(
                (TenancyDomain)domain,
                logicalListings[domain],
                logicalHolders[domain],
                charges));
            sums += charges;
        }

        return new TenancyCapture(_captureTemp.ToArray(), sums);
    }

    private bool Transition(
        AssetRef asset,
        AssetTenancyPhase upcoming,
        TenancyCharges charges,
        long cycle,
        string? miss = null)
    {
        if (!TryFetchAsset(asset, out AssetSocket socket))
            return false;
        ArgumentOutOfRangeException.ThrowIfNegative(cycle);
        charges.Validate();
        VetChangeover(socket.State, upcoming);
        VetCharges(upcoming, charges);
        if (upcoming == AssetTenancyPhase.Retiring && socket.Holders.Count is not 0)
        {
            throw new InvalidOperationException(
                "An owned asset can't enter retiring state");
        }

        socket.State = upcoming;
        socket.Charges = charges;
        socket.PreviousUsedFrame = Math.Max(socket.PreviousUsedFrame, cycle);
        socket.Failure = upcoming is AssetTenancyPhase.Corrupt
            or AssetTenancyPhase.Failed
            ? miss
            : null;
        if (IsTerminal(upcoming))
            UnfastenAllHolders(asset, socket);
        return true;
    }

    private bool Touch(
        AssetRef asset,
        TenancyPriority precedence,
        long cycle)
    {
        if (!TryFetchAsset(asset, out AssetSocket socket))
            return false;
        socket.Priority = Max(socket.Priority, precedence);
        socket.PreviousUsedFrame = Math.Max(socket.PreviousUsedFrame, cycle);
        return true;
    }

    private bool TryFetchCapture(
        AssetRef asset,
        out TenancyEntryCapture capture)
    {
        if (!TryFetchAsset(asset, out AssetSocket socket))
        {
            capture = default;
            return false;
        }

        capture = new TenancyEntryCapture(
            asset,
            socket.Key,
            socket.State,
            socket.Priority,
            socket.RealmGen,
            socket.PreviousUsedFrame,
            socket.ReassemblePrice,
            socket.Holders.Count,
            socket.Charges,
            socket.Failure);
        return true;
    }

    private bool TryFetchAsset(
        AssetRef asset,
        out AssetSocket socket)
    {
        socket = null!;
        if (!asset.IsValid || asset.Index >= _holdings.Count)
            return false;
        AssetSocket? contender = _holdings[checked((int)asset.Index)];
        if (contender is null
            || contender.Generation != asset.Generation
            || !contender.AssetKind.Equals(asset.AssetType))

            return false;
        socket = contender;
        return true;
    }

    private bool TryFetchHolder(HolderTicket holder, out HolderSocket socket)
    {
        socket = null!;
        if (!holder.IsValid || holder.Index >= _holders.Count)
            return false;
        HolderSocket? contender = _holders[checked((int)holder.Index)];
        if (contender is null || contender.Generation != holder.Generation)
            return false;
        socket = contender;
        return true;
    }

    private HolderSocket FetchHolder(HolderTicket holder)
    {
        return TryFetchHolder(holder, out HolderSocket socket)
            ? socket
            : throw new InvalidOperationException("Owner token is stale or not valid");
    }

    private void RecycleAsset(uint ordinal, AssetSocket socket)
    {
        if (socket.Holders.Count is not 0)
            throw new InvalidOperationException(
                "An asset can't be recycled while owners remain");
        _assetByTag.Remove(new TypedAssetTag(socket.AssetKind, socket.Key));
        _holdings[checked((int)ordinal)] = null;
        _releaseHoldings.Push(ordinal);
    }

    private void UnfastenAllHolders(AssetRef asset, AssetSocket socket)
    {
        if (socket.Holders.Count is 0)
            return;
        HolderTicket[] holders = [.. socket.Holders];
        for (int idx = 0; idx < holders.Length; ++idx)
        {
            if (TryFetchHolder(holders[idx], out HolderSocket holder))
                holder.Assets.Remove(asset);
        }
        socket.Holders.Clear();
    }

    private static uint ReserveOrdinal<T>(
        List<T?> sockets,
        List<ushort> generations,
        Stack<uint> release)
        where T : class
    {
        if (release.TryPop(out uint reused))
            return reused;
        uint ordinal = checked((uint)sockets.Count);
        sockets.Add(null);
        generations.Add(0);
        return ordinal;
    }

    private static TenancyCharges ToSunsettingCharges(
        TenancyCharges latest)
    {
        return new(
            LogicalBytes: latest.LogicalBytes,
            CpuPreparedBytes: latest.CpuPreparedBytes,
            DecodedBytes: latest.DecodedBytes,
            ScratchBytes: latest.ScratchBytes,
            PinnedBytes: latest.PinnedBytes,
            RetiringBytes: checked(
                latest.GpuRequestedBytes
                + latest.GpuResidentBytes
                + latest.RetiringBytes));
    }

    private static ushort UpcomingGen(
        List<ushort> generations,
        uint ordinal)
    {
        ushort gen = unchecked(
            (ushort)(generations[checked((int)ordinal)] + 1));
        if (gen is 0)
            gen = 1;
        generations[checked((int)ordinal)] = gen;
        return gen;
    }

    private static TenancyPriority Max(
        TenancyPriority left,
        TenancyPriority right) =>
        left >= right ? left : right;

    private static bool IsTerminal(AssetTenancyPhase phase)
    {
        return phase is AssetTenancyPhase.Cancelled
            or AssetTenancyPhase.Missing
            or AssetTenancyPhase.Corrupt
            or AssetTenancyPhase.Failed;
    }

    private static void VetChangeover(
        AssetTenancyPhase latest,
        AssetTenancyPhase upcoming)
    {
        bool legal = latest switch
        {
            AssetTenancyPhase.Requested =>
                upcoming is AssetTenancyPhase.Prepared
                    or AssetTenancyPhase.Cancelled
                    or AssetTenancyPhase.Missing
                    or AssetTenancyPhase.Corrupt
                    or AssetTenancyPhase.Failed
                    or AssetTenancyPhase.Retiring,
            AssetTenancyPhase.Prepared =>
                upcoming is AssetTenancyPhase.UploadPending
                    or AssetTenancyPhase.Retiring,
            AssetTenancyPhase.UploadPending =>
                upcoming is AssetTenancyPhase.Resident
                    or AssetTenancyPhase.Prepared
                    or AssetTenancyPhase.Retiring
                    or AssetTenancyPhase.Cancelled
                    or AssetTenancyPhase.Corrupt
                    or AssetTenancyPhase.Failed,
            AssetTenancyPhase.Resident =>
                upcoming is AssetTenancyPhase.Retiring,
            _ => false,
        };
        if (!legal)
        {
            throw new InvalidOperationException(
                $"Illegal residency transition {latest} -> {upcoming}.");
        }
    }

    private static void VetCharges(
        AssetTenancyPhase phase,
        TenancyCharges charges)
    {
        if (phase is AssetTenancyPhase.Cancelled
            or AssetTenancyPhase.Missing
            or AssetTenancyPhase.Corrupt
            or AssetTenancyPhase.Failed)
        {
            if (!charges.IsZero)
            {
                throw new InvalidOperationException(
                    $"Terminal state {phase} can't retain residency charges");
            }
            return;
        }

        if (phase == AssetTenancyPhase.Retiring
            && (charges.GpuRequestedBytes is not 0
                || charges.GpuResidentBytes is not 0
                || charges.StagingBytes is not 0))
        {
            throw new InvalidOperationException(
                "Retiring charges must move transient and resident GPU bytes into RetiringBytes");
        }
    }
}
