using System.Numerics;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    private const uint LeapAptitudeIdent = 22u;
    private const uint ExecAptitudeIdent = 24u;

    private sealed partial class Binder
    {
        // The login-time player description seeds spells, properties, vitals, skills and both inventory
        // manifests
        public void AttachAvatarBlurb()
        {
            On(GameEventKind.PlayerDescription, PlayerDescReader.TryParse, parsed =>
        {
            OnToonKnobs?.Invoke(parsed.Options1, parsed.Options2, parsed.TrailerTruncated);
            OnWantedModules?.Invoke(parsed.DesiredComps);

            double receivedAt = ClientMoment();
            var enchantments = parsed.Enchantments.Select(listing => ToEngagedEnchantment(listing, receivedAt)).ToArray();
            Spellbook.ReplaceManifest(parsed.Spells, enchantments, parsed.HotbarSpells, parsed.DesiredComps, parsed.SpellbookFilters);

            if (PlayerGuid is not null)
                Items.UpsertProps(PlayerGuid(), parsed.Properties);

            // Primary attributes (1..6) as (start + ranks), for the skill formula bonus.
            var attrCurrents = new Dictionary<uint, uint>();
            foreach (var attr in parsed.Attributes)
            {
                if (attr.AtType is >= 1 and <= 6)
                    attrCurrents[attr.AtType] = attr.Ranks + attr.Start;
            }

            if (LocalPlayer is { } state)
                SeedSelf(state, parsed);

            if (LocalPlayer is not null || OnAptitudesUpdated is not null)
                SeedAptitudes(parsed, attrCurrents);

            SeedManifests(parsed);
            OnShortcuts?.Invoke(parsed.Shortcuts);
        });
        }

        private static void SeedSelf(SelfState state, PlayerDescReader.Parsed parsed)
        {
            state.OnProps(parsed.Properties);
            state.OnLoci(parsed.Positions.ToDictionary(
                static duo => duo.Key,
                static duo => new Locus(
                    duo.Value.LandblockId,
                    new Vector3(duo.Value.X, duo.Value.Y, duo.Value.Z),
                    new Quaternion(duo.Value.Qx, duo.Value.Qy, duo.Value.Qz, duo.Value.Qw))));

            foreach (var attr in parsed.Attributes)
            {
                if (attr.Current is { } latest)
                {
                    state.OnVitalRefresh(vitalIdent: attr.AtType, ranks: attr.Ranks, begin: attr.Start, xp: attr.Xp, latest: latest);
                }
                else
                {
                    // Endurance and Self feed the vital maxima (Endurance/2 → health, Endurance → stamina, Self → mana).
                    state.OnAttrRefresh(atKind: attr.AtType, ranks: attr.Ranks, begin: attr.Start, xp: attr.Xp);
                }
            }
        }

        private void SeedAptitudes(PlayerDescReader.Parsed parsed, IReadOnlyDictionary<uint, uint> attrCurrents)
        {
            int exec = -1, leap = -1;
            foreach (var s in parsed.Skills)
            {
                uint bonus = LocateAptitudeEquationBonus?.Invoke(s.SkillId, attrCurrents) ?? 0u;
                LocalPlayer?.OnAptitudeRefresh(
                    aptitudeIdent: s.SkillId,
                    ranks: s.Ranks,
                    condition: s.Status,
                    xp: s.Xp,
                    prime: s.Init,
                    resistance: s.Resistance,
                    previousConsumed: s.LastUsed,
                    equationBonus: bonus);

                if (s.SkillId is not (LeapAptitudeIdent or ExecAptitudeIdent))
                    continue;
                int sum = (int)(bonus + s.Init + s.Ranks);
                if (s.SkillId == ExecAptitudeIdent)
                    exec = sum;
                else
                    leap = sum;
            }
            if (exec >= 0 || leap >= 0)
                OnAptitudesUpdated?.Invoke(exec, leap);
        }

        // With a known owner the manifests are installed wholesale; without one, membership is only
        // recorded per item
        private void SeedManifests(PlayerDescReader.Parsed parsed)
        {
            uint holder = PlayerGuid?.Invoke() ?? 0u;
            if (holder is not 0u)
            {
                Items.BootstrapSatchelManifest(holder, Ranks(parsed.Inventory, static inv => new ContainerSlotRow(inv.Guid, inv.ContainerType)));
                Items.BootstrapEquipmentManifest(holder, Ranks(parsed.Equipped, static eq => new WornItemRow(eq.Guid, (WieldBitmask)eq.EquipLocation, eq.Priority)));
                return;
            }

            foreach (var inv in parsed.Inventory)
                Items.CaptureMembership(inv.Guid, vesselKindHint: inv.ContainerType);
            foreach (var eq in parsed.Equipped)
                Items.CaptureMembership(eq.Guid, wield: (WieldBitmask)eq.EquipLocation, precedence: eq.Priority);
        }
    }
}
