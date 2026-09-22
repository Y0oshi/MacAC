using MacAC.Mechanics.Genesis;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class SimToonGenesisLedger
{
    internal bool TrySetLooksOrdinal(GenesisAppearanceSlot socket, uint ordinal)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged
                || !GenderKnobs(out GenesisSexOptions? gender))

                return false;

            if (ordinal != SimToonGenesisAppearance.Unset)
            {
                int tally = SocketTally(socket, gender);
                if (ordinal >= (uint)tally)
                    return false;
            }

            _looks = WithOrdinal(_looks, socket, ordinal);
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TrySetShade(GenesisShadeSlot socket, double val)
    {
        double clamped = Math.Clamp(val, 0.0, 1.0);
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            _looks = WithShade(_looks, socket, clamped);
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryRandomizeToon()
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            RollToon();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryRandomizeLooks()
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0 || _gender is 0)
                return false;
            RollLooks();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryRandomizeClothing()
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0 || _gender is 0)
                return false;
            RollClothing(excludeLatest: true);
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    private bool GenderKnobs(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GenesisSexOptions? gender)
    {
        gender = null;
        if (_lineage is 0 || _gender is 0)
            return false;
        if (!_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? lineage))
            return false;
        return lineage.GendersByKey.TryGetValue((int)_gender, out gender);
    }

    private static int SocketTally(
        GenesisAppearanceSlot socket,
        GenesisSexOptions gender)
    {
        return socket switch
        {
            GenesisAppearanceSlot.EyesStrip => gender.EyeStrips.Count,
            GenesisAppearanceSlot.NoseStrip => gender.NoseStrips.Count,
            GenesisAppearanceSlot.MouthStrip => gender.MouthStrips.Count,
            GenesisAppearanceSlot.HairStyle => gender.HairStyles.Count,
            GenesisAppearanceSlot.HairColor => gender.HairColors.Count,
            GenesisAppearanceSlot.EyeColor => gender.EyeColors.Count,
            GenesisAppearanceSlot.HeadgearStyle => gender.Headgears.Count,
            GenesisAppearanceSlot.ShirtStyle => gender.Shirts.Count,
            GenesisAppearanceSlot.TrousersStyle => gender.Pants.Count,
            GenesisAppearanceSlot.FootwearStyle => gender.Footwear.Count,
            GenesisAppearanceSlot.HeadgearColor
                or GenesisAppearanceSlot.ShirtColor
                or GenesisAppearanceSlot.TrousersColor
                or GenesisAppearanceSlot.FootwearColor => gender.ClothingColors.Count,
            _ => 0,
        };
    }

    private static SimToonGenesisAppearance WithOrdinal(
        SimToonGenesisAppearance looks,
        GenesisAppearanceSlot socket,
        uint ordinal)
    {
        return socket switch
        {
            GenesisAppearanceSlot.EyesStrip => looks with { EyesStrip = ordinal },
            GenesisAppearanceSlot.NoseStrip => looks with { NoseStrip = ordinal },
            GenesisAppearanceSlot.MouthStrip => looks with { MouthStrip = ordinal },
            GenesisAppearanceSlot.HairStyle => looks with { HairStyle = ordinal },
            GenesisAppearanceSlot.HairColor => looks with { HairColor = ordinal },
            GenesisAppearanceSlot.EyeColor => looks with { EyeColor = ordinal },
            GenesisAppearanceSlot.HeadgearStyle => looks with { HeadgearStyle = ordinal },
            GenesisAppearanceSlot.HeadgearColor => looks with { HeadgearColor = ordinal },
            GenesisAppearanceSlot.ShirtStyle => looks with { ShirtStyle = ordinal },
            GenesisAppearanceSlot.ShirtColor => looks with { ShirtColor = ordinal },
            GenesisAppearanceSlot.TrousersStyle => looks with { TrousersStyle = ordinal },
            GenesisAppearanceSlot.TrousersColor => looks with { TrousersColor = ordinal },
            GenesisAppearanceSlot.FootwearStyle => looks with { FootwearStyle = ordinal },
            GenesisAppearanceSlot.FootwearColor => looks with { FootwearColor = ordinal },
            _ => looks,
        };
    }

    private static SimToonGenesisAppearance WithShade(
        SimToonGenesisAppearance looks,
        GenesisShadeSlot socket,
        double val)
    {
        return socket switch
        {
            GenesisShadeSlot.Skin => looks with { SkinShade = val },
            GenesisShadeSlot.Hair => looks with { HairShade = val },
            GenesisShadeSlot.Headgear => looks with { HeadgearShade = val },
            GenesisShadeSlot.Shirt => looks with { ShirtShade = val },
            GenesisShadeSlot.Trousers => looks with { TrousersShade = val },
            GenesisShadeSlot.Footwear => looks with { FootwearShade = val },
            _ => looks,
        };
    }

    private void LimitLooksToGender()
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
        {
            _looks = SimToonGenesisAppearance.Default;
            return;
        }

        var appearance = _looks;
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.EyesStrip, LimitOrdinal(appearance.EyesStrip, gender.EyeStrips.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.NoseStrip, LimitOrdinal(appearance.NoseStrip, gender.NoseStrips.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.MouthStrip, LimitOrdinal(appearance.MouthStrip, gender.MouthStrips.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.HairStyle, LimitOrdinal(appearance.HairStyle, gender.HairStyles.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.HairColor, LimitOrdinal(appearance.HairColor, gender.HairColors.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.EyeColor, LimitOrdinal(appearance.EyeColor, gender.EyeColors.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.HeadgearStyle, LimitOrdinal(appearance.HeadgearStyle, gender.Headgears.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.HeadgearColor, LimitOrdinal(appearance.HeadgearColor, gender.ClothingColors.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.ShirtStyle, LimitOrdinal(appearance.ShirtStyle, gender.Shirts.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.ShirtColor, LimitOrdinal(appearance.ShirtColor, gender.ClothingColors.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.TrousersStyle, LimitOrdinal(appearance.TrousersStyle, gender.Pants.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.TrousersColor, LimitOrdinal(appearance.TrousersColor, gender.ClothingColors.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.FootwearStyle, LimitOrdinal(appearance.FootwearStyle, gender.Footwear.Count));
        appearance = WithOrdinal(appearance, GenesisAppearanceSlot.FootwearColor, LimitOrdinal(appearance.FootwearColor, gender.ClothingColors.Count));
        _looks = appearance;
    }

    private static uint LimitOrdinal(uint val, int tally)
    {
        if (val == SimToonGenesisAppearance.Unset)
            return val;
        return val >= (uint)tally
            ? unchecked((uint)(tally - 1))
            : val;
    }

    private int Roll(int lower, int upper)
    {
        if (lower == upper)
            return lower;
        int lo = Math.Min(lower, upper);
        int hi = Math.Max(lower, upper);
        return lo + _random.Next(hi - lo + 1);
    }

    private uint RollOrdinalExcluding(int tally, uint exclude)
    {
        if (tally <= 1)
            return 0u;
        uint outcome;
        do
        {
            outcome = (uint)_random.Next(tally);
        } while (outcome == exclude);
        return outcome;
    }

    private double RollShade() => _random.Next(32768) * (1.0 / 32767.0);

    private void RollLooks()
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
            return;

        var appearance = _looks;
        if (gender.EyeStrips.Count > 0)
            appearance = appearance with { EyesStrip = RollOrdinalExcluding(gender.EyeStrips.Count, appearance.EyesStrip) };
        if (gender.NoseStrips.Count > 0)
            appearance = appearance with { NoseStrip = RollOrdinalExcluding(gender.NoseStrips.Count, appearance.NoseStrip) };
        if (gender.MouthStrips.Count > 0)
            appearance = appearance with { MouthStrip = RollOrdinalExcluding(gender.MouthStrips.Count, appearance.MouthStrip) };
        appearance = appearance with { SkinShade = RollShade(), HairShade = RollShade() };
        if (gender.HairColors.Count > 0)
            appearance = appearance with { HairColor = RollOrdinalExcluding(gender.HairColors.Count, appearance.HairColor) };
        if (gender.EyeColors.Count > 0)
            appearance = appearance with { EyeColor = RollOrdinalExcluding(gender.EyeColors.Count, appearance.EyeColor) };
        if (gender.HairStyles.Count > 0)
            appearance = appearance with { HairStyle = RollOrdinalExcluding(gender.HairStyles.Count, appearance.HairStyle) };
        _looks = appearance;
    }

    private void RollHeadgear(bool excludeLatest)
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
            return;

        int stylingTally = gender.Headgears.Count;
        if (stylingTally > 0)
        {
            uint latest = _looks.HeadgearStyle;
            int latestPlusOne = latest == SimToonGenesisAppearance.Unset
                ? 0
                : (int)latest + 1;
            int rolled = excludeLatest
                ? (int)RollOrdinalExcluding(stylingTally + 1, (uint)latestPlusOne)
                : _random.Next(stylingTally + 1);
            uint newStyling = rolled is 0 ? SimToonGenesisAppearance.Unset : (uint)(rolled - 1);
            _looks = _looks with { HeadgearStyle = newStyling };
        }

        int tintTally = SocketTally(GenesisAppearanceSlot.HeadgearColor, gender);
        if (tintTally > 0)
        {
            _looks = _looks with
            {
                HeadgearColor = RollOrdinalExcluding(tintTally, _looks.HeadgearColor),
            };
        }
        _looks = _looks with { HeadgearShade = RollShade() };
    }

    private void RollShirt()
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
            return;
        int stylingTally = gender.Shirts.Count;
        if (stylingTally > 0)
        {
            _looks = _looks with
            {
                ShirtStyle = RollOrdinalExcluding(stylingTally, _looks.ShirtStyle),
            };
        }
        int tintTally = SocketTally(GenesisAppearanceSlot.ShirtColor, gender);
        if (tintTally > 0)
        {
            _looks = _looks with
            {
                ShirtColor = RollOrdinalExcluding(tintTally, _looks.ShirtColor),
            };
        }
        _looks = _looks with { ShirtShade = RollShade() };
    }

    private void RollTrousers()
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
            return;
        int stylingTally = gender.Pants.Count;
        if (stylingTally > 0)
        {
            _looks = _looks with
            {
                TrousersStyle = RollOrdinalExcluding(stylingTally, _looks.TrousersStyle),
            };
        }
        int tintTally = SocketTally(GenesisAppearanceSlot.TrousersColor, gender);
        if (tintTally > 0)
        {
            _looks = _looks with
            {
                TrousersColor = RollOrdinalExcluding(tintTally, _looks.TrousersColor),
            };
        }
        _looks = _looks with { TrousersShade = RollShade() };
    }

    private void RollFootwear()
    {
        if (!GenderKnobs(out GenesisSexOptions? gender))
            return;
        int stylingTally = gender.Footwear.Count;
        if (stylingTally > 0)
        {
            _looks = _looks with
            {
                FootwearStyle = RollOrdinalExcluding(stylingTally, _looks.FootwearStyle),
            };
        }
        int tintTally = SocketTally(GenesisAppearanceSlot.FootwearColor, gender);
        if (tintTally > 0)
        {
            _looks = _looks with
            {
                FootwearColor = RollOrdinalExcluding(tintTally, _looks.FootwearColor),
            };
        }
        _looks = _looks with { FootwearShade = RollShade() };
    }

    private void RollClothing(bool excludeLatest)
    {
        RollHeadgear(excludeLatest);
        RollShirt();
        RollTrousers();
        RollFootwear();
    }

    private void RollBlueprint()
    {
        if (_lineage is 0 || _gender is 0)
            return;
        if (!_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? lineage))
            return;

        if (_lineage == (uint)GenesisHeritage.Olthoi
            || _lineage == (uint)GenesisHeritage.OlthoiAcid)
        {
            ImposeBlueprint(lineage);
            return;
        }

        int tally = lineage.Templates.Count;
        if (tally <= 1)
            return;

        uint excludeShifted = unchecked(_blueprint - 1u);
        uint picked = RollOrdinalExcluding(tally - 1, excludeShifted) + 1u;
        _blueprint = picked;
        ImposeBlueprint(lineage);
    }

    private void RollToon()
    {
        Wipe();

        uint lineageIdent = (uint)Roll(1, 4);
        _lineage = lineageIdent;
        if (_knobs.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage))
            ChooseLineage(lineageIdent, lineage);

        uint genderTag = (uint)Roll(1, 2);
        ChooseGender(genderTag);

        RollLooks();
        RollHeadgear(excludeLatest: false);
        RollShirt();
        RollTrousers();
        RollFootwear();
        RollBlueprint();
        if (_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? finalLineage))
            RollBeginArea(finalLineage);
    }
}
