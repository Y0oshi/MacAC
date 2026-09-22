using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

/// <summary>The owner roster and per-landblock owner index, and the reflood scan that walks it.</summary>
public sealed partial class ProxyRegistry
{
    internal sealed class RefloodOwnerScan : IDisposable
    {
        private readonly ProxyRegistry _holder;
        private readonly uint _stem;
        private readonly List<uint>? _sockets;
        private readonly int _threshold;
        private int _ordinal;
        private bool _completed;

        internal RefloodOwnerScan(
            ProxyRegistry holder,
            uint stem,
            List<uint>? sockets)
        {
            _holder = holder;
            _stem = stem;
            _sockets = sockets;
            _threshold = sockets?.Count ?? 0;
        }

        internal RefloodOwnerScanStep Advance()
        {
            if (_completed)
            {
                return new RefloodOwnerScanStep(
                    Completed: true,
                    HasOwner: false,
                    OwnerId: 0u);
            }
            if (_sockets is null || _ordinal >= _threshold)
            {
                _completed = true;
                return new RefloodOwnerScanStep(
                    Completed: true,
                    HasOwner: false,
                    OwnerId: 0u);
            }

            uint holderIdent = _sockets[_ordinal++];
            bool kept = _holder.KeepsHolderOnReflood(holderIdent, _stem);
            return new RefloodOwnerScanStep(
                Completed: false,
                HasOwner: kept,
                OwnerId: kept ? holderIdent : 0u);
        }

        public void Dispose() { }
    }

    internal readonly record struct RefloodOwnerScanStep(
        bool Completed,
        bool HasOwner,
        uint OwnerId);

    internal RefloodOwnerScan BuildKeptRefloodHolderScan(
        uint lbIdent)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        _holdersByStem.TryGetValue(stem, out List<uint>? sockets);
        return new RefloodOwnerScan(this, stem, sockets);
    }

    private void ReindexHolderStems(uint actorIdent)
    {
        if (!_enrolments.ContainsKey(actorIdent))
        {
            DropHolderStems(actorIdent);
            return;
        }
        EnrolOnLineup(actorIdent);
        _stemTemp.Clear();
        if (_enrolments.TryGetValue(actorIdent, out EnrolmentRecord? enrollment))
            _stemTemp.Add(enrollment.SeedCellId & 0xFFFF0000u);
        if (_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers))
        {
            for (int ordinal = 0; ordinal < chambers.Count; ++ordinal)
                _stemTemp.Add(chambers[ordinal] & 0xFFFF0000u);
        }
        if (_withdrawnByHolder.TryGetValue(
                actorIdent,
                out HashSet<uint>? withdrawn))
        {
            foreach (uint stem in withdrawn)
                _stemTemp.Add(stem & 0xFFFF0000u);
        }

        if (!_stemsByHolder.TryGetValue(actorIdent, out HashSet<uint>? latest))
        {
            latest = new HashSet<uint>();
            _stemsByHolder[actorIdent] = latest;
        }

        _removedStemTemp.Clear();
        foreach (uint stem in latest)
        {
            if (!_stemTemp.Contains(stem))
                _removedStemTemp.Add(stem);
        }
        for (int ordinal = 0; ordinal < _removedStemTemp.Count; ++ordinal)
        {
            uint stem = _removedStemTemp[ordinal];
            latest.Remove(stem);
            if (_holderOrdinalByStem.TryGetValue(
                    stem,
                    out Dictionary<uint, int>? ordinals)
                && ordinals.Remove(actorIdent, out int socketOrdinal))
            {
                _holdersByStem[stem][socketOrdinal] = 0u;
                _releaseHolderSocketsByStem[stem].Push(socketOrdinal);
                PruneVacantStem(stem, ordinals);
            }
            OwnerPrefixMembershipChanged?.Invoke(actorIdent, stem);
        }

        foreach (uint stem in _stemTemp)
        {
            if (!latest.Add(stem))
                continue;
            if (!_holdersByStem.TryGetValue(stem, out List<uint>? sockets))
            {
                sockets = new List<uint>();
                _holdersByStem[stem] = sockets;
                _holderOrdinalByStem[stem] = new Dictionary<uint, int>();
                _releaseHolderSocketsByStem[stem] = new Stack<int>();
            }
            var ordinals = _holderOrdinalByStem[stem];
            if (ordinals.ContainsKey(actorIdent))
                continue;
            Stack<int> release = _releaseHolderSocketsByStem[stem];
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
            OwnerPrefixMembershipChanged?.Invoke(actorIdent, stem);
        }

    }

    private void DropHolderStems(uint actorIdent)
    {
        if (_stemsByHolder.Remove(actorIdent, out HashSet<uint>? stems))
        {
            foreach (uint stem in stems)
            {
                if (!_holderOrdinalByStem.TryGetValue(
                        stem,
                        out Dictionary<uint, int>? ordinals)
                    || !ordinals.Remove(actorIdent, out int socketOrdinal))

                    continue;

                _holdersByStem[stem][socketOrdinal] = 0u;
                _releaseHolderSocketsByStem[stem].Push(socketOrdinal);
                PruneVacantStem(stem, ordinals);
                OwnerPrefixMembershipChanged?.Invoke(actorIdent, stem);
            }
        }
        if (_lineupOrdinal.Remove(actorIdent, out int holderSocket))
        {
            _holderLineup[holderSocket] = 0u;
            _lineupSpareSockets.Push(holderSocket);
        }
    }

    private void EnrolOnLineup(uint actorIdent)
    {
        if (_lineupOrdinal.ContainsKey(actorIdent))
            return;
        if (_lineupSpareSockets.TryPop(out int releaseOrdinal))
        {
            _holderLineup[releaseOrdinal] = actorIdent;
            _lineupOrdinal[actorIdent] = releaseOrdinal;
            return;
        }
        _lineupOrdinal[actorIdent] = _holderLineup.Count;
        _holderLineup.Add(actorIdent);
    }

    private void PruneVacantStem(
        uint stem,
        Dictionary<uint, int> ordinals)
    {
        if (ordinals.Count is not 0)
            return;
        _holdersByStem.Remove(stem);
        _holderOrdinalByStem.Remove(stem);
        _releaseHolderSocketsByStem.Remove(stem);
    }
}
