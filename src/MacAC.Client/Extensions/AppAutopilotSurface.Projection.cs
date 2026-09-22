using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Traits;
using MacAC.Sim;
using MacAC.Sim.Actors;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{

    internal static NavigationFix ProjectNavigationLocus(
        Locus locus)
    {
        uint chamberIdent = locus.ObjCellId;
        uint chunkX = (chamberIdent >> 24) & 0xFFu;
        uint chunkY = (chamberIdent >> 16) & 0xFFu;
        var own = locus.Frame.Origin;
        return new NavigationFix(
            chamberIdent,
            (((double)chunkX - 127d) * 192d + own.X - 84d) / 240d,
            (((double)chunkY - 127d) * 192d + own.Y - 84d) / 240d,
            own.Z / 240d,
            ApproachMath.FetchBearing(locus.Frame.Orientation),
            (chamberIdent & 0xFFFFu) is >= 1u and <= 0x40u);
    }

    internal PackEntry ProjectSatchelGear(
        SimCore core,
        ClientThing gear)
    {
        return new(
            gear.ObjectId,
            gear.WeenieClassIdent,
            gear.Name,
            (uint)gear.Type,
            gear.VesselTag,
            gear.WielderIdent,
            (uint)gear.ValidLocations,
            (uint)gear.CurrentlyEquippedLocale,
            gear.Useability ?? 0u,
            gear.TargetType ?? 0u,
            gear.PublicWeenieBitfield ?? 0u,
            gear.StackSize,
            gear.Structure,
            gear.MaxStructure,
            gear.SpellId
                ?? (gear.Properties.BlobIdents.TryGetValue(
                    (uint)TraitDataId.Spell,
                    out uint gearArcanum) ? gearArcanum : 0u),
            gear.Properties.FetchInt((uint)TraitInt.PetClass),
            gear.Properties.FetchInt((uint)TraitInt.SummoningMastery),
            gear.Properties.BlobIdents.TryGetValue(
                (uint)TraitDataId.ProcSpell,
                out uint procArcanum) ? procArcanum : 0u,
            gear.Properties.FetchBool((uint)PropBool.ProcSpellSelfTargeted),
            gear.Properties.FetchFloat((uint)TraitFloat.ProcSpellRate),
            gear.Properties.FetchInt((uint)TraitInt.WeaponSkill),
            gear.Properties.FetchInt((uint)TraitInt.DamageType),
            gear.Properties.FetchInt((uint)TraitInt.Damage),
            gear.Properties.FetchFloat((uint)TraitFloat.DamageVariance),
            gear.Properties.FetchInt((uint)TraitInt.UseRequiresSkill),
            gear.Properties.FetchInt((uint)TraitInt.UseRequiresSkillLevel),
            gear.Properties.FetchInt((uint)TraitInt.UseRequiresSkillSpec))
        {
            CombatUse = gear.CombatUse ?? 0,
            ItemSpellcraft = gear.Properties.FetchInt(
                (uint)TraitInt.ItemSpellcraft),
            WieldRequirements = gear.Properties.FetchInt(
                (uint)TraitInt.WieldRequirements),
            WieldAptitudeKind = gear.Properties.FetchInt(
                (uint)TraitInt.WieldSkilltype),
            WieldDifficulty = gear.Properties.FetchInt(
                (uint)TraitInt.WieldDifficulty),
            AttackType = gear.Properties.FetchInt((uint)TraitInt.AttackType),
            WeaponType = gear.Properties.FetchInt((uint)TraitInt.WeaponType),
            BoosterVital = gear.Properties.FetchInt((uint)TraitInt.BoosterEnum),
            BoostValue = gear.Properties.FetchInt((uint)TraitInt.BoostValue),
            MendKitModifier = gear.Properties.FetchFloat(
                (uint)TraitFloat.HealkitMod),
            AppraisedArcanumIdents = gear.AppraisedArcanumIds.Count is 0
                ? []
                : gear.AppraisedArcanumIds.ToArray(),
            GearDamage = gear.Properties.FetchInt((uint)TraitInt.GearDamage),
            GearHarmResistance = gear.Properties.FetchInt(
                (uint)TraitInt.GearDamageResist),
            GearCriticalChance = gear.Properties.FetchInt(
                (uint)TraitInt.GearCrit),
            GearCriticalResistance = gear.Properties.FetchInt(
                (uint)TraitInt.GearCritResist),
            GearCriticalHarm = gear.Properties.FetchInt(
                (uint)TraitInt.GearCritDamage),
            GearCriticalHarmResistance = gear.Properties.FetchInt(
                (uint)TraitInt.GearCritDamageResist),
            CeilingPileDims = gear.PileDimsUpper,
            VesselSocket = gear.VesselSlot,
            ItemsCapacity = gear.ItemsCapacity,
            ContainersCapacity = gear.ContainersCapacity,
            Burden = gear.Burden,
            Value = gear.Value,
            GearLatestMana = gear.Properties.FetchInt((uint)TraitInt.ItemCurMana),
            GearCeilingMana = gear.Properties.FetchInt((uint)TraitInt.ItemMaxMana),
            Workmanship = gear.Workmanship,
            MaterialType = gear.MaterialType ?? 0u,
            ObjectClass = ClassifyObject(gear),
            Swatches = ProjectSwatches(core, gear.ObjectId),
            IconId = gear.IconId,
        };
    }

    private static InventoryVerb Project(PackRequestKind sort)
    {
        return sort switch
        {
            PackRequestKind.Pickup => InventoryVerb.Pickup,
            PackRequestKind.PutInContainer =>
                InventoryVerb.PutInContainer,
            PackRequestKind.SplitToContainer =>
                InventoryVerb.SplitToContainer,
            PackRequestKind.Merge => InventoryVerb.Merge,
            PackRequestKind.Move => InventoryVerb.Move,
            PackRequestKind.DropToWorld =>
                InventoryVerb.DropToWorld,
            PackRequestKind.SplitToWorld =>
                InventoryVerb.SplitToWorld,
            PackRequestKind.Wield => InventoryVerb.Wield,
            PackRequestKind.Give => InventoryVerb.Give,
            _ => InventoryVerb.Unknown,
        };
    }

    private static SpellFacts Project(SpellMeta meta)
    {
        return new(
        meta.SpellId,
        meta.Name,
        meta.Family,
        meta.Generation,
        meta.Difficulty,
        meta.ManaCost,
        meta.Duration,
        SchoolAptitudeIdent(meta.SchoolIdent),
        meta.Description,
        meta.IsSelfTargeted,
        meta.IsBeneficial)
        {
            IsDebuff = meta.IsDebuff,
            IsOffensive = meta.IsOffensive,
            IsFellowship = meta.IsFellowship,
            IsUntargeted = meta.IsUntargeted,
            RequiresPivotTo = meta.Family is not (>= 222u and <= 235u)
            && !meta.IsUntargeted,
            IsMissile = meta.IsProjectile,
            IsHarmOverTime = (meta.Flags & (uint)SpellBits.DamageOverTime) != 0,
            RawFlagSet = meta.Flags,
            ArcanumKind = meta.SpellType,
            TargetMask = meta.TargetMask,
            BaseSpanConstant = meta.BaseRangeConstant,
            BaseSpanModifier = meta.BaseRangeModifier,
            EquationModuleIdents = meta.EquationModules,
            IconId = meta.IconId,
            Saying = meta.Saying,
            ModuleSet = new ComponentQuartet(
            meta.ComponentSet.Herb,
            meta.ComponentSet.Powder,
            meta.ComponentSet.Potion,
            meta.ComponentSet.Talisman),
        };
    }

    private WorldObjectEntry ProjectWorldObject(
        SimCore core,
        SimActorRecord? capture,
        ClientThing? gear,
        uint avatarIdent)
    {
        uint objectIdent = capture?.ServerGuid ?? gear!.ObjectId;
        Locus? src = capture?.KineticBody?.CellPosition
            ?? (capture is null ? null : TranslateLocus(capture.Snapshot.Position));
        bool possessed = gear is not null
            && IsAvatarPossessed(gear, avatarIdent, core.SatchelHolder.Objects);
        IReadOnlyList<uint> engagedArcana = objectIdent == avatarIdent
            ? EngagedEnchantments.Select(static enchantment => enchantment.SpellId).ToArray()
            : [];
        uint publicFlagSet = gear?.PublicWeenieBitfield ?? 0u;
        return new WorldObjectEntry(
            objectIdent,
            gear?.WeenieClassIdent ?? 0u,
            gear?.Name ?? capture?.Snapshot.Name ?? $"0x{objectIdent:X8}",
            ClassifyObject(gear),
            (uint)(gear?.Type ?? GearKind.None),
            gear?.VesselTag ?? 0u,
            gear?.WielderIdent ?? 0u)
        {
            IsOwned = possessed,
            IsScenery = src is not null
                && !possessed
                && (gear?.VesselTag ?? 0u) is 0u
                && (gear?.WielderIdent ?? 0u) is 0u,
            HasLocus = src is not null,
            Position = src is { } locus
                ? ProjectNavigationLocus(locus)
                : default,
            HasAppraisalBlob = gear is not null && HasPropBlob(gear.Properties),
            PreviousIdentMoment = gear?.PreviousAppraisalMomentMsec ?? 0,
            IsDoorOpen = (publicFlagSet & (uint)PublicWeenieBits.Door) is not 0u
                && (gear?.Properties.FetchBool((uint)PropBool.Open) ?? false),
            StackSize = Math.Max(1, gear?.StackSize ?? 1),
            ItemsCapacity = gear?.ItemsCapacity ?? 0,
            ContainersCapacity = gear?.ContainersCapacity ?? 0,
            ArcanumIdents = gear?.AppraisedArcanumIds.Count > 0
                ? gear.AppraisedArcanumIds.ToArray()
                : [],
            EngagedArcanumIdents = engagedArcana,
            IconId = gear?.IconId ?? 0u,
        };
    }

    private IReadOnlyList<PaletteFacts> ProjectSwatches(
        SimCore core,
        uint objectIdent)
    {
        IGenesisPaletteColorSource? tints;
        lock (_latch)
            tints = _swatchTints;
        if (tints is null
            || !core.EntityObjects.Entities.TryFetchEngaged(
                objectIdent,
                out SimActorRecord capture)
            || capture.Snapshot.SubPalettes.Count is 0)

            return Array.Empty<PaletteFacts>();

        PaletteFacts[] outcome = new PaletteFacts[capture.Snapshot.SubPalettes.Count];
        for (int ordinal = 0; ordinal < outcome.Length; ++ordinal)
        {
            ProjectSwatchesLoop(capture, ordinal, tints, outcome);
        }
        return outcome;
    }

    private void ProjectSwatchesLoop(SimActorRecord capture, int ordinal, IGenesisPaletteColorSource tints, PaletteFacts[] outcome)
    {
        var swatch = capture.Snapshot.SubPalettes[ordinal];
        int specimenOrdinal = (swatch.Length * 16) + (swatch.Offset * 32) + 8;
        _ = tints.TryFetchTint(
                        swatch.SubPaletteId,
                        specimenOrdinal,
                        out var rgb);
        outcome[ordinal] = new PaletteFacts(
                        swatch.SubPaletteId,
                        swatch.Offset,
                        swatch.Length,
                        rgb.R,
                        rgb.G,
                        rgb.B);
    }

    private static CombatPosture Project(FightingManner manner)
    {
        return manner switch
        {
            FightingManner.NonCombat => CombatPosture.Peace,
            FightingManner.Melee => CombatPosture.Melee,
            FightingManner.Missile => CombatPosture.Missile,
            FightingManner.Magic => CombatPosture.Magic,
            _ => CombatPosture.Unknown,
        };
    }

    private static StrikeHeight Project(AssaultElevation height)
    {
        return height switch
        {
            AssaultElevation.High => StrikeHeight.High,
            AssaultElevation.Low => StrikeHeight.Low,
            _ => StrikeHeight.Medium,
        };
    }

    private static AssaultElevation Project(StrikeHeight height)
    {
        return height switch
        {
            StrikeHeight.High => AssaultElevation.High,
            StrikeHeight.Low => AssaultElevation.Low,
            _ => AssaultElevation.Medium,
        };
    }
}
