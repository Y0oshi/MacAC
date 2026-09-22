namespace MacAC.Mechanics.Gear;

/// <summary>The retail vendor arithmetic; trade notes always trade at par (1.15 when selling).</summary>
public static class VendorPriceRules
{
    private const float NotePurchaseRate = 1f;
    private const float NoteVendRate = 1.15f;

    public static int PerUnitVal(int pileSumVal, int? dscPileDims) =>
        dscPileDims is > 0 and { } dims ? pileSumVal / dims : pileSumVal;

    public static int BuyPrice(int perUnitVal, uint gearKind, float purchaseRate, int qty)
    {
        float rate = gearKind == (uint)GearKind.PromissoryNote ? NotePurchaseRate : purchaseRate;
        int price = (int)Math.Floor((double)rate * perUnitVal * qty + 0.1);
        return price is 0 ? 1 : price >= 0 ? price : -1;
    }

    public static int SellPrice(int perUnitVal, uint gearKind, float vendRate, int qty)
    {
        float rate = gearKind == (uint)GearKind.PromissoryNote ? NoteVendRate : vendRate;
        int price = (int)Math.Ceiling((double)rate * perUnitVal * qty - 0.1);
        return price is 0 ? 1 : price > 0 ? price : -1;
    }
}
