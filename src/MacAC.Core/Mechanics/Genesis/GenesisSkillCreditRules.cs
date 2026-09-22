namespace MacAC.Mechanics.Genesis;

public static class GenesisSkillCreditRules
{
    public static int CalculateSpent(
        GenesisSkillTrackSet advancement,
        IReadOnlyDictionary<uint, GenesisSkillPrice> pricesByAptitudeIdent,
        IReadOnlyDictionary<uint, GenesisSkillPrice> globalPricesByAptitudeIdent)
    {
        ArgumentNullException.ThrowIfNull(advancement);
        ArgumentNullException.ThrowIfNull(pricesByAptitudeIdent);
        ArgumentNullException.ThrowIfNull(globalPricesByAptitudeIdent);

        int spent = 0;
        for (uint aptitudeIdent = 1; aptitudeIdent < GenesisSkillTrackSet.SlotCount; ++aptitudeIdent)
        {
            var follow = advancement[aptitudeIdent];
            if (follow is not (GenesisSkillTrack.Trained or GenesisSkillTrack.Specialized))
                continue;

            // The heritage's own price list wins; the global list is the fallback.
            if (!pricesByAptitudeIdent.TryGetValue(aptitudeIdent, out GenesisSkillPrice price)
                && !globalPricesByAptitudeIdent.TryGetValue(aptitudeIdent, out price))
                continue;

            spent += follow == GenesisSkillTrack.Specialized ? price.PrimaryCost : price.NormalCost;
        }
        return spent;
    }

    public static int RemainingCredits(
        uint sumAptitudeCredits,
        GenesisSkillTrackSet advancement,
        IReadOnlyDictionary<uint, GenesisSkillPrice> pricesByAptitudeIdent,
        IReadOnlyDictionary<uint, GenesisSkillPrice> globalPricesByAptitudeIdent)
    {
        return checked((int)sumAptitudeCredits) - CalculateSpent(advancement, pricesByAptitudeIdent, globalPricesByAptitudeIdent);
    }
}
