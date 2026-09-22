using MacAC.Mechanics.Gear;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Play;

public readonly record struct SimStashHoldingCapture(
    bool IsDisposed,
    bool TransactionsDisposed,
    bool ShortcutsDisposed,
    int BusyCount,
    bool HasPendingRequest,
    uint RequestedContainerId,
    uint CurrentContainerId,
    int ItemManaCount,
    int ShortcutCount,
    int ShortcutSubscriberCount,
    long ShortcutDispatchFailureCount,
    long TransactionDispatchFailureCount,
    int OpenedCorpseCount,
    uint VendorId,
    int MaterializedVendorItemCount)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed && TransactionsDisposed && ShortcutsDisposed
        && BusyCount is 0 && !HasPendingRequest
        && RequestedContainerId is 0u && CurrentContainerId is 0u
        && ItemManaCount is 0 && ShortcutCount is 0 && ShortcutSubscriberCount is 0
        && OpenedCorpseCount is 0 && VendorId is 0u && MaterializedVendorItemCount is 0;
        }
    }
}

public sealed class SimStashLedger : IDisposable
{
    private readonly SimActorObjectLifetime _entityObjects;
    private bool _destroyed;

    public SimStashLedger(SimActorObjectLifetime entityObjects)
    {
        _entityObjects = entityObjects ?? throw new ArgumentNullException(nameof(entityObjects));
        ExternalVessels = new OpenContainerState();
        _entityObjects.Objects.ObjectRemoved += OnObjectRemoved;
        ItemMana = new ItemManaGauge();
        Shortcuts = new HotbarStore();
        Transactions = new PackTransactionState(_entityObjects.Objects);
        Vendor = new MerchantPhase();
        MerchantGearList = new MerchantShopItemAssembler(Vendor, _entityObjects.Objects);
        View = new Lens(this);
    }

    public ClientThingChart Objects => _entityObjects.Objects;
    public OpenContainerState ExternalVessels { get; }
    public ItemManaGauge ItemMana { get; }
    public HotbarStore Shortcuts { get; }
    public PackTransactionState Transactions { get; }
    public MerchantPhase Vendor { get; }
    public MerchantShopItemAssembler MerchantGearList { get; }
    public ISimStashStateLens View { get; }
    public bool IsDisposed => _destroyed;

    public SimStashHoldingCapture CaptureOwnership()
    {
        return new(
        _destroyed,
        Transactions.IsDisposed,
        Shortcuts.IsDisposed,
        Transactions.OccupiedCount,
        Transactions.HasQueuedReq,
        ExternalVessels.AskedVesselIdent,
        ExternalVessels.LatestVesselIdent,
        ItemMana.Count,
        Shortcuts.Count,
        Shortcuts.SubscriberTally,
        Shortcuts.RelayMissTally,
        Transactions.RelayFailureCount,
        ExternalVessels.OpenedCorpseTally,
        Vendor.MerchantIdent,
        MerchantGearList.PossessedTally);
    }

    public void RestartExternalVessel() => ExternalVessels.Reset();
    public void RestartTransactions() => Transactions.ResetSession();
    public void RestartGearMana() => ItemMana.Clear();
    public void RestartMerchant() => Vendor.Reset();
    public void RestartAvatarCaptures() => Shortcuts.Clear();

    /// <summary>Sends the outbound first; the local slot is written even if the send throws.</summary>
    public bool TryAppendShortcut(HotbarSlot listing, Action broadcastOutgoing)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if ((uint)listing.Index >= HotbarStore.SlotTally || (listing.ObjectId is 0u && listing.SpellId is 0u))
            return false;

        try { broadcastOutgoing(); }
        finally { Shortcuts.Set(listing); }
        return true;
    }

    public bool TryDropShortcut(int ordinal, Action broadcastOutgoing)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if ((uint)ordinal >= HotbarStore.SlotTally)
            return false;

        try { broadcastOutgoing(); }
        finally { Shortcuts.Remove(ordinal); }
        return true;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        List<Exception>? misses = null;
        try
        {
            _entityObjects.Objects.ObjectRemoved -= OnObjectRemoved;
            foreach (Action hop in (Action[])[() => ExternalVessels.Reset(), () => Vendor.Reset(), MerchantGearList.Dispose, ItemMana.Clear, Shortcuts.Dispose, Transactions.Dispose])
            {
                try { hop(); }
                catch (Exception problem) { (misses ??= []).Add(problem); }
            }
        }
        finally
        {
            _destroyed = true;
        }
        if (misses is not null)
            throw new AggregateException("Runtime inventory state didn't converge during disposal", misses);
    }

    private void OnObjectRemoved(ClientThing gear) => ExternalVessels.AssignCorpseDeleted(gear.ObjectId);

    private sealed class Lens(SimStashLedger holder) : ISimStashStateLens
    {
        public SimStashStateCapture Snapshot
        {
            get
            {
                SimPendingStashRequestCapture? queued = holder.Transactions.TryFetchQueued(out PackRequestInFlight req)
                    ? new SimPendingStashRequestCapture(req.Token, (int)req.Kind, req.ItemId, req.Dispatched)
                    : null;
                return new SimStashStateCapture(
                    holder.ExternalVessels.AskedVesselIdent,
                    holder.ExternalVessels.LatestVesselIdent,
                    holder.Transactions.OccupiedCount,
                    holder.Transactions.CanCommenceReq,
                    queued,
                    holder.Shortcuts.Count,
                    holder.Shortcuts.Revision,
                    holder.ItemMana.Count,
                    holder.ItemMana.Revision);
            }
        }

        public bool TryFetchShortcut(int ordinal, out SimHotkeyCapture shortcut)
        {
            foreach (HotbarSlot socket in holder.Shortcuts.Items)
            {
                if (socket.Index == ordinal)
                {
                    shortcut = new SimHotkeyCapture(socket.Index, socket.ObjectId, socket.SpellId);
                    return true;
                }
            }
            shortcut = default;
            return false;
        }

        public bool TryFetchGearMana(uint objectIdent, out float ratio) => holder.ItemMana.TryFetchManaPct(objectIdent, out ratio);
    }
}
