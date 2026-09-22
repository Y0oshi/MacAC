using System.Diagnostics.CodeAnalysis;

namespace MacAC.Mechanics.Gear;

public sealed partial class ClientThingChart
{
    private static bool FilesAsBundle(ClientThing gear)
    {
        return gear.VesselKindHint is not 0u
        || (gear.Type & GearKind.Container) != 0
        || gear.ItemsCapacity > 0;
    }

    // Per-container member lists
    private sealed class ShelfIndex(ClientThingChart chart)
    {
        private readonly Dictionary<uint, List<uint>> _ranks = new();

        public int Count => _ranks.Count;

        public bool Holds(uint vesselIdent) => _ranks.ContainsKey(vesselIdent);

        public bool TryGet(uint vesselIdent, [NotNullWhen(true)] out List<uint>? participants) =>
            _ranks.TryGetValue(vesselIdent, out participants);

        public bool Lists(uint vesselIdent, uint gearIdent)
        {
            return _ranks.TryGetValue(vesselIdent, out List<uint>? participants) && participants.Contains(gearIdent);
        }

        public List<uint> FetchOrAppend(uint vesselIdent)
        {
            if (!_ranks.TryGetValue(vesselIdent, out List<uint>? participants))
                _ranks[vesselIdent] = participants = [];
            return participants;
        }

        public void Set(uint vesselIdent, List<uint> sequenced) => _ranks[vesselIdent] = sequenced;

        public bool Shed(uint vesselIdent) => _ranks.Remove(vesselIdent);

        public IReadOnlyList<uint> Snapshot(uint vesselIdent)
        {
            return _ranks.TryGetValue(vesselIdent, out List<uint>? participants) ? participants.ToArray() : [];
        }

        public void Clear() => _ranks.Clear();

        // Pulls gearIdent out of every list except the one for exceptVesselIdent; returns the containers
        // touched
        public List<uint>? Evict(uint gearIdent, uint exceptVesselIdent)
        {
            List<uint>? touched = null;
            foreach ((uint vesselIdent, List<uint> participants) in _ranks)
            {
                if (vesselIdent == exceptVesselIdent || !participants.Remove(gearIdent))
                    continue;
                (touched ??= []).Add(vesselIdent);
            }
            return touched;
        }

        public List<uint>? EvictEverywhere(uint gearIdent, uint destVesselIdent, bool wasBundle)
        {
            List<uint>? touched = null;
            foreach ((uint vesselIdent, List<uint> participants) in _ranks)
            {
                if (!participants.Remove(gearIdent))
                    continue;

                RenumberExec(participants, wasBundle);
                if (vesselIdent != destVesselIdent)
                    (touched ??= []).Add(vesselIdent);
            }
            return touched;
        }

        public void SlotIntoExec(ClientThing gear, int askedSocket)
        {
            List<uint> participants = FetchOrAppend(gear.VesselTag);
            bool bundle = FilesAsBundle(gear);
            int wanted = askedSocket < 0 ? int.MaxValue : askedSocket;
            int observed = 0;
            int at = participants.Count;
            for (int idx = 0; idx < participants.Count; ++idx)
            {
                if (!chart._objects.TryGetValue(participants[idx], out ClientThing? counterpart) || FilesAsBundle(counterpart) != bundle)
                    continue;

                if (observed == wanted)
                {
                    at = idx;
                    break;
                }

                ++observed;
                at = idx + 1;
            }

            participants.Insert(at, gear.ObjectId);
            int published = askedSocket < 0 ? observed : askedSocket;
            RenumberExec(participants, bundle, keepGearIdent: gear.ObjectId, keepSocket: published);
        }

        // Renumbers a whole list straight through, ignoring runs
        public void RenumberPlanar(List<uint> participants)
        {
            for (int idx = 0; idx < participants.Count; ++idx)
            {
                if (chart._objects.TryGetValue(participants[idx], out ClientThing? participant))
                    participant.VesselSlot = idx;
            }
        }

        public void Refile(ClientThing objRef, uint formerVesselIdent)
        {
            if (formerVesselIdent != objRef.VesselTag && formerVesselIdent is not 0u
                && _ranks.TryGetValue(formerVesselIdent, out List<uint>? former))

                former.Remove(objRef.ObjectId);

            if (objRef.VesselTag is 0u)
                return;

            List<uint> participants = FetchOrAppend(objRef.VesselTag);
            if (!participants.Contains(objRef.ObjectId))
                participants.Add(objRef.ObjectId);

            var grade = new Dictionary<uint, int>(participants.Count);
            for (int idx = 0; idx < participants.Count; ++idx)
                grade[participants[idx]] = idx;
            participants.Sort((a, b) =>
            {
                int bySocket = chart.DeclaredSocket(a).CompareTo(chart.DeclaredSocket(b));
                return bySocket is not 0 ? bySocket : grade[a].CompareTo(grade[b]);
            });
        }

        private void RenumberExec(List<uint> participants, bool bundle, uint keepGearIdent = 0u, int keepSocket = -1)
        {
            int socket = 0;
            foreach (uint oid in participants)
            {
                if (!chart._objects.TryGetValue(oid, out ClientThing? participant) || FilesAsBundle(participant) != bundle)
                    continue;
                participant.VesselSlot = participant.ObjectId == keepGearIdent ? keepSocket : socket;
                ++socket;
            }
        }
    }

    private int DeclaredSocket(uint oid)
    {
        return _objects.TryGetValue(oid, out ClientThing? o) && o.VesselSlot >= 0 ? o.VesselSlot : int.MaxValue;
    }

    // Per-wielder lists of worn item guids, newest first
    private sealed class WornIndex
    {
        private readonly Dictionary<uint, List<uint>> _ranks = new();

        public int Count => _ranks.Count;

        public bool TryGet(uint holderIdent, [NotNullWhen(true)] out List<uint>? stances) =>
            _ranks.TryGetValue(holderIdent, out stances);

        public bool Lists(uint holderIdent, uint gearIdent)
        {
            return _ranks.TryGetValue(holderIdent, out List<uint>? stances) && stances.Contains(gearIdent);
        }

        public void Set(uint holderIdent, List<uint> sequenced) => _ranks[holderIdent] = sequenced;

        public void Clear() => _ranks.Clear();

        // Drops the item from every owner, pruning owners left empty
        public void Forget(uint gearIdent)
        {
            List<uint>? emptied = null;
            foreach ((uint holderIdent, List<uint> stances) in _ranks)
            {
                stances.Remove(gearIdent);
                if (stances.Count is 0)
                    (emptied ??= []).Add(holderIdent);
            }

            if (emptied is null)
                return;
            foreach (uint holderIdent in emptied)
                _ranks.Remove(holderIdent);
        }

        // Re-homes the item from the owner implied by one placement to the next
        public void Shift(uint gearIdent, ObjectPlacement before, ObjectPlacement following)
        {
            if (before == following)
                return;

            uint precedingHolder = HolderOf(before);
            if (precedingHolder is not 0u && _ranks.TryGetValue(precedingHolder, out List<uint>? preceding))
            {
                preceding.Remove(gearIdent);
                if (preceding.Count is 0)
                    _ranks.Remove(precedingHolder);
            }

            uint upcomingHolder = HolderOf(following);
            if (upcomingHolder is 0u)
                return;
            if (!_ranks.TryGetValue(upcomingHolder, out List<uint>? upcoming))
                _ranks[upcomingHolder] = upcoming = [];
            upcoming.Remove(gearIdent);
            upcoming.Insert(0, gearIdent);
        }

        private static uint HolderOf(ObjectPlacement stance)
        {
            if (stance.EquipLocation == WieldBitmask.None)
                return 0u;
            return stance.WielderId is not 0u ? stance.WielderId : stance.ContainerId;
        }
    }

    // Optimistic moves awaiting the server
    private sealed class PendingLedger
    {
        private readonly Dictionary<uint, Hold> _holds = new();

        public int Count => _holds.Count;

        public void Open(uint gearIdent, ClientThing gear)
        {
            _holds[gearIdent] = _holds.TryGetValue(gearIdent, out Hold grip)
                ? grip with { Outstanding = grip.Outstanding + 1 }
                : new Hold(ObjectPlacement.From(gear), 1);
        }

        public void Settle(uint gearIdent)
        {
            if (!_holds.TryGetValue(gearIdent, out Hold grip))
                return;
            if (grip.Outstanding <= 1)
                _holds.Remove(gearIdent);
            else
                _holds[gearIdent] = grip with { Outstanding = grip.Outstanding - 1 };
        }

        public bool TryShut(uint gearIdent, out ObjectPlacement prior)
        {
            if (!_holds.Remove(gearIdent, out Hold grip))
            {
                prior = default;
                return false;
            }
            prior = grip.Before;
            return true;
        }

        public void Forget(uint gearIdent) => _holds.Remove(gearIdent);

        public void Clear() => _holds.Clear();

        private readonly record struct Hold(ObjectPlacement Before, int Outstanding);
    }
}
