namespace MacAC.Mechanics.Gear;

public enum HotbarDropSource
{
    FreshItem,
    ShortcutAlias,
}

public enum HotbarEditKind
{
    Remove,
    Add,
}

public readonly record struct HotbarEdit(
    HotbarEditKind Kind,
    int Slot,
    HotbarSlot? Entry)
{
    public static HotbarEdit Remove(int socket) => new(HotbarEditKind.Remove, socket, null);

    public static HotbarEdit Add(HotbarSlot listing) => new(HotbarEditKind.Add, listing.Index, listing);
}

public static class HotbarDropPlanner
{
    public static HotbarEdit[] PlanDiscard(
        IReadOnlyList<HotbarSlot?> sockets,
        HotbarDropSource src,
        int srcSocket,
        int markSocket,
        HotbarSlot dragged)
    {
        ArgumentNullException.ThrowIfNull(sockets);
        if (sockets.Count != HotbarStore.SlotTally || (uint)markSocket >= HotbarStore.SlotTally || dragged.ObjectId is 0u)
            return [];

        Scratch board = new Scratch(sockets, anticipatedEdits: 4);
        HotbarSlot? displaced = board.Occupant(markSocket);
        board.Vacate(markSocket);

        switch (src)
        {
            case HotbarDropSource.FreshItem:
                board.VacateLeadHolding(dragged.ObjectId);
                board.Place(dragged.WithOrdinal(markSocket));
                if (displaced is { } bumped && bumped.ObjectId != dragged.ObjectId)
                {
                    int spare = board.LeadVacantFollowing(markSocket);
                    if (spare >= 0)
                        board.Place(bumped.WithOrdinal(spare));
                }
                break;

            default:
                board.Place(dragged.WithOrdinal(markSocket));
                if (displaced is { } swapped
                    && swapped.ObjectId != dragged.ObjectId
                    && (uint)srcSocket < HotbarStore.SlotTally
                    && board.Occupant(srcSocket) is null)
                {
                    board.Place(swapped.WithOrdinal(srcSocket));
                }
                break;
        }

        return board.Edits;
    }

    public static HotbarEdit[] PlanWholePileCombine(
        IReadOnlyList<HotbarSlot?> sockets,
        uint formerObjectIdent,
        uint newObjectIdent)
    {
        ArgumentNullException.ThrowIfNull(sockets);
        if (sockets.Count != HotbarStore.SlotTally || formerObjectIdent is 0u || newObjectIdent is 0u)
            return [];

        Scratch board = new Scratch(sockets, anticipatedEdits: 3);
        int from = board.LeadHolding(formerObjectIdent);
        if (from < 0)
            return [];

        HotbarSlot moving = board.Occupant(from)!.Value;
        board.Vacate(from);
        board.VacateLeadHolding(newObjectIdent);
        board.Place(moving with { ObjectId = newObjectIdent });
        return board.Edits;
    }

    // A private copy of the bar plus the edit log that recreates it
    private sealed class Scratch
    {
        private readonly HotbarSlot?[] _chambers = new HotbarSlot?[HotbarStore.SlotTally];
        private readonly List<HotbarEdit> _trace;

        public Scratch(IReadOnlyList<HotbarSlot?> sockets, int anticipatedEdits)
        {
            for (int idx = 0; idx < _chambers.Length; ++idx)
                _chambers[idx] = sockets[idx];
            _trace = new List<HotbarEdit>(anticipatedEdits);
        }

        public HotbarEdit[] Edits => _trace.ToArray();

        // The entry shown in a slot; a zero object id renders as empty
        public HotbarSlot? Occupant(int socket) =>
            _chambers[socket] is { ObjectId: not 0u } listing ? listing : null;

        public void Vacate(int socket)
        {
            if (Occupant(socket) is null)
                return;
            _chambers[socket] = null;
            _trace.Add(HotbarEdit.Remove(socket));
        }

        public int LeadHolding(uint objectIdent)
        {
            for (int socket = 0; socket < _chambers.Length; ++socket)
            {
                if (Occupant(socket) is { } listing && listing.ObjectId == objectIdent)
                    return socket;
            }
            return -1;
        }

        public void VacateLeadHolding(uint objectIdent)
        {
            int socket = LeadHolding(objectIdent);
            if (socket >= 0)
                Vacate(socket);
        }

        public void Place(HotbarSlot listing)
        {
            _chambers[listing.Index] = listing;
            _trace.Add(HotbarEdit.Add(listing));
        }

        // Nearest visually empty slot to the right, wrapping round to the start
        public int LeadVacantFollowing(int socket)
        {
            for (int idx = socket + 1; idx < _chambers.Length; ++idx)
            {
                if (Occupant(idx) is null)
                    return idx;
            }
            for (int idx = 0; idx <= socket; ++idx)
            {
                if (Occupant(idx) is null)
                    return idx;
            }
            return -1;
        }
    }
}
