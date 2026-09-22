using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class ProxyRegistry
{
    internal void SealSetLocus(
        uint actorIdent,
        Vector3 realmLocus,
        Quaternion realmSpin,
        uint seedChamberIdent,
        float realmShiftX,
        float realmShiftY,
        ProxyCommitAction action,
        System.Collections.Immutable.ImmutableArray<uint> crossChamberIdents)
    {
        if (!_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment))

            return;

        switch (action)
        {
            case ProxyCommitAction.None:
                RewriteLocusRanks(
                    actorIdent,
                    enrollment,
                    realmLocus,
                    realmSpin,
                    seedChamberIdent);
                return;
            case ProxyCommitAction.Recalculate:
                RefreshLocus(
                    actorIdent,
                    realmLocus,
                    realmSpin,
                    realmShiftX,
                    realmShiftY,
                    lbIdent: seedChamberIdent & 0xFFFF0000u,
                    seedChamberIdent);
                return;
            case ProxyCommitAction.Replace:
                if (crossChamberIdents.IsDefaultOrEmpty)
                {
                    RewriteLocusRanks(
                        actorIdent,
                        enrollment,
                        realmLocus,
                        realmSpin,
                        seedChamberIdent);
                    return;
                }
                SwapLocusRanks(
                    actorIdent,
                    enrollment,
                    realmLocus,
                    realmSpin,
                    seedChamberIdent,
                    crossChamberIdents);
                return;
            case ProxyCommitAction.Preserve:
                RewriteLocusRanks(
                    actorIdent,
                    enrollment,
                    realmLocus,
                    realmSpin,
                    seedChamberIdent);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    internal sealed record BakedPlaceProxyCommit(
        ulong CommitId,
        uint EntityId,
        ulong ExpectedMutationRevision,
        ulong ExpectedOwnerVersion,
        ulong FinalMutationRevision,
        ulong FinalOwnerVersion,
        bool ProvenShapeless,
        BakedProxyOwnerState? HolderLedger,
        BakedProxyCellSwap[] CellReplacements,
        BakedProxyPrefixSwap[] PrefixReplacements,
        HashSet<uint>? OwnerPrefixes,
        uint[] ChangedPrefixes,
        BakedProxyCanonCellSwap[] RetailCellReplacements);

    internal sealed record BakedProxyCellSwap(
        uint CellId,
        List<ProxyEntry> Entries);

    internal sealed record BakedProxyCanonCellSwap(
        uint CellId,
        List<CanonPartRow> Entries);

    internal sealed record BakedProxyPrefixSwap(
        uint Prefix,
        bool Remove,
        List<uint>? Slots,
        Dictionary<uint, int>? Indices,
        Stack<int>? FreeSlots);

    internal readonly record struct PlaceProxyCommitReceipt(
        ulong CommitId,
        uint EntityId,
        ulong OwnerVersion,
        uint[] ChangedPrefixes,
        bool Mutated)
    {
        internal bool IsValid => CommitId is not 0UL && EntityId is not 0u;
    }

    internal bool TryReadySetLocus(
        uint actorIdent,
        Vector3 realmLocus,
        Quaternion realmSpin,
        uint seedChamberIdent,
        float realmShiftX,
        float realmShiftY,
        ProxyCommitAction act,
        System.Collections.Immutable.ImmutableArray<uint> crossChamberIdents,
        bool provenShapeless,
        bool suspendHolder,
        out BakedPlaceProxyCommit? readied)
    {
        readied = null;
        ulong anticipatedAlteration = _alterationRev;
        ulong anticipatedHolder = FetchHolderVer(actorIdent);
        bool hasHolder = TryBakeHolder(
            actorIdent,
            out BakedProxyOwnerState? src);
        if (!hasHolder)
        {
            if (!provenShapeless)
                return false;
            _queuedSetLocusDispatches.EnsureCapacity(
                _queuedSetLocusDispatches.Count + 1);
            readied = new BakedPlaceProxyCommit(
                checked(++_upcomingReadiedSetLocusSealIdent),
                actorIdent,
                anticipatedAlteration,
                anticipatedHolder,
                anticipatedAlteration,
                anticipatedHolder,
                ProvenShapeless: true,
                HolderLedger: null,
                CellReplacements: [],
                PrefixReplacements: [],
                OwnerPrefixes: null,
                ChangedPrefixes: Array.Empty<uint>(),
                RetailCellReplacements: []);
            return _alterationRev == anticipatedAlteration
                && FetchHolderVer(actorIdent) == anticipatedHolder
                && !HasLogicalHolder(actorIdent);
        }
        if (provenShapeless || src is null)
            return false;

        ProxyRegistry loading = new ProxyRegistry
        {
            DataCache = DataCache,
        };
        loading.SetupBakedHolder(src);
        loading.SealSetLocus(
            actorIdent,
            realmLocus,
            realmSpin,
            seedChamberIdent,
            realmShiftX,
            realmShiftY,
            act,
            crossChamberIdents);
        if (suspendHolder && !loading.Suspend(actorIdent))
            return false;
        if (!loading.TryBakeHolder(
                actorIdent,
                out BakedProxyOwnerState? substitute)
            || substitute is null)

            return false;

        uint[] alteredStems = GrabAlteredStems(src, substitute);
        var chamberSubstitutes =
            ReadyChamberSubstitutes(actorIdent, src, substitute);
        var canonChamberSubstitutes =
            ReadyCanonPieceListingSubstitutes(actorIdent, src, substitute);
        var substituteStems = GrabStems(substitute);
        var stemSubstitutes =
            ReadyStemSubstitutes(
                actorIdent,
                GrabStems(src),
                substituteStems,
                alteredStems);
        ulong finalAlteration = checked(anticipatedAlteration + 1UL);
        ulong finalHolder = checked(anticipatedHolder + 1UL);
        _ranksByChamber.EnsureCapacity(_ranksByChamber.Count + substitute.Rows.Count);
        _chambersByHolder.EnsureCapacity(_chambersByHolder.Count + 1);
        _enrolments.EnsureCapacity(_enrolments.Count + 1);
        _formsByHolder.EnsureCapacity(_formsByHolder.Count + 1);
        _dormantHolderChambers.EnsureCapacity(_dormantHolderChambers.Count + 1);
        _withdrawnByHolder.EnsureCapacity(
            _withdrawnByHolder.Count + 1);
        _versions.EnsureCapacity(_versions.Count + 1);
        _stemsByHolder.EnsureCapacity(_stemsByHolder.Count + 1);
        _holdersByStem.EnsureCapacity(
            _holdersByStem.Count + alteredStems.Length);
        _holderOrdinalByStem.EnsureCapacity(
            _holderOrdinalByStem.Count + alteredStems.Length);
        _releaseHolderSocketsByStem.EnsureCapacity(
            _releaseHolderSocketsByStem.Count + alteredStems.Length);
        _dormantHolders.EnsureCapacity(_dormantHolders.Count + 1);
        _queuedSetLocusDispatches.EnsureCapacity(
            _queuedSetLocusDispatches.Count + 1);
        _canonPiecesByHolder.EnsureCapacity(_canonPiecesByHolder.Count + 1);
        _canonChambersByHolder.EnsureCapacity(_canonChambersByHolder.Count + 1);
        _canonCoursesByHolder.EnsureCapacity(_canonCoursesByHolder.Count + 1);
        _canonRanksByChamber.EnsureCapacity(
            _canonRanksByChamber.Count + canonChamberSubstitutes.Length);

        readied = new BakedPlaceProxyCommit(
            checked(++_upcomingReadiedSetLocusSealIdent),
            actorIdent,
            anticipatedAlteration,
            anticipatedHolder,
            finalAlteration,
            finalHolder,
            ProvenShapeless: false,
            substitute,
            chamberSubstitutes,
            stemSubstitutes,
            substituteStems,
            alteredStems,
            canonChamberSubstitutes);
        return _alterationRev == anticipatedAlteration
            && FetchHolderVer(actorIdent) == anticipatedHolder
            && HasLogicalHolder(actorIdent);
    }

    internal bool TryEnactSetLocus(
        BakedPlaceProxyCommit readied,
        out PlaceProxyCommitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(readied);
        receipt = default;
        if (readied.CommitId <= _previousImposedSetLocusSealIdent
            || _alterationRev != readied.ExpectedMutationRevision
            || FetchHolderVer(readied.EntityId)
                != readied.ExpectedOwnerVersion
            || HasLogicalHolder(readied.EntityId)
                == readied.ProvenShapeless)

            return false;

        if (readied.ProvenShapeless)
        {
            receipt = new PlaceProxyCommitReceipt(
                readied.CommitId,
                readied.EntityId,
                readied.ExpectedOwnerVersion,
                Array.Empty<uint>(),
                Mutated: false);
            _previousImposedSetLocusSealIdent = readied.CommitId;
            _queuedSetLocusDispatches.Add(readied.CommitId);
            return true;
        }
        if (readied.HolderLedger is null)
            return false;

        for (int ordinal = 0; ordinal < readied.CellReplacements.Length; ++ordinal)
        {
            var substitute =
                readied.CellReplacements[ordinal];
            _ranksByChamber[substitute.CellId] = substitute.Entries;
        }
        for (int ordinal = 0; ordinal < readied.RetailCellReplacements.Length; ++ordinal)
        {
            var substitute =
                readied.RetailCellReplacements[ordinal];
            _canonRanksByChamber[substitute.CellId] = substitute.Entries;
        }
        var phase = readied.HolderLedger;
        _enrolments[readied.EntityId] = phase.Registration;
        ReplaceHolderVal(_formsByHolder, readied.EntityId, phase.Shapes);
        if (phase.Suspended)
            _dormantHolders.Add(readied.EntityId);
        else
            _dormantHolders.Remove(readied.EntityId);
        ReplaceHolderVal(
            _dormantHolderChambers,
            readied.EntityId,
            phase.SuspendedCellIds);
        ReplaceHolderVal(
            _withdrawnByHolder,
            readied.EntityId,
            phase.WithdrawnPrefixes);
        ReplaceHolderVal(
            _chambersByHolder,
            readied.EntityId,
            phase.CellIds);
        if (phase.RetailPartArray is not null)
        {
            _canonPiecesByHolder[readied.EntityId] = phase.RetailPartArray;
            _canonCoursesByHolder[readied.EntityId] = phase.RetailRoute;
        }
        else
        {
            _canonPiecesByHolder.Remove(readied.EntityId);
            _canonCoursesByHolder.Remove(readied.EntityId);
        }
        ReplaceHolderVal(
            _canonChambersByHolder,
            readied.EntityId,
            phase.RetailCellIds);
        if (readied.OwnerPrefixes is not null)
            _stemsByHolder[readied.EntityId] = readied.OwnerPrefixes;
        for (int ordinal = 0; ordinal < readied.PrefixReplacements.Length; ++ordinal)
        {
            var substitute =
                readied.PrefixReplacements[ordinal];
            if (substitute.Remove)
            {
                _holdersByStem.Remove(substitute.Prefix);
                _holderOrdinalByStem.Remove(substitute.Prefix);
                _releaseHolderSocketsByStem.Remove(substitute.Prefix);
                continue;
            }
            _holdersByStem[substitute.Prefix] = substitute.Slots!;
            _holderOrdinalByStem[substitute.Prefix] = substitute.Indices!;
            _releaseHolderSocketsByStem[substitute.Prefix] = substitute.FreeSlots!;
        }
        _alterationRev = readied.FinalMutationRevision;
        _versions[readied.EntityId] = readied.FinalOwnerVersion;
        StampHolder(readied.EntityId);
        _previousImposedSetLocusSealIdent = readied.CommitId;
        _queuedSetLocusDispatches.Add(readied.CommitId);
        RepublishAffixedDescendants(readied.EntityId);
        receipt = new PlaceProxyCommitReceipt(
            readied.CommitId,
            readied.EntityId,
            readied.FinalOwnerVersion,
            readied.ChangedPrefixes,
            Mutated: true);
        return true;
    }

    internal bool IsReadiedSetLocusLatest(
        BakedPlaceProxyCommit readied)
    {
        ArgumentNullException.ThrowIfNull(readied);
        return readied.CommitId > _previousImposedSetLocusSealIdent
            && _alterationRev == readied.ExpectedMutationRevision
            && FetchHolderVer(readied.EntityId)
                == readied.ExpectedOwnerVersion
            && HasLogicalHolder(readied.EntityId)
                != readied.ProvenShapeless;
    }

    internal void RelaySetLocusSeal(
        in PlaceProxyCommitReceipt receipt)
    {
        if (!receipt.IsValid
            || receipt.CommitId > _previousImposedSetLocusSealIdent
            || !_queuedSetLocusDispatches.Remove(receipt.CommitId))
            return;
        if (!receipt.Mutated)
            return;
        ulong latestHolderVer = FetchHolderVer(receipt.EntityId);
        if (!HasLogicalHolder(receipt.EntityId)
            || latestHolderVer != receipt.OwnerVersion)

            return;
        for (int ordinal = 0; ordinal < receipt.ChangedPrefixes.Length; ++ordinal)
        {
            if (!HasLogicalHolder(receipt.EntityId)
                || FetchHolderVer(receipt.EntityId) != receipt.OwnerVersion)

                return;
            AlertStemWatchers(
                receipt.EntityId,
                receipt.ChangedPrefixes[ordinal]);
        }
        if (!HasLogicalHolder(receipt.EntityId))
            return;
        latestHolderVer = FetchHolderVer(receipt.EntityId);
        if (latestHolderVer != receipt.OwnerVersion)
            return;
        AlertHolderWatchers(
            receipt.EntityId,
            latestHolderVer);
    }

    internal bool TossSetLocusSeal(
        in PlaceProxyCommitReceipt receipt)
    {
        return receipt.IsValid
        && _queuedSetLocusDispatches.Remove(receipt.CommitId);
    }

    internal int QueuedSetLocusRelayTally =>
        _queuedSetLocusDispatches.Count;

    internal long SetLocusRelayMissTally =>
        _setLocusRelayMissTally;

    private void AlertStemWatchers(uint holder, uint stem)
    {
        var watchers = OwnerPrefixMembershipChanged;
        if (watchers is null)
            return;
        foreach (Action<uint, uint> watcher in watchers.GetInvocationList())
        {
            try
            {
                watcher(holder, stem);
            }
            catch
            {
                ++_setLocusRelayMissTally;
            }
        }
    }

    private void AlertHolderWatchers(uint holder, ulong ver)
    {
        var watchers = OwnerMutated;
        if (watchers is null)
            return;
        foreach (Action<uint, ulong> watcher in watchers.GetInvocationList())
        {
            try
            {
                watcher(holder, ver);
            }
            catch
            {
                ++_setLocusRelayMissTally;
            }
        }
    }

    private static uint[] GrabAlteredStems(
        BakedProxyOwnerState prior,
        BakedProxyOwnerState following)
    {
        var formerStems = GrabStems(prior);
        var newStems = GrabStems(following);
        List<uint> altered = new List<uint>();
        foreach (uint stem in formerStems)
        {
            if (!newStems.Contains(stem))
                altered.Add(stem);
        }
        foreach (uint stem in newStems)
        {
            if (!formerStems.Contains(stem))
                altered.Add(stem);
        }
        altered.Sort();
        return altered.ToArray();
    }

    private static HashSet<uint> GrabStems(
        BakedProxyOwnerState phase)
    {
        HashSet<uint> stems = new HashSet<uint>
        {
            phase.Registration.SeedCellId & 0xFFFF0000u,
        };
        if (phase.CellIds is not null)
        {
            for (int ordinal = 0; ordinal < phase.CellIds.Count; ++ordinal)
                stems.Add(phase.CellIds[ordinal] & 0xFFFF0000u);
        }
        if (phase.WithdrawnPrefixes is not null)
        {
            foreach (uint stem in phase.WithdrawnPrefixes)
                stems.Add(stem & 0xFFFF0000u);
        }
        return stems;
    }

    private BakedProxyCellSwap[] ReadyChamberSubstitutes(
        uint actorIdent,
        BakedProxyOwnerState prior,
        BakedProxyOwnerState following)
    {
        HashSet<uint> touched = new HashSet<uint>();
        AppendChambers(touched, prior.CellIds);
        AppendChambers(touched, following.CellIds);
        var followingRanks = new Dictionary<uint, ProxyEntry[]>();
        for (int ordinal = 0; ordinal < following.Rows.Count; ++ordinal)
        {
            ReadyChamberSubstitutesLoop(following, ordinal, touched, followingRanks);
        }
        for (int ordinal = 0; ordinal < prior.Rows.Count; ++ordinal)
            touched.Add(prior.Rows[ordinal].CellId);

        uint[] sequenced = touched.ToArray();
        Array.Sort(sequenced);
        BakedProxyCellSwap[] outcome = new BakedProxyCellSwap[sequenced.Length];
        for (int ordinal = 0; ordinal < sequenced.Length; ++ordinal)
        {
            uint chamberIdent = sequenced[ordinal];
            _ranksByChamber.TryGetValue(chamberIdent, out List<ProxyEntry>? engaged);
            followingRanks.TryGetValue(chamberIdent, out ProxyEntry[]? holderRanks);
            int keptTally = 0;
            if (engaged is not null)
            {
                for (int rank = 0; rank < engaged.Count; ++rank)
                {
                    if (engaged[rank].EntityId != actorIdent)
                        ++keptTally;
                }
            }
            List<ProxyEntry> substitute = new List<ProxyEntry>(
                keptTally + (holderRanks?.Length ?? 0));
            if (engaged is not null)
            {
                for (int rank = 0; rank < engaged.Count; ++rank)
                {
                    if (engaged[rank].EntityId != actorIdent)
                        substitute.Add(engaged[rank]);
                }
            }
            if (holderRanks is not null)
                substitute.AddRange(holderRanks);
            outcome[ordinal] = new BakedProxyCellSwap(
                chamberIdent,
                substitute);
        }
        return outcome;
    }

    private void ReadyChamberSubstitutesLoop(BakedProxyOwnerState following, int ordinal, HashSet<uint> touched, Dictionary<uint, ProxyEntry[]> followingRanks)
    {
        var rank = following.Rows[ordinal];
        touched.Add(rank.CellId);
        followingRanks[rank.CellId] = rank.Entries;
    }

    private BakedProxyCanonCellSwap[] ReadyCanonPieceListingSubstitutes(
        uint actorIdent,
        BakedProxyOwnerState prior,
        BakedProxyOwnerState following)
    {
        HashSet<uint> touched = new HashSet<uint>();
        AppendChambers(touched, prior.RetailCellIds);
        AppendChambers(touched, following.RetailCellIds);
        var followingRanks = new Dictionary<uint, CanonPartRow[]>();
        for (int ordinal = 0; ordinal < following.RetailRows.Count; ++ordinal)
        {
            ReadyCanonPieceListingSubstitutesLoop(following, ordinal, touched, followingRanks);
        }
        for (int ordinal = 0; ordinal < prior.RetailRows.Count; ++ordinal)
            touched.Add(prior.RetailRows[ordinal].CellId);

        uint[] sequenced = touched.ToArray();
        Array.Sort(sequenced);
        var outcome = new BakedProxyCanonCellSwap[sequenced.Length];
        for (int ordinal = 0; ordinal < sequenced.Length; ++ordinal)
        {
            uint chamberIdent = sequenced[ordinal];
            _canonRanksByChamber.TryGetValue(chamberIdent, out List<CanonPartRow>? engaged);
            followingRanks.TryGetValue(chamberIdent, out CanonPartRow[]? holderRanks);
            int keptTally = 0;
            if (engaged is not null)
            {
                for (int rank = 0; rank < engaged.Count; ++rank)
                {
                    if (engaged[rank].EntityId != actorIdent)
                        ++keptTally;
                }
            }
            List<CanonPartRow> substitute = new List<CanonPartRow>(
                keptTally + (holderRanks?.Length ?? 0));
            if (engaged is not null)
            {
                for (int rank = 0; rank < engaged.Count; ++rank)
                {
                    if (engaged[rank].EntityId != actorIdent)
                        substitute.Add(engaged[rank]);
                }
            }
            if (holderRanks is not null)
                substitute.AddRange(holderRanks);
            outcome[ordinal] = new BakedProxyCanonCellSwap(
                chamberIdent,
                substitute);
        }
        return outcome;
    }

    private void ReadyCanonPieceListingSubstitutesLoop(BakedProxyOwnerState following, int ordinal, HashSet<uint> touched, Dictionary<uint, CanonPartRow[]> followingRanks)
    {
        var rank = following.RetailRows[ordinal];
        touched.Add(rank.CellId);
        followingRanks[rank.CellId] = rank.Entries;
    }

    private BakedProxyPrefixSwap[] ReadyStemSubstitutes(
        uint actorIdent,
        HashSet<uint> prior,
        HashSet<uint> following,
        uint[] alteredStems)
    {
        BakedProxyPrefixSwap[] outcome = new BakedProxyPrefixSwap[
            alteredStems.Length];
        for (int ordinal = 0; ordinal < alteredStems.Length; ++ordinal)
        {
            uint stem = alteredStems[ordinal];
            bool dropHolder = prior.Contains(stem)
                && !following.Contains(stem);
            _holdersByStem.TryGetValue(stem, out List<uint>? formerSockets);
            _holderOrdinalByStem.TryGetValue(
                stem,
                out Dictionary<uint, int>? formerOrdinals);
            _releaseHolderSocketsByStem.TryGetValue(stem, out Stack<int>? formerSpare);
            List<uint> sockets = formerSockets is null ? [] : new List<uint>(formerSockets);
            var ordinals = formerOrdinals is null
                ? new Dictionary<uint, int>()
                : new Dictionary<uint, int>(formerOrdinals);
            Stack<int> release = ReplicatePile(formerSpare);
            if (dropHolder)
            {
                if (ordinals.Remove(actorIdent, out int holderSocket))
                {
                    sockets[holderSocket] = 0u;
                    release.Push(holderSocket);
                }
                outcome[ordinal] = ordinals.Count is 0
                    ? new BakedProxyPrefixSwap(
                        stem,
                        Remove: true,
                        Slots: null,
                        Indices: null,
                        FreeSlots: null)
                    : new BakedProxyPrefixSwap(
                        stem,
                        Remove: false,
                        sockets,
                        ordinals,
                        release);
                continue;
            }

            if (!ordinals.ContainsKey(actorIdent))
            {
                if (release.TryPop(out int releaseOrdinal))
                {
                    sockets[releaseOrdinal] = actorIdent;
                    ordinals[actorIdent] = releaseOrdinal;
                }
                else
                {
                    ordinals[actorIdent] = sockets.Count;
                    sockets.Add(actorIdent);
                }
            }
            outcome[ordinal] = new BakedProxyPrefixSwap(
                stem,
                Remove: false,
                sockets,
                ordinals,
                release);
        }
        return outcome;
    }

    private static Stack<int> ReplicatePile(Stack<int>? src)
    {
        return src is null
            ? new Stack<int>()
            : new Stack<int>(src.Reverse());
    }

    private static void AppendChambers(HashSet<uint> dest, List<uint>? chambers)
    {
        if (chambers is null)
            return;
        for (int ordinal = 0; ordinal < chambers.Count; ++ordinal)
            dest.Add(chambers[ordinal]);
    }

    private static void ReplaceHolderVal<T>(
        Dictionary<uint, T> dest,
        uint actorIdent,
        T? val)
        where T : class
    {
        if (val is null)
            dest.Remove(actorIdent);
        else
            dest[actorIdent] = val;
    }
}
