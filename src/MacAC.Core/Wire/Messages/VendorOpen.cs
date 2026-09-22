namespace MacAC.Wire.Messages;

public static class VendorOpen
{
    public readonly record struct VendorFacts(
        uint MerchandiseItemTypes,
        uint MerchandiseMinValue,
        uint MerchandiseMaxValue,
        bool DealMagicalItems,
        float BuyPrice,
        float SellPrice,
        uint AlternateCurrencyWcid,
        uint AlternateCurrencyAmount,
        string AlternateCurrencyPluralName);

    public readonly record struct ItemFacts(int StackSize, uint ItemGuid, WeenieDescBody Desc);

    public readonly record struct Parsed(uint VendorGuid, VendorFacts Profile, IReadOnlyList<ItemFacts> Items);

    private const int UpperGearList = 8192;
    private const int UpperLabelLen = 1024;

    public static Parsed? TryParse(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            uint merchant = cursor.Word();
            uint buckets = cursor.Word();
            uint lowerVal = cursor.Word();
            uint upperVal = cursor.Word();
            bool magic = cursor.Word() is not 0;
            if (!cursor.Has(8))
                return null;
            float purchase = cursor.F32();
            float vend = cursor.F32();
            uint currencyWcid = cursor.Word();
            uint currencyQuantity = cursor.Word();
            string currencyLabel = cursor.String16L(upperLen: UpperLabelLen);
            VendorFacts profile = new VendorFacts(buckets, lowerVal, upperVal, magic, purchase, vend, currencyWcid, currencyQuantity, currencyLabel);

            uint tally = cursor.Word();
            if (tally > UpperGearList || (long)tally * 12 > cursor.Left)
                return null;

            ItemFacts[] gearList = tally is 0 ? [] : new ItemFacts[tally];
            for (int idx = 0; idx < gearList.Length; ++idx)
            {
                // The stack size is a signed 24-bit value packed into a word
                uint dense = cursor.Word();
                int pile = unchecked((int)(dense << 8)) >> 8;
                uint oid = cursor.Word();
                var descriptor = WeenieDescReader.Decode(ref cursor);
                cursor.Align4();
                gearList[idx] = new ItemFacts(pile, oid, descriptor);
            }
            return new Parsed(merchant, profile, gearList);
        }
        catch
        {
            return null;
        }
    }
}
