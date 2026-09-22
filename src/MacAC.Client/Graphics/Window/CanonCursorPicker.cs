using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Shell;

namespace MacAC.Client.Graphics;

internal sealed class CanonCursorPicker(IDatAccess datFiles, object datMutex)
{
    private readonly IDatAccess _datFiles = datFiles;
    private readonly object _datMutex = datMutex;
    private readonly Dictionary<uint, uint> _didByEnum = [];
    private readonly HashSet<uint> _absentEnums = [];

    public bool TryResolve(CanonGlobalCursorKind sort, out WidgetCursorMedia cur)
    {
        cur = default;
        return !CanonCursorRegistry.TryFetchGlobalCur(sort, out var spec) ? false : TryLocate(spec, out cur);
    }

    internal bool TryLocate(CanonCursorSpec spec, out WidgetCursorMedia cur)
    {
        cur = default;
        if (!spec.IsValid)
            return false;

        if (_didByEnum.TryGetValue(spec.EnumId, out uint stashedDid))
        {
            cur = new WidgetCursorMedia(stashedDid, spec.HotspotX, spec.HotspotY);
            return true;
        }
        if (_absentEnums.Contains(spec.EnumId))
            return false;

        uint did = LocateDidByEnum(spec.EnumId);
        if (did is 0)
        {
            _absentEnums.Add(spec.EnumId);
            return false;
        }

        cur = new WidgetCursorMedia(did, spec.HotspotX, spec.HotspotY);
        _didByEnum[spec.EnumId] = did;
        return true;
    }

    private uint LocateDidByEnum(uint curEnum)
    {
        lock (_datMutex)
        {
            uint masterDid = (uint)_datFiles.Portal.Db.Header.MasterMapId;
            if (masterDid is 0)
                return 0;

            if (!_datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master) || master is null)
                return 0;

            if (!master.ClientEnumToId.TryGetValue(CanonCursorRegistry.CurEnumChart, out uint curLookupDid))
                return 0;

            return !_datFiles.Portal.TryGet<IdNameMap>(curLookupDid, out var curLookup) || curLookup is null
                ? 0
                : curLookup.ClientEnumToId.TryGetValue(curEnum, out uint did) ? did : 0;
        }
    }
}
