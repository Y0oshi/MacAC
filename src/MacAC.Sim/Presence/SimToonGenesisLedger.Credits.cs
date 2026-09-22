using MacAC.Mechanics.Genesis;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class SimToonGenesisLedger
{

    public GenesisSkillTrack GetSkillLevel(uint aptitudeIdent)
    {
        lock (_latch)
            return _aptitudes[aptitudeIdent];
    }

    internal bool TrySetAttr(GenesisTraitId attrIdent, int askedVal)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0)
                return false;

            int latest = TraitOf(attrIdent);
            int clamped = Math.Clamp(
                askedVal,
                GenesisAttributeRules.AttrLower,
                GenesisAttributeRules.AttrUpper);
            if (clamped > latest)
            {
                int absLeftover = CreditsLeftIgnoring(attrIdent);
                if (clamped - latest > absLeftover)
                    clamped = latest + absLeftover;
                if (clamped < latest)
                    clamped = latest;
            }

            PlaceTrait(attrIdent, clamped);
            RebalanceTraits(attrIdent);
            RecountTraitCredits();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TrySetAttrMutex(GenesisTraitId attrIdent, bool bolted)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            uint bit = 1u << ((int)attrIdent - 1);
            _boltedTraits = bolted
                ? _boltedTraits | bit
                : _boltedTraits & ~bit;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryTrainAptitude(uint aptitudeIdent) =>
        TryPutAptitudeTier(aptitudeIdent, GenesisSkillTrack.Trained);

    internal bool TrySpecializeAptitude(uint aptitudeIdent) =>
        TryPutAptitudeTier(aptitudeIdent, GenesisSkillTrack.Specialized);

    internal bool TryUntrainAptitude(uint aptitudeIdent) =>
        TryPutAptitudeTier(aptitudeIdent, GenesisSkillTrack.Untrained);
    private void RestartAptitudes(GenesisHeritageOptions lineage)
    {
        _aptitudeCreditsLeft = checked((int)_aptitudeCredits);
        if (_lineage is 0 || _gender is 0)
            return;

        for (uint aptitudeIdent = 1; aptitudeIdent < GenesisSkillTrackSet.SlotCount; ++aptitudeIdent)
        {
            if (!AptitudePriceOf(lineage, aptitudeIdent, out int trainedPrice, out int specializedPrice))
                continue;

            _aptitudes[aptitudeIdent] = trainedPrice > 0
                ? GenesisSkillTrack.Untrained
                : specializedPrice <= 0
                    ? GenesisSkillTrack.Specialized
                    : GenesisSkillTrack.Trained;
        }
    }

    private bool AptitudePriceOf(
        GenesisHeritageOptions lineage,
        uint aptitudeIdent,
        out int trainedPrice,
        out int specializedPrice)
    {
        if (lineage.SkillCostsBySkillId.TryGetValue(aptitudeIdent, out GenesisSkillPrice price)
            || _knobs.GlobalSkillCostsBySkillId.TryGetValue(aptitudeIdent, out price))
        {
            trainedPrice = price.NormalCost;
            specializedPrice = price.PrimaryCost;
            return true;
        }
        trainedPrice = 0;
        specializedPrice = 0;
        return false;
    }

    private int AptitudeSpend(GenesisHeritageOptions lineage)
    {
        return checked((int)_aptitudeCredits)
        - GenesisSkillCreditRules.CalculateSpent(
            _aptitudes,
            lineage.SkillCostsBySkillId,
            _knobs.GlobalSkillCostsBySkillId);
    }

    private int CreditsLeftIgnoring(GenesisTraitId queriedAttrIdent)
    {
        int sum = checked((int)_traitCredits);
        foreach (GenesisTraitId ident in BalanceOrdering)
        {
            bool useLatest = TraitBolted(ident) || ident == queriedAttrIdent;
            sum -= useLatest
                ? TraitOf(ident)
                : GenesisAttributeRules.AttrLower;
        }
        return sum;
    }

    private void RebalanceTraits(GenesisTraitId excluded)
    {
        int over = TraitSum() - checked((int)_traitCredits);
        if (over <= 0)
            return;

        bool begun = false;
        int guard = 6 * (GenesisAttributeRules.AttrUpper - GenesisAttributeRules.AttrLower) + 1;
        while (guard-- > 0)
        {
            foreach (GenesisTraitId ident in BalanceOrdering)
            {
                if (!begun)
                {
                    if ((int)_balanceCur != (int)ident)
                        continue;
                    begun = true;
                }
                if (ident == excluded)
                    continue;

                int val = TraitOf(ident);
                if (val > GenesisAttributeRules.AttrLower && !TraitBolted(ident))
                {
                    over -= 1;
                    PlaceTrait(ident, val - 1);
                    if (over <= 0)
                    {
                        int ordinal = Array.IndexOf(BalanceOrdering, ident);
                        _balanceCur = (int)(ordinal == BalanceOrdering.Length - 1
                            ? BalanceOrdering[0]
                            : BalanceOrdering[ordinal + 1]);
                        return;
                    }
                }
            }
            begun = true;
        }
    }

    private int TraitSum() => _attrs.Total;

    private int TraitOf(GenesisTraitId ident)
    {
        return ident switch
        {
            GenesisTraitId.Strength => _attrs.Strength,
            GenesisTraitId.Endurance => _attrs.Endurance,
            GenesisTraitId.Quickness => _attrs.Quickness,
            GenesisTraitId.Coordination => _attrs.Coordination,
            GenesisTraitId.Focus => _attrs.Focus,
            GenesisTraitId.Self => _attrs.Self,
            _ => 0,
        };
    }

    private void PlaceTrait(GenesisTraitId ident, int val)
    {
        _attrs = ident switch
        {
            GenesisTraitId.Strength => _attrs with { Strength = val },
            GenesisTraitId.Endurance => _attrs with { Endurance = val },
            GenesisTraitId.Quickness => _attrs with { Quickness = val },
            GenesisTraitId.Coordination => _attrs with { Coordination = val },
            GenesisTraitId.Focus => _attrs with { Focus = val },
            GenesisTraitId.Self => _attrs with { Self = val },
            _ => _attrs,
        };
    }

    private bool TraitBolted(GenesisTraitId ident) =>
        (_boltedTraits & (1u << ((int)ident - 1))) is not 0u;

    private void RecountTraitCredits()
    {
        _traitCreditsLeft =
            checked((int)_traitCredits) - TraitSum();
    }

    private bool TryPutAptitudeTier(uint aptitudeIdent, GenesisSkillTrack markClass)
    {
        if (aptitudeIdent is 0 || aptitudeIdent >= GenesisSkillTrackSet.SlotCount)
            return false;

        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0 || _gender is 0)
                return false;
            if (!_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? lineage))
                return false;
            if (!AptitudePriceOf(lineage, aptitudeIdent, out int trainedPrice, out int specializedPrice))
                return false;

            var earlier = _aptitudes[aptitudeIdent];
            if (earlier == markClass)
                return true;

            int leftover = _aptitudeCreditsLeft;
            leftover += earlier switch
            {
                GenesisSkillTrack.Trained => trainedPrice,
                GenesisSkillTrack.Specialized => specializedPrice,
                _ => 0,
            };
            leftover -= markClass switch
            {
                GenesisSkillTrack.Trained => trainedPrice,
                GenesisSkillTrack.Specialized => specializedPrice,
                _ => 0,
            };
            if (leftover < 0)
                return false;

            _aptitudes[aptitudeIdent] = markClass;
            _aptitudeCreditsLeft = leftover;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }
}
