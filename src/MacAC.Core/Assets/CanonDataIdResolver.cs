using MacAC.Dat;

namespace MacAC.Assets;

/// <summary>Walks the portal's master enum map to turn a client enum value into a data id.</summary>
public static class CanonDataIdResolver
{
    public static uint Resolve(IDatAccess datFiles, uint enumVal, uint enumBucket)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        uint masterDid = (uint)datFiles.Portal.Db.Header.MasterMapId;
        if (masterDid is 0)
            return 0u;

        // master map → category sub-map → value; any missing link resolves to nothing.
        return TryLookup(datFiles, masterDid, enumBucket, out uint subLookupDid) && TryLookup(datFiles, subLookupDid, enumVal, out uint did)
            ? did
            : 0u;
    }

    private static bool TryLookup(IDatAccess datFiles, uint lookupDid, uint tag, out uint val)
    {
        val = 0u;
        return datFiles.Portal.TryGet<IdNameMap>(lookupDid, out IdNameMap? lookup)
            && lookup is not null
            && lookup.ClientEnumToId.TryGetValue(tag, out val);
    }
}
