using System.Collections.Concurrent;

namespace MacAC.Mechanics.Gear;

/// <summary>Item mana fractions from QueryItemMana replies.</summary>
public sealed class ItemManaGauge
{
    private readonly ConcurrentDictionary<uint, float> _ratios = new();
    private long _rev;

    /// <summary>Raised for every reply, valid or not.</summary>
    public event Action<uint, float, bool>? ItemManaChanged;

    public int Count => _ratios.Count;

    public long Revision => Interlocked.Read(ref _rev);

    public float FetchManaPct(uint oid) => _ratios.GetValueOrDefault(oid, 0f);

    public bool HasMana(uint oid) => _ratios.ContainsKey(oid);

    public bool TryFetchManaPct(uint oid, out float ratio) => _ratios.TryGetValue(oid, out ratio);

    public void OnAskGearManaResponse(uint gearOid, float manaPct, bool valid)
    {
        if (valid)
            _ratios[gearOid] = manaPct;
        else
            _ratios.TryRemove(gearOid, out _);
        Interlocked.Increment(ref _rev);
        ItemManaChanged?.Invoke(gearOid, manaPct, valid);
    }

    public void Clear()
    {
        _ratios.Clear();
        Interlocked.Increment(ref _rev);
    }
}
