namespace MacAC.Wire.Messages;

public static class VendorAsks
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint PurchaseOpcode = 0x005Fu;
    public const uint VendOpcode = 0x0060u;

    public static byte[] BuildBuy(uint playActSeries, uint merchantOid, IReadOnlyList<(int Amount, uint ItemGuid)> gearList, uint alternateCurrencyIdent)
    {
        ArgumentNullException.ThrowIfNull(gearList);
        return Strokes(playActSeries, PurchaseOpcode, merchantOid, gearList, 24).U32(alternateCurrencyIdent).Bytes();
    }

    public static byte[] BuildBuy(uint playActSeries, uint merchantOid, int quantity, uint gearOid, uint alternateCurrencyIdent)
    {
        return BuildBuy(playActSeries, merchantOid, [(quantity, gearOid)], alternateCurrencyIdent);
    }

    public static byte[] AssembleVend(uint playActSeries, uint merchantOid, IReadOnlyList<(int Amount, uint ItemGuid)> gearList)
    {
        ArgumentNullException.ThrowIfNull(gearList);
        return Strokes(playActSeries, VendOpcode, merchantOid, gearList, 20).Bytes();
    }

    private static GameActionScribe Strokes(uint series, uint act, uint merchantOid, IReadOnlyList<(int Amount, uint ItemGuid)> gearList, int fixedOctets)
    {
        GameActionScribe scribe = new GameActionScribe(series, act, fixedOctets + gearList.Count * 8).U32(merchantOid).U32((uint)gearList.Count);
        foreach ((int quantity, uint oid) in gearList)
            scribe.I32(quantity).U32(oid);
        return scribe;
    }
}
