using System.Collections.Concurrent;

namespace MacAC.Client.Graphics.Tenancy;

internal sealed class TenancyObservationDiary
{
    private readonly ConcurrentDictionary<
        (AssetRef Asset, TenancyObservationKind Kind),
        TenancyObservation> _queued = new();
    private readonly int _ceilingListings;

    public TenancyObservationDiary(int ceilingListings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingListings, 1);
        _ceilingListings = ceilingListings;
    }

    public int Count => _queued.Count;

    public bool TryBroadcast(in TenancyObservation observation)
    {
        if (!observation.Asset.IsValid)
            throw new ArgumentException(
                "Residency observations require a valid asset reference",
                nameof(observation));
        observation.Charges.Validate();

        var tag = (observation.Asset, observation.Kind);
        while (true)
        {
            if (_queued.TryGetValue(tag, out TenancyObservation latest))
            {
                if (observation.Frame < latest.Frame)
                    return true;
                if (_queued.TryUpdate(tag, observation, latest))
                    return true;
                continue;
            }

            if (_queued.Count >= _ceilingListings)
                return false;
            if (_queued.TryAdd(tag, observation))
                return true;
        }
    }

    public int BleedTo(List<TenancyObservation> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        int begin = dest.Count;
        foreach (KeyValuePair<
                     (AssetRef Asset, TenancyObservationKind Kind),
                     TenancyObservation> duo in _queued)
        {
            if (((ICollection<KeyValuePair<
                    (AssetRef Asset, TenancyObservationKind Kind),
                    TenancyObservation>>)_queued).Remove(duo))

                dest.Add(duo.Value);
        }

        return dest.Count - begin;
    }
}
