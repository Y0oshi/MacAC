using System.Globalization;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public static partial class GearAssayTextComposer
{
    private static void RevealValAndBurden(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        dossier.Line(props.Ints.TryGetValue(19u, out int val)
            ? $"Value: {val.ToString("N0", CultureInfo.InvariantCulture)}"
            : "Value: ???");
        dossier.Line(props.Ints.TryGetValue(5u, out int burden)
            ? $"Burden: {burden.ToString("N0", CultureInfo.InvariantCulture)}"
            : "Burden: Unknown");
    }

    private static void RevealTinkering(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        if (props.Ints.TryGetValue(171u, out int tinkers))
        {
            dossier.Line(
                $"This item has been tinkered {tinkers.ToString(CultureInfo.InvariantCulture)} "
                + (tinkers is 1 ? "time." : "times."));
        }

        string previousTinkeredBy = props.ObtainString(39u);
        if (!string.IsNullOrWhiteSpace(previousTinkeredBy))
            dossier.Line($"Last tinkered by {previousTinkeredBy}.");
        string imbuedBy = props.ObtainString(40u);
        RevealTinkeringRest(props, dossier, imbuedBy);
    }

    private static void RevealTinkeringRest(TraitBundle props, CanonDigestAssembler dossier, string imbuedBy)
    {
        if (!string.IsNullOrWhiteSpace(imbuedBy))
            dossier.Line($"Imbued by {imbuedBy}.");
        if (props.Ints.TryGetValue(105u, out int workmanship))
        {
            if (props.Ints.TryGetValue(170u, out int salvagedGearList)
                && salvagedGearList > 0)
            {
                double average = (double)workmanship / salvagedGearList;
                int workmanshipBand = Math.Clamp(
                    (int)Math.Round(average),
                    0,
                    10);
                dossier.Line(
                    $"Workmanship: {WorkmanshipAdjective(workmanshipBand)} "
                    + $"({average.ToString("0.00", CultureInfo.InvariantCulture)})");
                dossier.Paragraph(
                    $"Salvaged from {salvagedGearList.ToString(CultureInfo.InvariantCulture)} items.");
            }
            else
            {
                dossier.Line(
                    $"Workmanship: {WorkmanshipAdjective(Math.Clamp(workmanship, 0, 10))} "
                    + $"({workmanship.ToString(CultureInfo.InvariantCulture)})");
            }
        }
        dossier.BlankLine();
    }

    private static void RevealSetAndRatings(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        string? setLabel = props.Ints.TryGetValue(265u, out int setIdent)
            ? EquipmentSetLabel(setIdent)
            : null;
        bool setShown = setLabel is not null;
        if (setShown)
            dossier.Line($"Set: {setLabel}");

        (uint Property, string Label)[] ratingProps =
        [
            (370u, "Dam"),
            (371u, "Dam Resist"),
            (372u, "Crit"),
            (374u, "Crit Dam"),
            (373u, "Crit Resist"),
            (375u, "Crit Dam Resist"),
            (376u, "Heal Boost"),
            (377u, "Nether Resist"),
            (378u, "Life Resist"),
        ];
        string[] ratings = [.. ratingProps
            .Where(duo => props.Ints.TryGetValue(duo.Property, out int val)
                           && val > 0)
            .Select(duo =>
                $"{duo.Label} {props.FetchInt(duo.Property).ToString(CultureInfo.InvariantCulture)}")];
        bool ratingsShown = ratings.Length is not 0;
        if (ratingsShown)
            dossier.Line($"Ratings: {string.Join(", ", ratings)}");
        if (props.FetchInt(379u) is int vitality && vitality > 0)
        {
            dossier.Line(
                $"This item adds {vitality.ToString(CultureInfo.InvariantCulture)} Vitality.");
            ratingsShown = true;
        }

        if (ratingsShown)
            dossier.BlankLine();
        if (setShown || ratingsShown)
            dossier.BlankLine();
    }

    private static void RevealWeaponAndArmor(
        CanonDigestAssembler dossier,
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        uint validLocales = objRef.IsTap && appraisal.HookProfile is { } tap
            ? tap.ValidLocations
            : (uint)objRef.ValidLocations;
        uint ammoKind = objRef.IsTap && appraisal.HookProfile is { } tapProfile
            ? tapProfile.AmmoType
            : objRef.AmmoType ?? 0u;
        const uint weaponAndShieldLocales = 0x03F0_0000u;
        bool hasWeaponOrShieldLocale =
            (validLocales & weaponAndShieldLocales) is not 0u;

        if (hasWeaponOrShieldLocale
            && (validLocales & (uint)WieldBitmask.Shield) is not 0)
        {
            if (props.Ints.TryGetValue(28u, out int shieldTier))
                dossier.Line(
                    $"Base Shield Level: {shieldTier.ToString(CultureInfo.InvariantCulture)}",
                    EnchantmentStyling(
                        appraisal.ArmorEnchantments,
                        0x0001u));
            else
                dossier.Line("Shield Level: Unknown");
        }

        if (hasWeaponOrShieldLocale
            && appraisal.WeaponProfile is { } weapon)
        {
            RevealWeaponAndArmorBranch(weapon, props, dossier, validLocales, ammoKind, appraisal);
        }

        RevealAmmunitionBlurb(dossier, validLocales, ammoKind);

        if (!hasWeaponOrShieldLocale
            && (validLocales & 0x0800_7FFFu) is not 0u
            && ClothingCoverage(objRef.Priority) is { Length: > 0 } coverage)

            dossier.Line($"Covers {coverage}");
    }

    private static void RevealWeaponAndArmorBranch(AppraisalReader.WeaponSheet weapon, TraitBundle props, CanonDigestAssembler dossier, uint validLocales, uint ammoKind, AppraisalReader.WireParsed appraisal)
    {
        string aptitude = AptitudeLabel((int)weapon.WeaponSkill);
        int weaponKind = props.FetchInt(353u);
        dossier.Line(
                        $"Skill: {aptitude}{WeaponSubtype(weaponKind)}");
        bool launcher = (validLocales & (uint)WieldBitmask.MissileWeapon) is not 0
                                    && ammoKind is not 0u;
        string harmCaption = launcher ? "Damage Bonus" : "Damage";
        string harm;
        if (weapon.Damage == uint.MaxValue)
        {
            harm = "Unknown";
        }
        else
        {
            double floorHarm =
                (1d - weapon.DamageVariance) * weapon.Damage;
            harm = weapon.Damage - floorHarm > 0.0002d
                ? $"{ComposeCanonHarm(floorHarm)}"
                  + $" - {weapon.Damage.ToString(CultureInfo.InvariantCulture)}"
                : weapon.Damage.ToString(CultureInfo.InvariantCulture);
            if (!launcher)
            {
                harm += TryHarmKindLabel(weapon.DamageType, out string? kind)
                    ? $", {kind}"
                    : ", unknown type";
            }
        }
        var harmStyling = EnchantmentStyling(
                        appraisal.WeaponEnchantments,
                        0x0008u);
        if (harmStyling == GearAssayFontStyle.Normal)
        {
            harmStyling = EnchantmentStyling(
                appraisal.WeaponEnchantments,
                0x0010u);
        }
        dossier.Line($"{harmCaption}: {harm}", harmStyling);
        RevealWeaponAndArmorTail(weapon, props, dossier, launcher, validLocales, appraisal);
    }

    private static void RevealWeaponAndArmorTail(AppraisalReader.WeaponSheet weapon, TraitBundle props, CanonDigestAssembler dossier, bool launcher, uint validLocales, AppraisalReader.WireParsed appraisal)
    {
        int elementalBonus = props.FetchInt(204u);
        if (elementalBonus > 0)
            dossier.Line(
                $"Elemental Damage Bonus: "
                + $"{elementalBonus.ToString(CultureInfo.InvariantCulture)}, "
                + $"{HarmKindLabel(weapon.DamageType)}.");
        if (launcher)
        {
            dossier.Line(
                appraisal.Success
                    ? $"Damage Modifier: {ComposeModifier(weapon.DamageMod)}."
                    : "Damage Modifier: Unknown",
                EnchantmentStyling(
                    appraisal.WeaponEnchantments,
                    0x0020u));
        }
        const uint timedWeaponLocales =
                                (uint)(WieldBitmask.MeleeWeapon
                                       | WieldBitmask.MissileWeapon
                                       | WieldBitmask.TwoHanded);
        if ((validLocales & timedWeaponLocales) is not 0)
        {
            dossier.Line(
                weapon.WeaponTime == uint.MaxValue
                    ? "Speed:  Unknown"
                    : $"Speed: {WeaponMomentLabel((int)weapon.WeaponTime)} "
                      + $"({weapon.WeaponTime.ToString(CultureInfo.InvariantCulture)})",
                EnchantmentStyling(
                    appraisal.WeaponEnchantments,
                    0x0004u));
        }
        if (launcher)
        {
            if (appraisal.Success)
            {
                double rawSpan = Math.Min(
                    85d,
                    2d * Math.Pow(weapon.MaxVelocity, 2d)
                    * (1d / 9.8d)
                    * 1.094d);
                int span = rawSpan < 10d
                    ? (int)Math.Ceiling(rawSpan)
                    : (int)rawSpan - (int)rawSpan % 5;
                dossier.Line(
                    $"Range: {span.ToString(CultureInfo.InvariantCulture)} yds."
                    + (weapon.MaxVelocityEstimated is not 0u
                        ? " (based on STRENGTH 100)"
                        : string.Empty));
            }
            else
            {
                dossier.Line("Range:  Unknown");
            }
        }
        if (!launcher && Math.Abs(weapon.WeaponOffense - 1d) > 0.000001d)
            dossier.Line(
                $"Bonus to Attack Skill: "
                + $"{ComposeSignedPct(weapon.WeaponOffense - 1d)}.",
                EnchantmentStyling(
                    appraisal.WeaponEnchantments,
                    0x0001u));
    }

    private static void RevealArmorModifiers(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal)
    {
        if (appraisal.ArmorProfile is not { } armor
            || !appraisal.Properties.Ints.TryGetValue(28u, out int armorTier)
            || armorTier <= 0)

            return;

        dossier.Paragraph(
            $"Armor Level: {armorTier.ToString(CultureInfo.InvariantCulture)}",
            EnchantmentStyling(appraisal.ArmorEnchantments, 0x0001u));
        RevealProtection(
            dossier, "Slashing", armorTier, armor.SlashingProtection,
            EnchantmentStyling(appraisal.ArmorEnchantments, 0x0002u));
        RevealArmorModifiersRest(appraisal, armor, armorTier, dossier);
    }

    private static void RevealArmorModifiersRest(AppraisalReader.WireParsed appraisal, AppraisalReader.ArmorSheet armor, int armorTier, CanonDigestAssembler dossier)
    {
        RevealProtection(
                dossier, "Piercing", armorTier, armor.PiercingProtection,
                EnchantmentStyling(appraisal.ArmorEnchantments, 0x0004u));
        RevealProtection(
                    dossier, "Bludgeoning", armorTier, armor.BludgeoningProtection,
                    EnchantmentStyling(appraisal.ArmorEnchantments, 0x0008u));
        RevealArmorModifiersTail(appraisal, armor, armorTier, dossier);
    }

    private static void RevealArmorModifiersTail(AppraisalReader.WireParsed appraisal, AppraisalReader.ArmorSheet armor, int armorTier, CanonDigestAssembler dossier)
    {
        RevealProtection(
                dossier, "Fire", armorTier, armor.FireProtection,
                EnchantmentStyling(appraisal.ArmorEnchantments, 0x0020u));
        RevealProtection(
                    dossier, "Cold", armorTier, armor.ColdProtection,
                    EnchantmentStyling(appraisal.ArmorEnchantments, 0x0010u));
        RevealProtection(
                    dossier, "Acid", armorTier, armor.AcidProtection,
                    EnchantmentStyling(appraisal.ArmorEnchantments, 0x0040u));
        RevealProtection(
                    dossier, "Electric", armorTier, armor.LightningProtection,
                    EnchantmentStyling(appraisal.ArmorEnchantments, 0x0080u));
        RevealProtection(
                    dossier, "Nether", armorTier, armor.NetherProtection,
                    EnchantmentStyling(appraisal.ArmorEnchantments, 0x0100u));
    }

    private static void RevealAmmunitionBlurb(
        CanonDigestAssembler dossier,
        uint validLocales,
        uint ammoKind)
    {
        if (ammoKind is 0u)
            return;

        AmmoKind baseAmmoKind = (AmmoKind)ammoKind switch
        {
            AmmoKind.ArrowCrystal or AmmoKind.ArrowChorizite => AmmoKind.Arrow,
            AmmoKind.BoltCrystal or AmmoKind.BoltChorizite => AmmoKind.Bolt,
            AmmoKind.AtlatlCrystal or AmmoKind.AtlatlChorizite => AmmoKind.Atlatl,
            var another => another,
        };
        bool launcher = (validLocales & (uint)WieldBitmask.MissileWeapon) is not 0;
        string? blurb = (launcher, baseAmmoType: baseAmmoKind) switch
        {
            (true, AmmoKind.Arrow) => "Uses arrows as ammunition.",
            (true, AmmoKind.Bolt) => "Uses quarrels as ammunition.",
            (true, AmmoKind.Atlatl) => "Uses atlatl darts as ammunition.",
            (false, AmmoKind.Arrow) => "Used as ammunition by bows.",
            (false, AmmoKind.Bolt) => "Used as ammunition by crossbows.",
            (false, AmmoKind.Atlatl) => "Used as ammunition by atlatls.",
            _ => null,
        };
        if (blurb is not null)
            dossier.Line(blurb);
    }

    private static void RevealProtection(
        CanonDigestAssembler dossier,
        string harmKind,
        int armorTier,
        float modifier,
        GearAssayFontStyle styling)
    {
        string fidelity = modifier switch
        {
            <= 0.0002f => "None",
            < 0.4f => "Poor",
            < 0.8f => "Below Average",
            < 1.2f => "Average",
            < 1.6f => "Above Average",
            < 2.0f => "Excellent",
            _ => "Unparalleled",
        };
        double net = armorTier * modifier;
        dossier.Line(
            $"{harmKind}: {fidelity}  "
            + $"({net.ToString("0", CultureInfo.InvariantCulture)})",
            styling);
    }

    private static void RevealDefenseModifiers(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        RevealModifier(
            dossier,
            props,
            29u,
            "Bonus to Melee Defense",
            EnchantmentStyling(appraisal.WeaponEnchantments, 0x0002u));
        RevealModifier(dossier, props, 149u, "Bonus to Missile Defense");
        RevealModifier(dossier, props, 150u, "Bonus to Magic Defense");
    }

    private static void RevealModifier(
        CanonDigestAssembler dossier,
        TraitBundle props,
        uint prop,
        string caption,
        GearAssayFontStyle styling = GearAssayFontStyle.Normal)
    {
        if (!props.Floats.TryGetValue(prop, out double modifier)
            || Math.Abs(modifier - 1d) <= 0.000001d)
            return;
        dossier.Line(
            $"{caption}: {ComposeSignedPct(modifier - 1d)}.",
            styling);
    }

    private static void RevealShortMagicDetails(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal,
        Func<uint, SpellMeta?> locateArcanum)
    {
        if (appraisal.SpellBook.Length is 0)
            return;
        if (!appraisal.Success)
        {
            dossier.Paragraph("Spells: unknown.");
            return;
        }

        uint[] plain = [.. appraisal.SpellBook.Where(raw => (raw & 0x8000_0000u) == 0u)];
        if (plain.Length is 0)
            return;

        string labels = string.Join(
            ", ",
            plain.Select(raw =>
            {
                return locateArcanum(raw)?.Name
                       ?? $"Spell {raw.ToString(CultureInfo.InvariantCulture)}";
            }));
        dossier.Paragraph($"Spells: {labels}");
    }

    private static void RevealSpecialProps(
        CanonDigestAssembler dossier,
        TraitBundle props,
        CanonAssayNamePicker labels)
    {
        dossier.BlankLine();

        int carryThreshold = props.FetchInt(279u);
        if (carryThreshold > 0)
        {
            dossier.BlankLine();
            dossier.Line(
                $"You can only carry "
                + $"{carryThreshold.ToString("N0", CultureInfo.InvariantCulture)} "
                + "of these items.");
        }

        if (props.Floats.TryGetValue(167u, out double cooldown)
            && cooldown > 0d)
        {
            dossier.Line($"Cooldown When Used: {ComposeDiffMoment(cooldown)}");
            if (props.Ints.ContainsKey(280u))
                dossier.BlankLine();
        }

        int cleave = props.FetchInt(292u);
        if (cleave > 1)
        {
            dossier.Line(
                $"Cleave: {cleave.ToString(CultureInfo.InvariantCulture)} enemies in front arc.");
            dossier.BlankLine();
        }

        List<string> special = new List<string>();
        int slayer = props.FetchInt(166u);
        RevealSpecialPropsRest(dossier, props, special, slayer, labels);
    }

    private static void RevealSpecialPropsRest(CanonDigestAssembler dossier, TraitBundle props, List<string> special, int slayer, CanonAssayNamePicker labels)
    {
        if (slayer is not 0)
        {
            string slayerLabel = slayer is 31
                ? "Bael'Zharon's Hate"
                : labels.LocateBeast(slayer);
            if (!string.IsNullOrWhiteSpace(slayerLabel))
            {
                special.Add(slayer is 31
                    ? slayerLabel
                    : $"{slayerLabel} slayer");
            }
        }
        if ((props.FetchInt(47u) & 0x79E0) is not 0)
            special.Add("Multi-Strike");
        uint imbuedFxList = unchecked((uint)(
                    props.FetchInt(179u)
                    | props.FetchInt(303u)
                    | props.FetchInt(304u)
                    | props.FetchInt(305u)
                    | props.FetchInt(306u)));
        AffixImbuedFxList(special, imbuedFxList);
        if (props.Floats.ContainsKey(159u))
            special.Add("Magic Absorbing");
        if (props.FetchInt(36u) >= 9_999)
            special.Add("Unenchantable");
        int attuned = props.FetchInt(114u);
        RevealSpecialPropsTail(dossier, props, special, imbuedFxList, attuned);
    }

    private static void RevealSpecialPropsTail(CanonDigestAssembler dossier, TraitBundle props, List<string> special, uint imbuedFxList, int attuned)
    {
        if (attuned is 1 or 2)
            special.Add("Attuned");
        int bonded = props.FetchInt(33u);
        switch (bonded)
        {
            case -2:
                special.Add("Destroyed on Death");
                break;
            case -1:
                special.Add("Dropped on Death");
                break;
            case 1:
                special.Add("Bonded");
                break;
        }
        if (props.FetchBool(91u))
            special.Add("Retained");
        if (props.Floats.ContainsKey(136u))
            special.Add("Crushing Blow");
        if (props.Floats.ContainsKey(147u))
            special.Add("Biting Strike");
        if (props.Floats.ContainsKey(155u))
            special.Add("Armor Cleaving");
        RevealSpecialPropsCoda(special, props, dossier, imbuedFxList);
    }

    private static void RevealSpecialPropsCoda(List<string> special, TraitBundle props, CanonDigestAssembler dossier, uint imbuedFxList)
    {
        if (props.Floats.ContainsKey(157u)
                        && props.Ints.TryGetValue(263u, out int resistanceKind))
            special.Add(
                $"Resistance Cleaving: {HarmKindLabel((uint)resistanceKind)}");
        if (props.BlobIdents.ContainsKey(55u))
            special.Add("Cast on Strike");
        if (props.FetchBool(99u))
            special.Add("Ivoryable");
        if (props.FetchBool(100u))
            special.Add("Dyeable");
        if (special.Count is not 0)
            dossier.Line($"Properties: {string.Join(", ", special)}");
        if (imbuedFxList is not 0u)
            dossier.Line("This item cannot be further imbued.");
        if (props.FetchBool(130u))
            dossier.Line("This item is tethered to the left side.");
    }

    private static void RevealUsage(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        string use = props.ObtainString(14u);
        if (!string.IsNullOrWhiteSpace(use))
            dossier.Paragraph(use);
    }

    private static void RevealTierThresholds(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        int floor = props.FetchInt(86u);
        int ceiling = props.FetchInt(87u);
        if (floor > 0 && ceiling > 0)
        {
            dossier.Paragraph(floor == ceiling
                ? $"Restricted to characters of Level "
                  + $"{floor.ToString(CultureInfo.InvariantCulture)}."
                : $"Restricted to characters of Levels "
                  + $"{floor.ToString(CultureInfo.InvariantCulture)} to "
                  + $"{ceiling.ToString(CultureInfo.InvariantCulture)}.");
        }
        else if (floor > 0)
            dossier.Paragraph(
                $"Restricted to characters of Level "
                + $"{floor.ToString(CultureInfo.InvariantCulture)} or greater.");
        else if (ceiling > 0)
            dossier.Paragraph(
                $"Restricted to characters of Level "
                + $"{ceiling.ToString(CultureInfo.InvariantCulture)} or below.");

        string dest = props.ObtainString(38u);
        if (!string.IsNullOrWhiteSpace(dest))
            dossier.Paragraph($"Destination: {dest}");
    }

    private static void RevealWieldRequirements(
        CanonDigestAssembler dossier,
        TraitBundle props,
        CanonAssayNamePicker labels)
    {
        if (props.FetchBool(85u))
        {
            string holder = props.ObtainString(25u);
            dossier.Line(
                $"Wield requires "
                + $"{(string.IsNullOrWhiteSpace(holder) ? "the original owner" : holder)}");
        }
        if (props.FetchInt(26u) is 1)
            dossier.Line("Use requires Throne of Destiny.");

        if (props.Ints.TryGetValue(324u, out int lineage))
        {
            string lineageLabel = labels.LocateLineage(lineage);
            if (!string.IsNullOrEmpty(lineageLabel))
                dossier.Line($"Wield requires {lineageLabel}");
        }

        for (int ordinal = 0; ordinal < WieldRequirements.Length; ++ordinal)
        {
            (uint requirementIdent, uint statIdent, uint difficultyIdent) =
                WieldRequirements[ordinal];
            if (!props.Ints.TryGetValue(requirementIdent, out int requirement)
                || !props.Ints.TryGetValue(statIdent, out int stat)
                || !props.Ints.TryGetValue(difficultyIdent, out int difficulty))
                continue;

            string fidelity = RequirementFidelity(
                requirement,
                stat,
                difficulty,
                labels);
            if (requirement is 8)
            {
                string training = difficulty is 3
                    ? ordinal is 3 ? "Specialized" : "specialized"
                    : ordinal is 3 ? "Trained" : "trained";
                dossier.Line($"Wield requires {training} {fidelity}");
            }
            else if (requirement is 11)
            {
                dossier.Line($"Wield requires {fidelity} type");
            }
            else if (requirement is 12)
            {
                dossier.Line($"Wield requires {fidelity} race");
            }
            else if (!string.IsNullOrEmpty(fidelity))
            {
                dossier.Line(
                    $"Wield requires {fidelity} "
                    + $"{difficulty.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }

    private static void RevealUsageThresholds(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        int tier = props.FetchInt(369u);
        int aptitude = props.FetchInt(366u);
        int difficulty = props.FetchInt(367u);
        int specializedAptitude = props.FetchInt(368u);
        RevealUsageThresholdsRest(tier, aptitude, difficulty, specializedAptitude, dossier);
    }

    private static void RevealUsageThresholdsRest(int tier, int aptitude, int difficulty, int specializedAptitude, CanonDigestAssembler dossier)
    {
        if (tier > 0
                || (aptitude > 0 && difficulty > 0)
                || specializedAptitude > 0)

            dossier.BlankLine();
        if (tier > 0)
            dossier.Line(
                $"Use requires level {tier.ToString(CultureInfo.InvariantCulture)}.");
        if (aptitude > 0 && difficulty > 0)
            dossier.Line(
                $"Use requires {UsageAptitudeLabel(aptitude)} of at least "
                + $"{difficulty.ToString(CultureInfo.InvariantCulture)}.");
        if (specializedAptitude > 0)
            dossier.Line(
                $"Use requires specialized {UsageAptitudeLabel(specializedAptitude)}.");
    }

    private static void RevealGearTier(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        if (!props.Int64s.TryGetValue(5u, out long baseExperience)
            || baseExperience <= 0
            || !props.Ints.TryGetValue(319u, out int ceiling)
            || ceiling <= 0
            || !props.Ints.TryGetValue(320u, out int styling)
            || styling <= 0)

            return;

        long experience = Math.Max(0, props.FetchInt64(4u));
        int latestTier = GearSumXpToTier(
            experience,
            baseExperience,
            ceiling,
            styling);
        int displayedTier = Math.Min(latestTier + 1, ceiling);
        long upcomingExperience = GearTierToSumXp(
            Math.Min(latestTier + 1, ceiling),
            baseExperience,
            ceiling,
            styling);

        RevealGearTierRest(props, ceiling, experience, displayedTier, upcomingExperience, dossier);
    }

    private static void RevealGearTierRest(TraitBundle props, int ceiling, long experience, int displayedTier, long upcomingExperience, CanonDigestAssembler dossier)
    {
        dossier.Line(
                $"Item Level: {displayedTier.ToString(CultureInfo.InvariantCulture)} / "
                + $"{ceiling.ToString(CultureInfo.InvariantCulture)}");
        dossier.Line(
                    $"Item XP: {experience.ToString("N0", CultureInfo.InvariantCulture)} / "
                    + $"{upcomingExperience.ToString("N0", CultureInfo.InvariantCulture)}");
        dossier.BlankLine();
        if (props.FetchInt(352u) is 2)
        {
            dossier.Line(
                "This cloak has a chance to reduce an incoming attack by 200 damage.");
            dossier.BlankLine();
        }
    }

    private static void RevealActivationRequirements(
        CanonDigestAssembler dossier,
        TraitBundle props,
        bool appraisalSucceeded,
        CanonAssayNamePicker labels)
    {
        if (!appraisalSucceeded)
            return;

        List<string> requirements = new List<string>();
        AppendRequirement(requirements, "Arcane Lore", props.FetchInt(109u));
        AppendRequirement(requirements, "Allegiance Rank", props.FetchInt(110u));

        int lineage = props.FetchInt(188u);
        RevealActivationRequirementsRest(requirements, props, lineage, labels, dossier);
    }

    private static void RevealActivationRequirementsRest(List<string> requirements, TraitBundle props, int lineage, CanonAssayNamePicker labels, CanonDigestAssembler dossier)
    {
        if (lineage is not 0
                && labels.LocateLineage(lineage) is { Length: > 0 } lineageLabel)

            requirements.Add(lineageLabel);
        int aptitudeTier = props.FetchInt(115u);
        int aptitude = props.FetchInt(176u);
        if (aptitudeTier > 0 && aptitude > 0)
            requirements.Add(
                $"{UsageAptitudeLabel(aptitude)}: {aptitudeTier.ToString(CultureInfo.InvariantCulture)}");
        RevealActivationRequirementsTail(requirements, props, dossier);
    }

    private static void RevealActivationRequirementsTail(List<string> requirements, TraitBundle props, CanonDigestAssembler dossier)
    {
        int attrTier = props.FetchInt(258u);
        int attr = props.FetchInt(257u);
        if (attrTier > 0 && attr > 0)
            requirements.Add(
                $"{PrimaryAttrLabel(attr)}: "
                + $"{attrTier.ToString(CultureInfo.InvariantCulture)}");
        int secondaryTier = props.FetchInt(260u);
        RevealActivationRequirementsCoda(props, requirements, secondaryTier, dossier);
    }

    private static void RevealActivationRequirementsCoda(TraitBundle props, List<string> requirements, int secondaryTier, CanonDigestAssembler dossier)
    {
        int secondary = props.FetchInt(259u);
        if (secondaryTier > 0 && secondary > 0)
            requirements.Add(
                $"{SecondaryAttrLabel(secondary)}: "
                + $"{secondaryTier.ToString(CultureInfo.InvariantCulture)}");
        if (requirements.Count is not 0)
            dossier.Line($"Activation requires {string.Join(", ", requirements)}");
        if (props.FetchBool(94u))
        {
            string holder = props.ObtainString(25u);
            dossier.Line(
                $"This item can only be activated by "
                + $"{(string.IsNullOrWhiteSpace(holder) ? "the original owner" : holder)}.");
        }
    }

    private static void RevealInvokerBlob(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        if (props.Floats.TryGetValue(144u, out double manaConversion))
            dossier.Paragraph(
                $"Bonus to Mana Conversion: "
                + $"{ComposeSignedPct(manaConversion)}.",
                EnchantmentStyling(
                    appraisal.ResistEnchantments,
                    0x1000u));

        if (props.Floats.TryGetValue(152u, out double elemental)
            && props.Ints.TryGetValue(45u, out int harmKind))
        {
            dossier.Paragraph(
                $"Damage bonus for {HarmKindLabel((uint)harmKind)} spells:",
                EnchantmentStyling(
                    appraisal.ResistEnchantments,
                    0x2000u));
            dossier.Line($" vs. Monsters: {ComposeSignedPct(elemental - 1d)}.");
            double avatarModifier = 1d + (elemental - 1d) * 0.25d;
            dossier.Line(
                $" vs. Players: {ComposeSignedPct(avatarModifier - 1d)}.");
        }
    }

    private static void RevealBoostAndHealing(
        CanonDigestAssembler dossier,
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        bool healer =
            ((PublicWeenieBits)objRef.PublicWeenieBitfield.GetValueOrDefault()
             & PublicWeenieBits.Healer) != 0
            || objRef.IsTap
            && appraisal.HookProfile is { } tap
            && (tap.Flags & 0x2u) is not 0u;
        int boost = props.FetchInt(90u);
        string? boostPhrase = props.FetchInt(89u) switch
        {
            2 => $"{(boost >= 0 ? "Restores" : "Depletes")} "
                 + $"{Math.Abs(boost).ToString(CultureInfo.InvariantCulture)} "
                 + "Health when used.",
            4 => $"{(boost >= 0 ? "Restores" : "Depletes")} "
                 + $"{Math.Abs(boost).ToString(CultureInfo.InvariantCulture)} "
                 + "Stamina when consumed.",
            6 => $"{(boost >= 0 ? "Restores" : "Depletes")} "
                 + $"{Math.Abs(boost).ToString(CultureInfo.InvariantCulture)} "
                 + "Mana when used.",
            _ => null,
        };
        if (!healer && boostPhrase is not null
            && props.Ints.ContainsKey(90u))

            dossier.Paragraph(boostPhrase);

        if (!healer)
            return;

        if (props.Ints.TryGetValue(90u, out int healingBonus))
            dossier.Paragraph(
                $"Bonus to Healing Skill: {healingBonus.ToString(CultureInfo.InvariantCulture)}");
        if (props.Floats.TryGetValue(100u, out double mendKitModifier))
            dossier.Line(
                $"Restoration Bonus: "
                + $"{(mendKitModifier * 100d).ToString("0", CultureInfo.InvariantCulture)}%");
    }

    private static void RevealCapAndMutex(
        CanonDigestAssembler dossier,
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        if (!objRef.IsTap || appraisal.HookProfile is null)
        {
            RevealCapAndMutexBranch(objRef, dossier, props);
        }

        if (objRef.IsTap)
            return;

        bool hasBolted = props.Bools.TryGetValue(3u, out bool bolted);
        bool hasResistance = props.Ints.TryGetValue(
            38u,
            out int resistance);
        if (!hasBolted)
        {
            if (hasResistance && resistance is not 0)
            {
                dossier.Paragraph(
                    $"Bonus to Lockpick Skill: "
                    + $"{resistance.ToString("+0;-0;0", CultureInfo.InvariantCulture)}");
            }
            return;
        }

        if (!bolted)
        {
            dossier.Paragraph("Unlocked");
            return;
        }

        dossier.Paragraph("Locked");
        if (!hasResistance)
        {
            dossier.Paragraph("You can't tell how hard the lock is to pick.");
            return;
        }

        if (props.Ints.TryGetValue(173u, out int chance)
            && LockpickDifficulty(chance) is { } difficulty)
        {
            dossier.Paragraph(
                $"The lock looks {difficulty} to pick "
                + $"(Resistance {resistance.ToString(CultureInfo.InvariantCulture)}).");
        }
    }

    private static void RevealCapAndMutexBranch(ClientThing objRef, CanonDigestAssembler dossier, TraitBundle props)
    {
        if (objRef.ItemsCapacity > 0 && objRef.ContainersCapacity > 0)
            dossier.Paragraph(
                $"Can hold up to {objRef.ItemsCapacity.ToString(CultureInfo.InvariantCulture)} "
                + $"items and {objRef.ContainersCapacity.ToString(CultureInfo.InvariantCulture)} containers.");
        else if (objRef.ItemsCapacity > 0)
            dossier.Paragraph(
                $"Can hold up to {objRef.ItemsCapacity.ToString(CultureInfo.InvariantCulture)} items.");
        else if (objRef.ContainersCapacity > 0)
            dossier.Paragraph(
                $"Can hold up to {objRef.ContainersCapacity.ToString(CultureInfo.InvariantCulture)} containers.");
        int sheets = props.FetchInt(175u);
        int sheetsConsumed = props.FetchInt(174u);
        if (sheets > 0)
            dossier.Paragraph(
                $"{sheetsConsumed.ToString(CultureInfo.InvariantCulture)} of "
                + $"{sheets.ToString(CultureInfo.InvariantCulture)} pages full.");
    }

    private static void RevealManaStone(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal)
    {
        if (appraisal.SpellBook.Length is not 0)
            return;
        TraitBundle props = appraisal.Properties;
        if (props.Ints.TryGetValue(107u, out int storedMana))
            dossier.Line(
                $"Stored Mana: {storedMana.ToString(CultureInfo.InvariantCulture)}");
        if (props.Floats.TryGetValue(87u, out double efficiency))
            dossier.Line(
                $"Efficiency: {(efficiency * 100d).ToString("0", CultureInfo.InvariantCulture)}%");
        if (props.Floats.TryGetValue(137u, out double destruction))
            dossier.Line(
                $"Chance of Destruction: "
                + $"{(destruction * 100d).ToString("0", CultureInfo.InvariantCulture)}%");
    }

    private static void RevealLeftoverUses(
        CanonDigestAssembler dossier,
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal)
    {
        TraitBundle props = appraisal.Properties;
        if (props.Ints.TryGetValue(193u, out int tags))
            dossier.Line(
                $"Contains {tags.ToString(CultureInfo.InvariantCulture)} "
                + (tags is 1 ? "key." : "keys."));

        if (props.FetchBool(63u))
        {
            dossier.Line("Number of uses remaining:  Unlimited");
            return;
        }

        if (props.Ints.TryGetValue(92u, out int uses))
        {
            dossier.Line(
                $"Number of uses remaining: {uses.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        PublicWeenieBits publicFlagSet =
            (PublicWeenieBits)objRef.PublicWeenieBitfield.GetValueOrDefault();
        if (!appraisal.Success
            && ((publicFlagSet
                 & (PublicWeenieBits.Healer | PublicWeenieBits.Lockpick)) != 0
                || appraisal.HookProfile is { Flags: var flagSet }
                && (flagSet & 0xAu) is not 0))

            dossier.Paragraph("Number of uses remaining:  Unknown");
    }

    private static void RevealCraftsman(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        string craftsman = props.ObtainString(25u);
        if (!string.IsNullOrWhiteSpace(craftsman)
            && !props.FetchBool(85u)
            && !props.FetchBool(94u))
            dossier.Line($"Created by {craftsman}.");
    }

    private static void RevealSaleAndRareDetails(
        CanonDigestAssembler dossier,
        TraitBundle props)
    {
        if (props.Bools.TryGetValue(69u, out bool sellable) && !sellable)
            dossier.Line("This item cannot be sold.");
        if (props.FetchBool(108u))
        {
            dossier.Paragraph(
                "This rare item has a timer restriction of 3 minutes. "
                + "You will not be able to use another rare item with a timer "
                + "within 3 minutes of using this one.");
        }
        int rare = props.FetchInt(17u);
        if (rare > 0)
            dossier.Paragraph($"Rare #{rare.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void RevealMagicDetails(
        CanonDigestAssembler dossier,
        AppraisalReader.WireParsed appraisal,
        Func<uint, SpellMeta?> locateArcanum)
    {
        if (appraisal.SpellBook.Length is 0)
            return;
        if (!appraisal.Success)
        {
            dossier.Paragraph("Spells: unknown.");
            return;
        }

        var plain = new List<(uint Id, SpellMeta? Metadata)>();
        var enchantments = new List<(uint Id, SpellMeta? Metadata)>();
        RevealMagicDetailsRest(appraisal, dossier, plain, enchantments, locateArcanum);
    }

    private static void RevealMagicDetailsRest(AppraisalReader.WireParsed appraisal, CanonDigestAssembler dossier, List<(uint Id, SpellMeta? Metadata)> plain, List<(uint Id, SpellMeta? Metadata)> enchantments, Func<uint, SpellMeta?> locateArcanum)
    {
        foreach (uint rawArcanumIdent in appraisal.SpellBook)
        {
            uint arcanumIdent = rawArcanumIdent & 0x7FFF_FFFFu;
            var arcanum = (spellId: arcanumIdent, locateArcanum(arcanumIdent));
            if ((rawArcanumIdent & 0x8000_0000u) is 0)
                plain.Add(arcanum);
            else
                enchantments.Add(arcanum);
        }
        TraitBundle props = appraisal.Properties;
        if (plain.Count is not 0)
        {
            if (props.Ints.TryGetValue(106u, out int spellcraft))
                dossier.Line(
                    $"Spellcraft: {spellcraft.ToString(CultureInfo.InvariantCulture)}.");
            if (props.Ints.TryGetValue(107u, out int latestMana)
                && props.Ints.TryGetValue(108u, out int ceilingMana))
                dossier.Line(
                    $"Mana: {latestMana.ToString(CultureInfo.InvariantCulture)} / "
                    + $"{ceilingMana.ToString(CultureInfo.InvariantCulture)}.");

            if (props.Floats.TryGetValue(5u, out double manaRate)
                && Math.Abs(manaRate) > 0.000001d)
            {
                int secs = (int)Math.Round(1d / manaRate);
                dossier.Line(
                    $"Mana Cost: 1 point per {secs.ToString(CultureInfo.InvariantCulture)} "
                    + (secs is 1 ? "second." : "seconds."));
            }
            else if (props.Ints.TryGetValue(117u, out int manaPrice))
            {
                string manaPricePhrase =
                    $"Mana Cost: {manaPrice.ToString(CultureInfo.InvariantCulture)}.";
                if (manaPrice > 0)
                {
                    manaPricePhrase +=
                        "\n(Can be reduced by the Mana Conversion skill).";
                }
                dossier.Paragraph(manaPricePhrase);
            }

            dossier.Paragraph(AssembleArcanumBlurbChunk(
                "Spell Descriptions:",
                plain));
        }
        if (enchantments.Count is not 0)
        {
            dossier.Paragraph(AssembleArcanumBlurbChunk(
                "Enchantments:",
                enchantments));
        }
    }

    private static void RevealBlurb(
        CanonDigestAssembler dossier,
        TraitBundle props,
        CanonAssayNamePicker labels)
    {
        if (props.Ints.ContainsKey(267u)
            && props.Ints.ContainsKey(98u)
            && props.Ints.TryGetValue(268u, out int leftoverLifespan))
        {
            dossier.Line(leftoverLifespan < 0
                ? "This item is in the act of disintegrating."
                : $"This item expires in {ComposeExpiry(leftoverLifespan)}");
        }

        string longBlurb = props.ObtainString(16u);
        if (string.IsNullOrWhiteSpace(longBlurb))
        {
            string shortBlurb = props.ObtainString(15u);
            if (!string.IsNullOrWhiteSpace(shortBlurb))
                dossier.Paragraph(shortBlurb);
        }
        else
        {
            string blurb = props.ObtainString(52u);
            if (string.IsNullOrWhiteSpace(blurb))
                blurb = longBlurb;

            string stem = string.Empty;
            string suffix = string.Empty;
            if (props.Ints.TryGetValue(172u, out int decorations))
            {
                if ((decorations & 1) is not 0
                    && props.Ints.TryGetValue(105u, out int workmanship))
                {
                    stem += WorkmanshipAdjective(
                        Math.Clamp(workmanship, 0, 10)) + " ";
                }

                int matlKind = props.FetchInt(131u);
                string matl = labels.LocateMatl(matlKind);
                if (!string.IsNullOrWhiteSpace(matl))
                {
                    stem += matl + " ";
                    blurb = DropLead(blurb, matl).Trim();
                }

                if ((decorations & 4) is not 0
                    && props.Ints.TryGetValue(177u, out int gemTally)
                    && props.Ints.TryGetValue(178u, out int gemMatl))
                {
                    string gemLabel = gemTally is 1
                        ? labels.LocateMatl(gemMatl)
                        : PluralizedGemLabel(
                            gemMatl,
                            labels.LocateMatl(gemMatl));
                    if (!string.IsNullOrWhiteSpace(gemLabel))
                    {
                        suffix =
                            $", set with {gemTally.ToString(CultureInfo.InvariantCulture)} "
                            + gemLabel;
                    }
                }
            }

            dossier.Paragraph(stem + blurb + suffix);
        }

        int gatewayRestrictions = props.FetchInt(111u);
        if (gatewayRestrictions is not 0)
        {
            List<string> restrictions = new List<string>();
            if ((gatewayRestrictions & 2) is not 0)
                restrictions.Add("Player Killers may not use this portal.");
            if ((gatewayRestrictions & 4) is not 0)
                restrictions.Add("Lite Player Killers may not use this portal.");
            if ((gatewayRestrictions & 8) is not 0)
                restrictions.Add("Non-Player Killers may not use this portal.");
            if ((gatewayRestrictions & 0x20) is not 0)
                restrictions.Add("This portal cannot be recalled nor linked to.");
            if ((gatewayRestrictions & 0x10) is not 0)
                restrictions.Add("This portal cannot be summoned.");
            if (restrictions.Count is not 0)
                dossier.Paragraph(string.Join('\n', restrictions));
        }
    }
}
