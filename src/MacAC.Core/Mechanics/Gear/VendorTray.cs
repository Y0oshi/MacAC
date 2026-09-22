namespace MacAC.Mechanics.Gear;

/// <summary>One staged Buying/Selling-tab row - a shop/pack item guid plus a staged quantity.</summary>
public readonly record struct VendorTrayRow(uint ItemGuid, int Quantity);

public enum VendorTrayAddOutcome
{
    Added,
    Capped,
    Ignored,
}

/// <summary>The rows the player has lined up to buy or sell before pressing the button.</summary>
public sealed class VendorTray
{
    public const int UpperLinedQty = 0x1388;

    public const string TooMuchMsg =
        "I can't possibly sell you that much! Please be a little more reasonable.";

    private readonly List<VendorTrayRow> _ranks = [];

    public IReadOnlyList<VendorTrayRow> Listings => _ranks;
    public bool IsEmpty => _ranks.Count is 0;

    public event Action? Changed;

    /// <summary>Buying-tab add: a repeated guid accumulates, up to the vendor's cap.</summary>
    public VendorTrayAddOutcome Add(uint gearOid, int qty)
    {
        if (gearOid is 0u || qty <= 0)
            return VendorTrayAddOutcome.Ignored;

        int at = OrdinalOf(gearOid);
        if (at < 0)
        {
            _ranks.Add(new VendorTrayRow(gearOid, qty));
        }
        else
        {
            int sum = _ranks[at].Quantity + qty;
            if (sum > UpperLinedQty)
                return VendorTrayAddOutcome.Capped;
            _ranks[at] = new VendorTrayRow(gearOid, sum);
        }

        Changed?.Invoke();
        return VendorTrayAddOutcome.Added;
    }

    public VendorTrayAddOutcome Stage(uint gearOid, int qty)
    {
        if (gearOid is 0u || qty <= 0)
            return VendorTrayAddOutcome.Ignored;

        for (int at; (at = OrdinalOf(gearOid)) >= 0;)
            _ranks.RemoveAt(at);
        _ranks.Add(new VendorTrayRow(gearOid, qty));
        Changed?.Invoke();
        return VendorTrayAddOutcome.Added;
    }

    public bool Remove(uint gearOid, int quantity)
    {
        int at = OrdinalOf(gearOid);
        if (at < 0)
            return false;

        var rank = _ranks[at];
        if (quantity == -1 || quantity >= rank.Quantity)
            _ranks.RemoveAt(at);
        else
            _ranks[at] = rank with { Quantity = rank.Quantity - quantity };
        Changed?.Invoke();
        return true;
    }

    public bool TryGet(uint gearOid, out VendorTrayRow rank)
    {
        int at = OrdinalOf(gearOid);
        rank = at < 0 ? default : _ranks[at];
        return at >= 0;
    }

    /// <summary>Re-keys a staged row when the server swaps the item's guid (e.g. after a stack split).</summary>
    public bool Replace(uint gearOid, uint substituteOid)
    {
        if (gearOid is 0u || substituteOid is 0u || gearOid == substituteOid)
            return false;

        int at = OrdinalOf(gearOid);
        if (at < 0 || OrdinalOf(substituteOid) >= 0)
            return false;

        _ranks[at] = _ranks[at] with { ItemGuid = substituteOid };
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_ranks.Count is 0)
            return;
        _ranks.Clear();
        Changed?.Invoke();
    }

    private int OrdinalOf(uint gearOid)
    {
        for (int idx = 0; idx < _ranks.Count; ++idx)
        {
            if (_ranks[idx].ItemGuid == gearOid)
                return idx;
        }
        return -1;
    }
}
