using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public sealed class MerchantShopItemAssembler : IDisposable
{
    private readonly MerchantPhase _merchant;
    private readonly ClientThingChart _objects;
    private readonly Dictionary<uint, uint> _merchantByWare = new();
    private bool _destroyed;

    public MerchantShopItemAssembler(MerchantPhase vendor, ClientThingChart objects)
    {
        _merchant = vendor ?? throw new ArgumentNullException(nameof(vendor));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _merchant.Changed += OnStockAltered;
    }

    public int PossessedTally => _merchantByWare.Count;

    /// <summary>True if <paramref name="oid"/> is a shop item this materializer put in the table.</summary>
    public bool Owns(uint oid) => _merchantByWare.ContainsKey(oid);

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _merchant.Changed -= OnStockAltered;
        _merchantByWare.Clear();
    }

    private void OnStockAltered(VendorShift shift)
    {
        var stock = _merchant.Items;
        HashSet<uint> listed = new HashSet<uint>(stock.Count);
        foreach (VendorWare ware in stock)
            listed.Add(ware.ItemGuid);

        var upcomingPossessed = new Dictionary<uint, uint>(stock.Count);
        try
        {
            RetireUnlisted(listed);
            foreach (VendorWare ware in stock)
            {
                if (!_merchantByWare.ContainsKey(ware.ItemGuid) && _objects.Get(ware.ItemGuid) is not null)
                {
                    Console.Error.WriteLine(
                        "[MerchantShopItemAssembler] skipped guid=0x" + ware.ItemGuid.ToString("X8")
                        + " - by now present in ClientThingChart and not owned by this vendor session");
                    continue;
                }
                _objects.Ingest(AsWeenie(ware, shift.VendorId));
                upcomingPossessed[ware.ItemGuid] = shift.VendorId;
            }
        }
        finally
        {
            _merchantByWare.Clear();
            foreach ((uint oid, uint merchantIdent) in upcomingPossessed)
                _merchantByWare[oid] = merchantIdent;
        }
    }

    // Removes wares we placed that are no longer listed and still sit in the vendor's container
    private void RetireUnlisted(HashSet<uint> listed)
    {
        foreach ((uint oid, uint merchantIdent) in _merchantByWare.ToArray())
        {
            if (listed.Contains(oid))
                continue;
            var online = _objects.Get(oid);
            if (online is null || online.VesselTag != merchantIdent)
                continue;
            try
            {
                _objects.Remove(oid);
            }
            catch (Exception problem)
            {
                System.Diagnostics.Trace.TraceError(
                    "[MerchantShopItemAssembler] ObjectRemoved observer threw retiring guid=0x{0}: {1}",
                    oid.ToString("X8"),
                    problem);
            }
        }
    }

    private static WeenieRecord AsWeenie(VendorWare ware, uint merchantIdent)
    {
        return new(
        Guid: ware.ItemGuid,
        Name: ware.Name,
        Type: ware.ItemType is { } t ? (GearKind)t : null,
        WeenieClassId: ware.WeenieClassId,
        IconId: ware.IconId,
        IconOverlayId: ware.IconOverlayId,
        IconUnderlayId: ware.IconUnderlayId,
        Effects: ware.Effects,
        Value: ware.Value,
        StackSize: VendorSplitRules.LocateAuthoredPileDims(ware.DescStackSize, ware.MaxStackSize),
        StackSizeMax: ware.MaxStackSize,
        Burden: null,
        ContainerId: merchantIdent,
        WielderId: 0u,
        ValidLocations: null,
        CurrentWieldedLocation: null,
        Priority: null,
        ItemsCapacity: null,
        ContainersCapacity: null,
        Structure: null,
        MaxStructure: null,
        Workmanship: null,
        PluralName: ware.PluralName);
    }
}
