using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Sound;

public static class WidgetSoundChartPicker
{
    public const uint WidgetSfxChartEnumSocket = 7u;

    public const uint WidgetSfxChartKindTag = 0x10000003u;

    public static uint Resolve(IDatAccess datFiles)
    {
        if (datFiles is null)
            return 0u;

        uint masterDid = (uint)datFiles.Portal.Db.Header.MasterMapId;
        if (masterDid is 0)
            return 0u;

        if (!datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master) || master is null)
            return 0u;

        if (!master.ClientEnumToId.TryGetValue(WidgetSfxChartEnumSocket, out uint perSocketDid)
            || perSocketDid is 0)

            return 0u;

        return !datFiles.Portal.TryGet<IdNameMap>(perSocketDid, out var perSocket) || perSocket is null
            ? 0u
            : perSocket.ClientEnumToId.TryGetValue(WidgetSfxChartKindTag, out uint did) ? did : 0u;
    }
}
