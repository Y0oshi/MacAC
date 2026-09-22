using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Landblock streaming: reflooding, retiring and replacing the owners a landblock holds.</summary>
public sealed partial class ProxyRegistry
{
    public void RefloodLb(uint lbIdent)
    {
        uint[] holders = GrabRefloodHoldersForLb(lbIdent);
        for (int idx = 0; idx < holders.Length; ++idx)
            RefloodHolderForLb(holders[idx], lbIdent);
    }

    public uint[] GrabRefloodHoldersForLb(uint lbIdent)
    {
        uint lbStem = lbIdent & 0xFFFF0000u;
        HashSet<uint> toReflood = new HashSet<uint>();

        foreach (var kvp in _enrolments)
        {
            if (_dormantHolders.Contains(kvp.Key))
                continue;

            if ((kvp.Value.SeedCellId & 0xFFFF0000u) == lbStem)
            {
                toReflood.Add(kvp.Key);
                continue;
            }
            if (_chambersByHolder.TryGetValue(kvp.Key, out var chambers))
            {
                foreach (uint c in chambers)
                {
                    if ((c & 0xFFFF0000u) == lbStem)
                    {
                        toReflood.Add(kvp.Key);
                        break;
                    }
                }
            }
            if (_withdrawnByHolder.TryGetValue(kvp.Key, out var stems)
                && stems.Contains(lbStem))

                toReflood.Add(kvp.Key);
        }

        uint[] sequenced = toReflood.ToArray();
        Array.Sort(sequenced);
        return sequenced;
    }

    public void RefloodHolderForLb(uint actorIdent, uint lbIdent)
    {
        uint lbStem = lbIdent & 0xFFFF0000u;
        if (_dormantHolders.Contains(actorIdent)
            || !_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? reg))

            return;

        _withdrawnByHolder.TryGetValue(
            actorIdent,
            out var withdrawnPriorReflood);

        _canonPiecesByHolder.TryGetValue(
            actorIdent,
            out IReadOnlyList<ProxyShape>? keptPieceArr);

        if (reg.IsMultiPart
            && _formsByHolder.TryGetValue(actorIdent, out var forms))
        {
            EnrollMultiPiece(
                actorIdent,
                reg.EntityWorldPos,
                reg.EntityWorldRot,
                forms,
                reg.State,
                reg.Flags,
                0f,
                0f,
                lbStem,
                reg.SeedCellId,
                reg.IsStatic,
                broadcastAlteration: false,
                pieceArr: keptPieceArr);
        }
        else
        {
            Register(
                actorIdent,
                reg.GfxObjId,
                reg.EntityWorldPos,
                reg.EntityWorldRot,
                reg.Radius,
                0f,
                0f,
                lbStem,
                reg.CollisionType,
                reg.CylHeight,
                reg.Scale,
                reg.State,
                reg.Flags,
                reg.SeedCellId,
                reg.IsStatic,
                broadcastAlteration: false,
                pieceArr: keptPieceArr);
        }

        if (withdrawnPriorReflood is not null)
            _withdrawnByHolder[actorIdent] = withdrawnPriorReflood;

        if (_chambersByHolder.TryGetValue(actorIdent, out var refreshedChambers)
            && refreshedChambers.Exists(chamber =>
                (chamber & 0xFFFF0000u) == lbStem)
            && _withdrawnByHolder.TryGetValue(
                actorIdent,
                out var withdrawn))
        {
            withdrawn.Remove(lbStem);
            if (withdrawn.Count is 0)
                _withdrawnByHolder.Remove(actorIdent);
        }
        BumpHolderVer(actorIdent);
    }

    public void RemoveLandblock(uint lbIdent)
    {
        uint lbStem = lbIdent & 0xFFFF0000u;
        List<uint> toDrop = new List<uint>();
        HashSet<uint> touchedHolders = new HashSet<uint>();

        foreach (var (actorIdent, chambers) in _chambersByHolder)
        {
            if (!chambers.Exists(chamber => (chamber & 0xFFFF0000u) == lbStem))
                continue;
            touchedHolders.Add(actorIdent);
            if (!_withdrawnByHolder.TryGetValue(actorIdent, out var withdrawn))
            {
                withdrawn = new HashSet<uint>();
                _withdrawnByHolder[actorIdent] = withdrawn;
            }
            withdrawn.Add(lbStem);
        }

        foreach (var kvp in _ranksByChamber)
        {
            if ((kvp.Key & 0xFFFF0000u) == lbStem)
                toDrop.Add(kvp.Key);
        }

        foreach (var chamberIdent in toDrop)
            _ranksByChamber.Remove(chamberIdent);

        List<uint> actorsToDrop = new List<uint>();
        foreach (var kvp in _chambersByHolder)
        {
            kvp.Value.RemoveAll(c => (c & 0xFFFF0000u) == lbStem);
            if (kvp.Value.Count is 0)
                actorsToDrop.Add(kvp.Key);
        }
        foreach (var eid in actorsToDrop)
        {
            _chambersByHolder.Remove(eid);
            if (!_enrolments.TryGetValue(eid, out var enrollment)
                || enrollment.IsStatic)
            {
                _formsByHolder.Remove(eid);
                _enrolments.Remove(eid);
                _dormantHolders.Remove(eid);
                _dormantHolderChambers.Remove(eid);
                _withdrawnByHolder.Remove(eid);
            }
        }
        DiscardCanonProductForStem(lbStem, touchedHolders);
        foreach (uint actorIdent in touchedHolders)
            BumpHolderVer(actorIdent);
    }

    private readonly List<uint> _stemDeletionTemp = new();

    internal void RetireHolderFromLb(uint actorIdent, uint lbIdent)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        if (_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment)
            && enrollment.IsStatic
            && (enrollment.SeedCellId & 0xFFFF0000u) == stem)
        {
            Withdraw(actorIdent, broadcastAlteration: false);
            DropHolderStems(actorIdent);
            _versions.Remove(actorIdent);
            ProgressAlterationRev();
            return;
        }
        bool touched = false;
        if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? canonChambers))
        {
            for (int ordinal = canonChambers.Count - 1; ordinal >= 0; --ordinal)
            {
                uint chamberIdent = canonChambers[ordinal];
                if ((chamberIdent & 0xFFFF0000u) != stem)
                    continue;
                touched = true;
                canonChambers.RemoveAt(ordinal);
                if (_canonRanksByChamber.TryGetValue(
                        chamberIdent,
                        out List<CanonPartRow>? pieceRanks))
                {
                    DropHolderPieceRanks(pieceRanks, actorIdent);
                    if (pieceRanks.Count is 0)
                        _canonRanksByChamber.Remove(chamberIdent);
                }
            }
            if (canonChambers.Count is 0)
                _canonChambersByHolder.Remove(actorIdent);
        }
        if (!_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers))
        {
            if (touched)
                BumpHolderVer(actorIdent);
            return;
        }

        for (int ordinal = chambers.Count - 1; ordinal >= 0; --ordinal)
        {
            uint chamberIdent = chambers[ordinal];
            if ((chamberIdent & 0xFFFF0000u) != stem)
                continue;
            touched = true;
            chambers.RemoveAt(ordinal);
            if (_ranksByChamber.TryGetValue(chamberIdent, out List<ProxyEntry>? listings))
            {
                DropHolderRanks(listings, actorIdent);
                if (listings.Count is 0)
                    _ranksByChamber.Remove(chamberIdent);
            }
        }
        if (!touched)
            return;
        if (!_withdrawnByHolder.TryGetValue(
                actorIdent,
                out HashSet<uint>? withdrawn))
        {
            withdrawn = new HashSet<uint>();
            _withdrawnByHolder[actorIdent] = withdrawn;
        }
        withdrawn.Add(stem);
        if (chambers.Count is 0)
            _chambersByHolder.Remove(actorIdent);
        BumpHolderVer(actorIdent);
    }

    // Mirrors one ordinary active-world mutation into an off-side generation
    internal void MirrorHolderFrom(
        ProxyRegistry src,
        uint actorIdent)
    {
        ArgumentNullException.ThrowIfNull(src);
        Withdraw(actorIdent, broadcastAlteration: false);
        if (src.TryBakeHolder(
                actorIdent,
                out BakedProxyOwnerState? phase)
            && phase is not null)
        {
            SetupBakedHolder(phase);
            _versions[actorIdent] = src.FetchHolderVer(actorIdent);
        }
        else
        {
            DropHolderStems(actorIdent);
            _versions.Remove(actorIdent);
        }
    }

    internal int GrabHolderSocketThreshold() => _holderLineup.Count;

    internal uint FetchHolderSocket(int ordinal) => _holderLineup[ordinal];

    internal void ImposeSealedHolderSubstitute(
        ProxyRegistry loadingSrc,
        uint holderIdent,
        uint lbIdent)
    {
        ArgumentNullException.ThrowIfNull(loadingSrc);
        if (loadingSrc.HasLogicalHolder(holderIdent))
        {
            MirrorHolderFrom(loadingSrc, holderIdent);
            RefloodHolderForLb(holderIdent, lbIdent);
            return;
        }
        if (IsStaticHolderRootedIn(holderIdent, lbIdent))
        {
            Withdraw(holderIdent, broadcastAlteration: false);
            DropHolderStems(holderIdent);
            _versions.Remove(holderIdent);
            ProgressAlterationRev();
            return;
        }
        RefloodHolderForLb(holderIdent, lbIdent);
    }

    internal void RefloodStemHoldersFollowingSubstitute(
        uint lbIdent,
        IReadOnlyList<uint> sealedHolderIdents)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        if (!_holdersByStem.TryGetValue(stem, out List<uint>? sockets))
            return;
        HashSet<uint> imposed = new HashSet<uint>(sealedHolderIdents);
        int threshold = sockets.Count;
        for (int ordinal = 0; ordinal < threshold; ++ordinal)
        {
            uint holderIdent = sockets[ordinal];
            if (holderIdent is 0u || !imposed.Add(holderIdent))
                continue;
            RefloodHolderForLb(holderIdent, lbIdent);
        }
    }

    internal bool RenewKeptHolderFrom(
        ProxyRegistry src,
        uint actorIdent,
        uint lbIdent,
        out ulong srcVer)
    {
        ArgumentNullException.ThrowIfNull(src);
        srcVer = src.FetchHolderVer(actorIdent);
        if (!src._enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment)
            || src._dormantHolders.Contains(actorIdent)
            || !src.HolderTouchesLb(actorIdent, lbIdent))
        {
            MirrorHolderFrom(src, actorIdent);
            return false;
        }
        if (enrollment.IsStatic
            && (enrollment.SeedCellId & 0xFFFF0000u)
                == (lbIdent & 0xFFFF0000u))

            return false;

        Withdraw(actorIdent, broadcastAlteration: false);

        if (enrollment.IsMultiPart
            && src._formsByHolder.TryGetValue(
                actorIdent,
                out IReadOnlyList<ProxyShape>? forms))
        {
            EnrollMultiPiece(
                actorIdent,
                enrollment.EntityWorldPos,
                enrollment.EntityWorldRot,
                forms,
                enrollment.State,
                enrollment.Flags,
                0f,
                0f,
                lbIdent,
                enrollment.SeedCellId,
                isStatic: enrollment.IsStatic,
                broadcastAlteration: false);
        }
        else
        {
            Register(
                actorIdent,
                enrollment.GfxObjId,
                enrollment.EntityWorldPos,
                enrollment.EntityWorldRot,
                enrollment.Radius,
                0f,
                0f,
                lbIdent,
                enrollment.CollisionType,
                enrollment.CylHeight,
                enrollment.Scale,
                enrollment.State,
                enrollment.Flags,
                enrollment.SeedCellId,
                isStatic: enrollment.IsStatic,
                broadcastAlteration: false);
        }

        if (src._withdrawnByHolder.TryGetValue(
                actorIdent,
                out HashSet<uint>? srcWithdrawn))
        {
            HashSet<uint> keptWithdrawn = new HashSet<uint>(srcWithdrawn);
            uint stem = lbIdent & 0xFFFF0000u;
            if (_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers)
                && chambers.Exists(chamber => (chamber & 0xFFFF0000u) == stem))

                keptWithdrawn.Remove(stem);
            if (keptWithdrawn.Count is not 0)
                _withdrawnByHolder[actorIdent] = keptWithdrawn;
        }
        ReindexHolderStems(actorIdent);
        _versions[actorIdent] = srcVer;
        return true;
    }

    internal LandblockSwapBuilder BuildLbSubstituteBuilder(
        ProxyRegistry loading,
        uint lbIdent,
        IReadOnlyList<uint> anticipatedKeptHolders)
    {
        return new(
            this,
            loading,
            lbIdent,
            anticipatedKeptHolders);
    }

    private void DiscardCanonProductForStem(
        uint lbStem,
        HashSet<uint> touchedHolders)
    {
        _stemDeletionTemp.Clear();
        foreach (uint chamberIdent in _canonRanksByChamber.Keys)
        {
            if ((chamberIdent & 0xFFFF0000u) == lbStem)
                _stemDeletionTemp.Add(chamberIdent);
        }
        for (int idx = 0; idx < _stemDeletionTemp.Count; ++idx)
            _canonRanksByChamber.Remove(_stemDeletionTemp[idx]);

        _stemDeletionTemp.Clear();
        foreach (var (holderIdent, chambers) in _canonChambersByHolder)
        {
            for (int idx = chambers.Count - 1; idx >= 0; --idx)
            {
                if ((chambers[idx] & 0xFFFF0000u) == lbStem)
                {
                    chambers.RemoveAt(idx);
                    touchedHolders.Add(holderIdent);
                }
            }
            if (chambers.Count is 0)
                _stemDeletionTemp.Add(holderIdent);
        }
        for (int idx = 0; idx < _stemDeletionTemp.Count; ++idx)
        {
            uint holderIdent = _stemDeletionTemp[idx];
            _canonChambersByHolder.Remove(holderIdent);
            bool endsWithLb =
                !_enrolments.TryGetValue(holderIdent, out EnrolmentRecord? enrollment)
                || enrollment.IsStatic;
            if (!endsWithLb)
                continue;
            _canonCoursesByHolder.Remove(holderIdent);
            _canonPiecesByHolder.Remove(holderIdent);
            _formsByHolder.Remove(holderIdent);
            _enrolments.Remove(holderIdent);
            _dormantHolders.Remove(holderIdent);
            _dormantHolderChambers.Remove(holderIdent);
            _withdrawnByHolder.Remove(holderIdent);
        }
    }

    private bool TryBakeHolder(
        uint actorIdent,
        out BakedProxyOwnerState? phase)
    {
        if (!_enrolments.TryGetValue(actorIdent, out EnrolmentRecord? enrollment))
        {
            phase = null;
            return false;
        }
        _chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers);
        _dormantHolderChambers.TryGetValue(
            actorIdent,
            out List<uint>? suspendedChambers);
        _formsByHolder.TryGetValue(
            actorIdent,
            out IReadOnlyList<ProxyShape>? forms);
        _withdrawnByHolder.TryGetValue(
            actorIdent,
            out HashSet<uint>? withdrawn);
        var ranks = new List<BakedProxyCellRows>();
        if (chambers is not null)
        {
            foreach (uint chamberIdent in chambers)
            {
                if (_ranksByChamber.TryGetValue(chamberIdent, out List<ProxyEntry>? listings))
                {
                    ranks.Add(new BakedProxyCellRows(
                        chamberIdent,
                        CollectHolderRanks(listings, actorIdent)));
                }
            }
        }

        _canonPiecesByHolder.TryGetValue(
            actorIdent,
            out IReadOnlyList<ProxyShape>? canonPieceArr);
        _canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? canonChambers);
        CanonCellSetRoute canonCourse = _canonCoursesByHolder.TryGetValue(
            actorIdent,
            out CanonCellSetRoute grabbedCourse)
                ? grabbedCourse
                : CanonCellSetRoute.None;
        var canonRanks = new List<BakedProxyCanonPartRows>();
        if (canonChambers is not null)
        {
            foreach (uint chamberIdent in canonChambers)
            {
                if (_canonRanksByChamber.TryGetValue(
                        chamberIdent,
                        out List<CanonPartRow>? listings))
                {
                    canonRanks.Add(new BakedProxyCanonPartRows(
                        chamberIdent,
                        CollectHolderPieceRanks(listings, actorIdent)));
                }
            }
        }

        phase = new BakedProxyOwnerState(
            actorIdent,
            enrollment,
            forms,
            chambers is null ? null : new List<uint>(chambers),
            ranks,
            _dormantHolders.Contains(actorIdent),
            suspendedChambers is null ? null : new List<uint>(suspendedChambers),
            withdrawn is null ? null : new HashSet<uint>(withdrawn),
            canonPieceArr,
            canonChambers is null ? null : new List<uint>(canonChambers),
            canonCourse,
            canonRanks);
        return true;
    }

    private void SetupBakedHolder(BakedProxyOwnerState phase)
    {
        _enrolments[phase.EntityId] = phase.Registration;
        if (phase.Shapes is not null)
            _formsByHolder[phase.EntityId] = phase.Shapes;
        if (phase.Suspended)
            _dormantHolders.Add(phase.EntityId);
        if (phase.SuspendedCellIds is not null)
            _dormantHolderChambers[phase.EntityId] = phase.SuspendedCellIds;
        if (phase.WithdrawnPrefixes is not null)
        {
            _withdrawnByHolder[phase.EntityId] = phase.WithdrawnPrefixes;
        }
        if (phase.CellIds is not null)
            _chambersByHolder[phase.EntityId] = phase.CellIds;
        for (int rankOrdinal = 0; rankOrdinal < phase.Rows.Count; ++rankOrdinal)
        {
            var rank = phase.Rows[rankOrdinal];
            for (int listingOrdinal = 0; listingOrdinal < rank.Entries.Length; ++listingOrdinal)
                FileRank(rank.Entries[listingOrdinal], rank.CellId);
        }

        if (phase.RetailPartArray is not null)
        {
            _canonPiecesByHolder[phase.EntityId] = phase.RetailPartArray;
            _canonCoursesByHolder[phase.EntityId] = phase.RetailRoute;
        }
        if (phase.RetailCellIds is not null)
            _canonChambersByHolder[phase.EntityId] = phase.RetailCellIds;
        for (int rankOrdinal = 0; rankOrdinal < phase.RetailRows.Count; ++rankOrdinal)
        {
            var rank = phase.RetailRows[rankOrdinal];
            if (!_canonRanksByChamber.TryGetValue(
                    rank.CellId,
                    out List<CanonPartRow>? listings))
            {
                listings = new List<CanonPartRow>();
                _canonRanksByChamber[rank.CellId] = listings;
            }
            listings.AddRange(rank.Entries);
            StampChamber(listings);
        }
        BumpHolderVer(phase.EntityId);
    }

    private bool KeepsHolderOnReflood(uint holderIdent, uint lbIdent)
    {
        if (!_enrolments.TryGetValue(holderIdent, out EnrolmentRecord? enrollment)
            || _dormantHolders.Contains(holderIdent)
            || (enrollment.IsStatic
                && (enrollment.SeedCellId & 0xFFFF0000u)
                    == (lbIdent & 0xFFFF0000u)))

            return false;
        return HolderTouchesLb(holderIdent, lbIdent);
    }

    internal sealed class LandblockSwapBuilder : IDisposable
    {
        private readonly ProxyRegistry _engaged;
        private readonly ProxyRegistry _loading;
        private readonly uint _stem;
        private readonly IReadOnlyList<uint> _anticipated;
        private readonly List<uint>? _engagedSockets;
        private readonly List<uint>? _loadingSockets;
        private readonly int _engagedSocketThreshold;
        private readonly int _loadingSocketThreshold;
        private readonly HashSet<uint> _holders = new();
        private readonly List<uint> _holderIdents = new();
        private readonly List<BakedProxyOwnerSlot> _phases = new();
        private readonly Dictionary<uint, int> _phaseOrdinal = new();
        private int _anticipatedOrdinal;
        private int _engagedSocketOrdinal;
        private int _loadingSocketOrdinal;
        private int _holderOrdinal;
        private int _stage;

        internal LandblockSwapBuilder(
            ProxyRegistry engaged,
            ProxyRegistry loading,
            uint lbIdent,
            IReadOnlyList<uint> anticipated)
        {
            _engaged = engaged;
            _loading = loading;
            _stem = lbIdent & 0xFFFF0000u;
            _anticipated = anticipated;
            engaged._holdersByStem.TryGetValue(
                _stem,
                out _engagedSockets);
            loading._holdersByStem.TryGetValue(
                _stem,
                out _loadingSockets);
            _engagedSocketThreshold = _engagedSockets?.Count ?? 0;
            _loadingSocketThreshold = _loadingSockets?.Count ?? 0;
        }

        internal int JobUnits { get; private set; }
        internal BakedLandblockProxySwap? Prepared { get; private set; }

        public void Dispose() { }

        internal bool Advance()
        {
            switch (_stage)
            {
                case 0:
                    if (_anticipatedOrdinal < _anticipated.Count)
                    {
                        AppendHolder(_anticipated[_anticipatedOrdinal++]);
                        ++JobUnits;
                        return false;
                    }
                    ++_stage;
                    return false;
                case 1:
                    if (_engagedSocketOrdinal < _engagedSocketThreshold)
                    {
                        uint holderIdent = _engagedSockets![_engagedSocketOrdinal++];
                        if (_engaged._enrolments.TryGetValue(
                                holderIdent,
                                out EnrolmentRecord? enrollment)
                            && enrollment.IsStatic
                            && (enrollment.SeedCellId & 0xFFFF0000u) == _stem)

                            AppendHolder(holderIdent);
                        ++JobUnits;
                        return false;
                    }
                    ++_stage;
                    return false;
                case 2:
                    if (_loadingSocketOrdinal < _loadingSocketThreshold)
                    {
                        uint holderIdent = _loadingSockets![_loadingSocketOrdinal++];
                        if (_loading._enrolments.ContainsKey(holderIdent))
                            AppendHolder(holderIdent);
                        ++JobUnits;
                        return false;
                    }
                    ++_stage;
                    return false;
                case 3:
                    if (_holderOrdinal < _holderIdents.Count)
                    {
                        uint holderIdent = _holderIdents[_holderOrdinal++];
                        _loading.TryBakeHolder(
                            holderIdent,
                            out BakedProxyOwnerState? phase);
                        _phaseOrdinal[holderIdent] = _phases.Count;
                        _phases.Add(new BakedProxyOwnerSlot(holderIdent, phase));
                        ++JobUnits;
                        return false;
                    }
                    Prepared = new BakedLandblockProxySwap(
                        _stem,
                        _holderIdents,
                        _phases);
                    ++_stage;
                    return true;
                default:
                    return true;
            }
        }

        internal void AppendHolder(uint holderIdent)
        {
            if (_holders.Add(holderIdent))
                _holderIdents.Add(holderIdent);
        }

        internal void RenewHolder(uint holderIdent)
        {
            AppendHolder(holderIdent);
            if (_phaseOrdinal.TryGetValue(holderIdent, out int ordinal))
            {
                _loading.TryBakeHolder(
                    holderIdent,
                    out BakedProxyOwnerState? phase);
                _phases[ordinal].State = phase;
                return;
            }
            if (_stage > 3)
            {
                _loading.TryBakeHolder(
                    holderIdent,
                    out BakedProxyOwnerState? phase);
                _phaseOrdinal[holderIdent] = _phases.Count;
                _phases.Add(new BakedProxyOwnerSlot(holderIdent, phase));
            }
        }
    }

    internal sealed class BakedLandblockProxySwap
    {
        internal BakedLandblockProxySwap(
            uint lbStem,
            IReadOnlyList<uint> holderIdents,
            IReadOnlyList<BakedProxyOwnerSlot> holderPhases)
        {
            LbStem = lbStem;
            HolderIdents = holderIdents;
            HolderPhases = holderPhases;
        }

        internal uint LbStem { get; }
        internal IReadOnlyList<uint> HolderIdents { get; }
        internal IReadOnlyList<BakedProxyOwnerSlot> HolderPhases { get; }
    }

    internal sealed class BakedProxyOwnerSlot
    {
        internal BakedProxyOwnerSlot(
            uint actorIdent,
            BakedProxyOwnerState? phase)
        {
            ActorIdent = actorIdent;
            State = phase;
        }

        internal uint ActorIdent { get; }
        internal BakedProxyOwnerState? State { get; set; }
    }

    internal sealed record BakedProxyOwnerState(
        uint EntityId,
        EnrolmentRecord Registration,
        IReadOnlyList<ProxyShape>? Shapes,
        List<uint>? CellIds,
        IReadOnlyList<BakedProxyCellRows> Rows,
        bool Suspended,
        List<uint>? SuspendedCellIds,
        HashSet<uint>? WithdrawnPrefixes,
        IReadOnlyList<ProxyShape>? RetailPartArray,
        List<uint>? RetailCellIds,
        CanonCellSetRoute RetailRoute,
        IReadOnlyList<BakedProxyCanonPartRows> RetailRows);

    internal sealed record BakedProxyCellRows(
        uint CellId,
        ProxyEntry[] Entries);

    internal sealed record BakedProxyCanonPartRows(
        uint CellId,
        CanonPartRow[] Entries);
}
