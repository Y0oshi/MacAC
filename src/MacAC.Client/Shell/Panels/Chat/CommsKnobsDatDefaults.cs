using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public static class CommsKnobsDatDefaults
{
    private const uint EnumBucket = 2u;

    // DAT enum-value key for the SECOND (sub-map) lookup
    private const uint EnumVal = 0x16u;

    public const uint DefaultDensityPropIdent = 0x10000080u;

    public const uint EngagedDensityPropIdent = 0x10000081u;

    public const float BackupDefaultDensity = 0.5f;

    public const float BackupEngagedDensity = 1.0f;

    public static bool TryRead(
        IDatAccess datFiles, out float defaultDensity, out float engagedDensity)
    {
        defaultDensity = BackupDefaultDensity;
        engagedDensity = BackupEngagedDensity;

        uint did = CanonDataIdResolver.Resolve(datFiles, EnumVal, EnumBucket);
        if (did is 0u || !datFiles.Portal.TryGet<PropertyBag>(did, out PropertyBag? props) || props is null)
            return false;

        bool ok = true;
        if (props.Properties.TryGetValue(DefaultDensityPropIdent, out PropertyValue? d)
            && d is FloatProperty defaultProp)
            defaultDensity = defaultProp.Value;
        else
            ok = false;

        if (props.Properties.TryGetValue(EngagedDensityPropIdent, out PropertyValue? a)
            && a is FloatProperty engagedProp)
            engagedDensity = engagedProp.Value;
        else
            ok = false;

        return ok;
    }
}
