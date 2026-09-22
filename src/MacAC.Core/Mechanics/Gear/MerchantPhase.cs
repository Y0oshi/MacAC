namespace MacAC.Mechanics.Gear;

public readonly record struct VendorCatalogue(
    uint MerchandiseItemTypes,
    uint MerchandiseMinValue,
    uint MerchandiseMaxValue,
    bool DealMagicalItems,
    float BuyPrice,
    float SellPrice,
    uint AlternateCurrencyWcid,
    uint AlternateCurrencyAmount,
    string AlternateCurrencyPluralName);

public readonly record struct VendorWare(
    uint ItemGuid,
    int StackSize,
    uint WeenieClassId,
    string? Name,
    uint? ItemType,
    uint IconId,
    int? Value,
    int? DescStackSize = null,
    int? MaxStackSize = null,
    uint IconUnderlayId = 0u,
    uint IconOverlayId = 0u,
    uint Effects = 0u,
    string? PluralName = null);

public enum VendorShiftKind
{
    Opened,
    Refreshed,
    Closed,
    Reset,
}

public readonly record struct VendorShift(
    VendorShiftKind Kind,
    uint PreviousVendorId,
    uint VendorId);

/// <summary>The vendor whose shop is open, if any, and what it is selling.</summary>
public sealed class MerchantPhase
{
    public uint MerchantIdent { get; private set; }
    public VendorCatalogue Profile { get; private set; }
    public IReadOnlyList<VendorWare> Items { get; private set; } = [];

    public event Action<VendorShift>? Changed;

    public bool Apply(uint merchantOid, VendorCatalogue profile, IReadOnlyList<VendorWare> gearList)
    {
        ArgumentNullException.ThrowIfNull(gearList);
        if (merchantOid is 0u)
            return false;

        uint earlier = MerchantIdent;
        VendorShiftKind sort = earlier is not 0u && earlier == merchantOid
            ? VendorShiftKind.Refreshed
            : VendorShiftKind.Opened;

        MerchantIdent = merchantOid;
        Profile = profile;
        Items = gearList;

        Broadcast(new VendorShift(sort, earlier, merchantOid), "Apply()");
        return true;
    }

    public bool Close()
    {
        if (MerchantIdent is 0u)
            return false;

        uint earlier = MerchantIdent;
        Drop();
        Broadcast(new VendorShift(VendorShiftKind.Closed, earlier, 0u), "Close()");
        return true;
    }

    public bool Reset()
    {
        uint earlier = MerchantIdent;
        Drop();

        VendorShift shift = new VendorShift(VendorShiftKind.Reset, earlier, 0u);
        List<Exception>? misses = null;
        foreach (Action<VendorShift> listener in Listeners())
        {
            try { listener(shift); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }
        if (misses is not null)
            throw new AggregateException("One or more vendor-state reset observers failed", misses);
        return earlier is not 0u;
    }

    // Delivers a gameplay transition, logging (not propagating) observer faults
    private void Broadcast(VendorShift shift, string stage)
    {
        foreach (Action<VendorShift> listener in Listeners())
        {
            try { listener(shift); }
            catch (Exception problem)
            {
                Console.Error.WriteLine($"[MerchantPhase] {stage} observer threw: {problem.Message}");
            }
        }
    }

    private Delegate[] Listeners() => Changed?.GetInvocationList() ?? [];

    private void Drop()
    {
        MerchantIdent = 0u;
        Profile = default;
        Items = [];
    }
}
