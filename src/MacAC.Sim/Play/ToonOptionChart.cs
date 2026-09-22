using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct ToonOptionChartEntry(CharacterOptionId Id, bool IsOptions1, uint Mask, bool IsAutoSave, bool ClientDefault);

public static class ToonOptionChart
{
    private enum Word { One, Two }

    [Flags]
    private enum Trait { None = 0, AutoSave = 1, OnByDefault = 2 }

    public static bool TryGet(uint knobIdent, out ToonOptionChartEntry listing) => ById.TryGetValue(knobIdent, out listing);

    private static readonly ToonOptionChartEntry[] Ranks =
    [
        Row(CharacterOptionId.AutoRepeatAttack, Word.One, 0x00000002u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.IgnoreAllegianceRequests, Word.One, 0x00000004u, Trait.AutoSave),
        Row(CharacterOptionId.IgnoreFellowshipRequests, Word.One, 0x00000008u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.IgnoreTradeRequests, Word.One, 0x00020000u, Trait.None),
        Row(CharacterOptionId.DisableMostWeatherEffects, Word.One, 0x00010000u, Trait.None),
        Row(CharacterOptionId.PersistentAtDay, Word.Two, 0x00000001u, Trait.None),
        Row(CharacterOptionId.AllowGive, Word.One, 0x00000040u, Trait.OnByDefault),
        Row(CharacterOptionId.ViewCombatTarget, Word.One, 0x00000080u, Trait.None),
        Row(CharacterOptionId.ShowTooltips, Word.One, 0x00000100u, Trait.OnByDefault),
        Row(CharacterOptionId.UseDeception, Word.One, 0x00000200u, Trait.None),
        Row(CharacterOptionId.ToggleRun, Word.One, 0x00000400u, Trait.OnByDefault),
        Row(CharacterOptionId.StayInChatMode, Word.One, 0x00000800u, Trait.None),
        Row(CharacterOptionId.AdvancedCombatUI, Word.One, 0x00001000u, Trait.None),
        Row(CharacterOptionId.AutoTarget, Word.One, 0x00002000u, Trait.OnByDefault),
        Row(CharacterOptionId.VividTargetingIndicator, Word.One, 0x00008000u, Trait.OnByDefault),
        Row(CharacterOptionId.FellowshipShareXP, Word.One, 0x00040000u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.AcceptLootPermits, Word.One, 0x00080000u, Trait.AutoSave),
        Row(CharacterOptionId.FellowshipShareLoot, Word.One, 0x00100000u, Trait.AutoSave),
        Row(CharacterOptionId.FellowshipAutoAcceptRequests, Word.One, 0x20000000u, Trait.AutoSave),
        Row(CharacterOptionId.SideBySideVitals, Word.One, 0x00200000u, Trait.None),
        Row(CharacterOptionId.CoordinatesOnRadar, Word.One, 0x00400000u, Trait.OnByDefault),
        Row(CharacterOptionId.SpellDuration, Word.One, 0x00800000u, Trait.OnByDefault),
        Row(CharacterOptionId.DisableHouseRestrictionEffects, Word.One, 0x02000000u, Trait.None),
        Row(CharacterOptionId.DragItemOnPlayerOpensSecureTrade, Word.One, 0x04000000u, Trait.None),
        Row(CharacterOptionId.DisplayAllegianceLogonNotifications, Word.One, 0x08000000u, Trait.None),
        Row(CharacterOptionId.UseChargeAttack, Word.One, 0x10000000u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.UseCraftSuccessDialog, Word.One, 0x80000000u, Trait.None),
        Row(CharacterOptionId.ListenToAllegianceChat, Word.One, 0x40000000u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.DisplayDateOfBirth, Word.Two, 0x00000002u, Trait.None),
        Row(CharacterOptionId.DisplayAge, Word.Two, 0x00000020u, Trait.None),
        Row(CharacterOptionId.DisplayChessRank, Word.Two, 0x00000004u, Trait.None),
        Row(CharacterOptionId.DisplayFishingSkill, Word.Two, 0x00000008u, Trait.None),
        Row(CharacterOptionId.DisplayNumberDeaths, Word.Two, 0x00000010u, Trait.None),
        Row(CharacterOptionId.DisplayTimeStamps, Word.Two, 0x00000040u, Trait.None),
        Row(CharacterOptionId.SalvageMultiple, Word.Two, 0x00000080u, Trait.None),
        Row(CharacterOptionId.ListenToGeneralChat, Word.Two, 0x00000100u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.ListenToTradeChat, Word.Two, 0x00000200u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.ListenToLFGChat, Word.Two, 0x00000400u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.ListenToRoleplayChat, Word.Two, 0x00000800u, Trait.AutoSave),
        Row(CharacterOptionId.AppearOffline, Word.Two, 0x00001000u, Trait.AutoSave),
        Row(CharacterOptionId.DisplayNumberCharacterTitles, Word.Two, 0x00002000u, Trait.None),
        Row(CharacterOptionId.MainPackPreferred, Word.Two, 0x00004000u, Trait.None),
        Row(CharacterOptionId.LeadMissileTargets, Word.Two, 0x00008000u, Trait.AutoSave | Trait.OnByDefault),
        Row(CharacterOptionId.UseFastMissiles, Word.Two, 0x00010000u, Trait.AutoSave),
        Row(CharacterOptionId.FilterLanguage, Word.Two, 0x00020000u, Trait.None),
        Row(CharacterOptionId.ConfirmVolatileRareUse, Word.Two, 0x00040000u, Trait.None),
        Row(CharacterOptionId.ListenToSocietyChat, Word.Two, 0x00080000u, Trait.AutoSave),
        Row(CharacterOptionId.ShowHelm, Word.Two, 0x00100000u, Trait.AutoSave),
        Row(CharacterOptionId.DisableDistanceFog, Word.Two, 0x00200000u, Trait.None),
        Row(CharacterOptionId.UseMouseTurning, Word.Two, 0x00400000u, Trait.AutoSave),
        Row(CharacterOptionId.ShowCloak, Word.Two, 0x00800000u, Trait.AutoSave),
        Row(CharacterOptionId.LockUI, Word.Two, 0x01000000u, Trait.AutoSave),
        Row(CharacterOptionId.HearPkDeathMessages, Word.Two, 0x02000000u, Trait.None),
    ];

    private static readonly Dictionary<uint, ToonOptionChartEntry> ById = Ranks.ToDictionary(static entry => (uint)entry.Id);

    public static IReadOnlyList<ToonOptionChartEntry> All { get; } = [.. Ranks.OrderBy(static entry => (uint)entry.Id)];

    public static bool TryGet(CharacterOptionId knobIdent, out ToonOptionChartEntry listing) => TryGet((uint)knobIdent, out listing);

    private static ToonOptionChartEntry Row(CharacterOptionId ident, Word word, uint bitmask, Trait traits)
    {
        return new(ident, word == Word.One, bitmask, (traits & Trait.AutoSave) != 0, (traits & Trait.OnByDefault) != 0);
    }
}
