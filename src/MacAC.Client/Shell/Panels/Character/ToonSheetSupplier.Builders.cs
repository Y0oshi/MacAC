using MacAC.Dat;
using MacAC.Client.Link;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ToonSheetSupplier
{
    public ToonSheet AssembleSheet()
    {
        if (!HasOnlineBlob())
            return _backupSheet?.Invoke(ToonLabel()) ?? new ToonSheet { Name = ToonLabel() };

        TraitBundle props = LatestAvatarProps();
        bool hasTier = props.Ints.TryGetValue(0x19u, out int tierVal);
        int tier = hasTier ? tierVal : 0;
        int? readoutTier = hasTier ? tierVal : null;
        long sumXp = props.FetchInt64(1u);
        long unassignedXp = props.FetchInt64(UnassignedXpPropIdent);
        var xp = CalculateTierXp(tier, sumXp);

        int aptitudeCredits = props.FetchInt(0x18u);

        return new ToonSheet
        {
            Name = AllegianceRankTitleChart.ConstructWholeLabel(
                props.FetchInt(AllegianceRankTitleChart.AllegianceGradePropIdent),
                props.FetchInt(ToonIdentityText.LineageClusterPropIdent),
                props.FetchInt(ToonIdentityText.GenderPropIdent),
                ToonLabel()),
            Level = readoutTier,
            Gender = ToonIdentityText.GenderReadoutLabel(
                props.FetchInt(ToonIdentityText.GenderPropIdent)),
            Heritage = ToonIdentityText.LineageClusterReadoutLabel(
                props.FetchInt(ToonIdentityText.LineageClusterPropIdent)),
            Title = _banners is not null && _locateReadoutBanner is not null
                ? _locateReadoutBanner(_banners.ReadoutBannerIdent)
                : null,
            PkCondition = PkConditionPhrase(LatestAvatarBitfield(), _locateWidgetString),
            SumXp = sumXp,
            XpToUpcomingTier = xp.toNext,
            XpRatio = xp.fraction,
            AvailableLuminance = props.FetchInt64(6u),
            MaximumLuminance = props.FetchInt64(7u),

            HealthCurrent = VitalLatest(SelfState.VitalSort.Health),
            HealthMax = VitalUpper(SelfState.VitalSort.Health),
            StaminaLatest = VitalLatest(SelfState.VitalSort.Stamina),
            StaminaUpper = VitalUpper(SelfState.VitalSort.Stamina),
            ManaCurrent = VitalLatest(SelfState.VitalSort.Mana),
            ManaMax = VitalUpper(SelfState.VitalSort.Mana),
            VitalBaseUpperVals =
            [
                VitalBaseUpper(SelfState.VitalSort.Health),
                VitalBaseUpper(SelfState.VitalSort.Stamina),
                VitalBaseUpper(SelfState.VitalSort.Mana),
            ],
            VitalVitaeModifiers =
            [
                _ownAvatar.FetchVitalVitaeModifier(SelfState.VitalSort.Health),
                _ownAvatar.FetchVitalVitaeModifier(SelfState.VitalSort.Stamina),
                _ownAvatar.FetchVitalVitaeModifier(SelfState.VitalSort.Mana),
            ],

            Strength = AttrNet(SelfState.StatKind.Strength),
            Endurance = AttrNet(SelfState.StatKind.Endurance),
            Coordination = AttrNet(SelfState.StatKind.Coordination),
            Quickness = AttrNet(SelfState.StatKind.Quickness),
            Focus = AttrNet(SelfState.StatKind.Focus),
            Self = AttrNet(SelfState.StatKind.Self),

            UnspentAptitudeCredits = aptitudeCredits,
            SpecializedAptitudeCredits = 0,
            ChessRank = props.FetchInt(0xB5u),
            FishingAptitude = props.FetchInt(0xC0u),
            BirthStamp = props.Ints.TryGetValue(0x62u, out int born)
                ? born
                : null,
            SumPlayMomentSecs = props.Ints.TryGetValue(0x7Du, out int played)
                ? played
                : null,
            Deaths = props.FetchInt(0x2Bu),
            AptitudeCredits = aptitudeCredits,
            ExpectingEmit = _expectingEmit,
            UnassignedXp = unassignedXp,
            AttrEmitPrices = AssembleAttrEmitPrices(quantity: 1),
            AttrRaise10Prices = AssembleAttrEmitPrices(quantity: 10),
            AttrBaseVals =
            [
                AttrLatest(SelfState.StatKind.Strength),
                AttrLatest(SelfState.StatKind.Endurance),
                AttrLatest(SelfState.StatKind.Coordination),
                AttrLatest(SelfState.StatKind.Quickness),
                AttrLatest(SelfState.StatKind.Focus),
                AttrLatest(SelfState.StatKind.Self),
            ],
            Skills = AssembleOnlineToonAptitudes(props),
            BurdenLatest = props.FetchInt(5u),
            BurdenUpper = props.FetchInt(96u),
            EncumbranceAugmentations = props.FetchInt(0xE6u),
            ToonDetailsProps = new Dictionary<uint, int>(props.Ints),
        };
    }

    private long[] AssembleAttrEmitPrices(int quantity)
    {
        var table = ExperienceTable;
        return
        [
            AttrEmitPrice(SelfState.StatKind.Strength),
            AttrEmitPrice(SelfState.StatKind.Endurance),
            AttrEmitPrice(SelfState.StatKind.Coordination),
            AttrEmitPrice(SelfState.StatKind.Quickness),
            AttrEmitPrice(SelfState.StatKind.Focus),
            AttrEmitPrice(SelfState.StatKind.Self),
            VitalEmitPrice(SelfState.VitalSort.Health),
            VitalEmitPrice(SelfState.VitalSort.Stamina),
            VitalEmitPrice(SelfState.VitalSort.Mana),
        ];

        long AttrEmitPrice(SelfState.StatKind sort)
        {
            SelfState.StatFrame? attr = _ownAvatar.FetchAttr(sort);
            return attr is null || table is null ? 0L : FirePriceFromXpCurve(table.Attributes, attr.Value.Ranks, attr.Value.Xp, quantity);
        }

        long VitalEmitPrice(SelfState.VitalSort sort)
        {
            SelfState.VitalFrame? vital = _ownAvatar.Get(sort);
            return vital is null || table is null ? 0L : FirePriceFromXpCurve(table.Vitals, vital.Value.Ranks, vital.Value.Xp, quantity);
        }
    }

    private IReadOnlyList<ToonSkill> AssembleOnlineToonAptitudes(
        TraitBundle props)
    {
        List<ToonSkill> outcome = new List<ToonSkill>();
        var aptitudeChart = SkillTable;
        var table = ExperienceTable;

        foreach (var capture in _ownAvatar.Skills.Values)
        {
            var advancement = AdvancementFromCondition(capture.Status);
            if (advancement == ToonSkillAdvancementClass.Inactive)
                continue;

            SkillSpec? aptitudeBase = null;
            if (aptitudeChart?.Skills is not null)
                aptitudeChart.Skills.TryGetValue((SkillId)capture.SkillId, out aptitudeBase);

            string? label = aptitudeBase?.Name;
            if (string.IsNullOrWhiteSpace(label))
                label = $"Skill {capture.SkillId}";

            uint glyph = aptitudeBase?.IconId ?? 0u;
            int trainedPrice = aptitudeBase?.TrainedCost ?? 0;
            int specializedPrice = aptitudeBase?.SpecializedCost ?? 0;
            long emitPrice = AptitudeEmitPrice(table, advancement, capture, 1);
            long raise10Price = AptitudeEmitPrice(table, advancement, capture, 10);
            string? hintPhrase = aptitudeBase is null ? null : CanonSkillFormula.AssembleHint(aptitudeBase);

            SkillRules.Val vals =
                _ownAvatar.FetchAptitudeVal(capture.SkillId, props)
                ?? new SkillRules.Val(
                    checked((int)Math.Min(int.MaxValue, capture.LatestTier)),
                    checked((int)Math.Min(int.MaxValue, capture.LatestTier)),
                    0);

            outcome.Add(new ToonSkill(
                capture.SkillId,
                label,
                glyph,
                advancement,
                vals.UnenchantedLevel,
                vals.EffectiveLevel,
                IsUsableUntrained(capture.SkillId),
                trainedPrice,
                specializedPrice,
                emitPrice,
                raise10Price,
                vals.VitaeModifier,
                hintPhrase));
        }

        return outcome;
    }
}
