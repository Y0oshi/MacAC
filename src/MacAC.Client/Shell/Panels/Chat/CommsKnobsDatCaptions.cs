using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public static class CommsKnobsDatCaptions
{
    private const uint EnumBucket = 2u;

    private const uint EnumVal = 0x15u;

    private const uint RegistryArrPropIdent = 0xD2u;
    private const uint ListingLabelPropIdent = 0xD4u;
    private const uint ListingHintPropIdent = 0xD5u;
    private const uint ListingOwningPropIdent = 0xD6u;

    public readonly record struct Legend(string? Name, string? Tooltip);

    public static bool TryRead(
        IDatAccess datFiles,
        DatStringPicker texts,
        out Legend defaultDensity,
        out Legend engagedDensity)
    {
        defaultDensity = default;
        engagedDensity = default;

        uint did = CanonDataIdResolver.Resolve(datFiles, EnumVal, EnumBucket);
        if (did is 0u || !datFiles.Portal.TryGet<PropertyBag>(did, out PropertyBag? props) || props is null)
            return false;

        if (!props.Properties.TryGetValue(RegistryArrPropIdent, out PropertyValue? arrProp)
            || arrProp is not ArrayProperty arr)
            return false;

        foreach (PropertyValue listing in arr.Items)
        {
            if (listing is not StructProperty listingStruct) continue;
            if (!listingStruct.Fields.TryGetValue(ListingOwningPropIdent, out PropertyValue? owningProp)
                || owningProp is not EnumProperty owningEnum)
                continue;

            if (owningEnum.Value == CommsKnobsDatDefaults.DefaultDensityPropIdent)
                defaultDensity = ScanLegend(listingStruct, texts);
            else if (owningEnum.Value == CommsKnobsDatDefaults.EngagedDensityPropIdent)
                engagedDensity = ScanLegend(listingStruct, texts);
        }

        return true;
    }

    private static Legend ScanLegend(StructProperty listing, DatStringPicker texts)
    {
        string? label = listing.Fields.TryGetValue(ListingLabelPropIdent, out PropertyValue? labelProp)
            && labelProp is TextInfoProperty labelDetails
                ? texts.Resolve(labelDetails.Value.TableId, labelDetails.Value.StringId, labelDetails.Value.Token)
                : null;
        string? hint = listing.Fields.TryGetValue(ListingHintPropIdent, out PropertyValue? hintProp)
            && hintProp is TextInfoProperty hintDetails
                ? texts.Resolve(hintDetails.Value.TableId, hintDetails.Value.StringId, hintDetails.Value.Token)
                : null;
        return new Legend(label, hint);
    }
}
