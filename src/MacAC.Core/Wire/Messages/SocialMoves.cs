using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

/// <summary>Queries, fellowship management, titles and character-option GameActions.</summary>
public static class SocialMoves
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint AskHealthOpcode = 0x01BFu;
    public const uint AskGearManaOpcode = 0x0263u;
    public const uint PingReqOpcode = 0x01E9u;
    public const uint FellowshipBuildOpcode = 0x00A2u;
    public const uint FellowshipQuitOpcode = 0x00A3u;
    public const uint FellowshipDismissOpcode = 0x00A4u;
    public const uint FellowshipRecruitOpcode = 0x00A5u;

    /// <summary>Panel visibility, not openness.</summary>
    public const uint FellowshipRefreshReqOpcode = 0x00A6u;

    public const uint FellowshipAssignNewLeaderOpcode = 0x0290u;

    /// <summary>The real openness toggle.</summary>
    public const uint FellowshipEditOpennessOpcode = 0x0291u;

    public const uint BannerSetOpcode = 0x002Cu;
    public const uint SetSingleToonKnobOpcode = 0x0005u;
    public const uint SetToonKnobsOpcode = 0x01A1u;

    // PlayerModule pack header bits: 8 spell lists, spellbook filters and the second options word are always present.
    private const uint BundleBase = 0x400u | 0x020u | 0x040u;
    private const uint BundleShortcuts = 0x001u;
    private const uint BundleWantedComps = 0x008u;
    private const int ArcanumTabs = 8;

    /// <summary>Server answers with UpdateHealth (0x01C0).</summary>
    public static byte[] AssembleAskHealth(uint seq, uint markOid) => One(seq, AskHealthOpcode, markOid);

    public static byte[] AssembleAskGearMana(uint seq, uint gearOid) => One(seq, AskGearManaOpcode, gearOid);

    public static byte[] AssemblePingReq(uint seq) => new GameActionScribe(seq, PingReqOpcode, 12).Bytes();

    public static byte[] AssembleFellowshipBuild(uint seq, string fellowshipLabel, bool portionXp)
    {
        ArgumentNullException.ThrowIfNull(fellowshipLabel);
        return new GameActionScribe(seq, FellowshipBuildOpcode).String16L(fellowshipLabel).Bool32(portionXp).Bytes();
    }

    public static byte[] AssembleFellowshipQuit(uint seq, bool disband) => Mark(seq, FellowshipQuitOpcode, disband);

    public static byte[] AssembleFellowshipDismiss(uint seq, uint markOid) => One(seq, FellowshipDismissOpcode, markOid);

    public static byte[] AssembleFellowshipRecruit(uint seq, uint markOid) => One(seq, FellowshipRecruitOpcode, markOid);

    public static byte[] AssembleFellowshipRefreshReq(uint seq, bool boardOpen) => Mark(seq, FellowshipRefreshReqOpcode, boardOpen);

    public static byte[] AssembleFellowshipAssignNewLeader(uint seq, uint newLeaderOid) => One(seq, FellowshipAssignNewLeaderOpcode, newLeaderOid);

    public static byte[] AssembleFellowshipEditOpenness(uint seq, bool isOpen) => Mark(seq, FellowshipEditOpennessOpcode, isOpen);

    public static byte[] AssembleBannerSet(uint seq, uint bannerIdent) => One(seq, BannerSetOpcode, bannerIdent);

    public static byte[] AssembleSetSingleToonKnob(uint seq, uint knobIdent, bool val)
    {
        return new GameActionScribe(seq, SetSingleToonKnobOpcode, 20).U32(knobIdent).Bool32(val).Bytes();
    }

    public static byte[] AssembleSetToonKnobs(
        uint seq,
        uint options1,
        uint options2,
        IReadOnlyList<HotbarSlot> shortcuts,
        IReadOnlyList<IReadOnlyList<uint>> favoriteSpells,
        IReadOnlyDictionary<uint, uint> wantedModules,
        uint grimoireFilters)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(favoriteSpells);
        ArgumentNullException.ThrowIfNull(wantedModules);
        if (favoriteSpells.Count != ArcanumTabs)
            throw new ArgumentException("The player-module payload always carries precisely 8 favorite-spell lists", nameof(favoriteSpells));

        uint preamble = BundleBase
            | (shortcuts.Count > 0 ? BundleShortcuts : 0u)
            | (wantedModules.Count > 0 ? BundleWantedComps : 0u);

        GameActionScribe scribe = new GameActionScribe(seq, SetToonKnobsOpcode, 128).U32(preamble).U32(options1);
        if (shortcuts.Count > 0)
        {
            scribe.U32((uint)shortcuts.Count);
            foreach (HotbarSlot socket in shortcuts)
                scribe.I32(socket.Index).U32(socket.ObjectId).U32(socket.SpellId);
        }
        for (int tab = 0; tab < ArcanumTabs; ++tab)
        {
            AssembleSetToonKnobsLoop(favoriteSpells, tab, scribe);
        }
        if (wantedModules.Count > 0)
        {
            scribe.U32((uint)wantedModules.Count);
            foreach ((uint ident, uint quantity) in wantedModules)
                scribe.U32(ident).U32(quantity);
        }
        return scribe.U32(grimoireFilters).U32(options2).Align4().Bytes();
    }

    private static void AssembleSetToonKnobsLoop(IReadOnlyList<IReadOnlyList<uint>> favoriteSpells, int tab, GameActionScribe scribe)
    {
        IReadOnlyList<uint>? roster = favoriteSpells[tab];
        int tally = roster?.Count ?? 0;
        scribe.U32((uint)tally);
        for (int idx = 0; idx < tally; ++idx)
            scribe.U32(roster![idx]);
    }

    private static byte[] One(uint seq, uint act, uint word) => new GameActionScribe(seq, act, 16).U32(word).Bytes();

    // A single boolean byte padded out to a word
    private static byte[] Mark(uint seq, uint act, bool val)
    {
        return new GameActionScribe(seq, act, 16).U8(val ? (byte)1 : (byte)0).Align4().Bytes();
    }
}

public enum CharacterOptionId : uint
{
    AutoRepeatAttack = 0x00,
    IgnoreAllegianceRequests = 0x01,
    IgnoreFellowshipRequests = 0x02,
    IgnoreTradeRequests = 0x03,
    DisableMostWeatherEffects = 0x04,
    PersistentAtDay = 0x05,
    AllowGive = 0x06,
    ViewCombatTarget = 0x07,
    ShowTooltips = 0x08,
    UseDeception = 0x09,
    ToggleRun = 0x0A,
    StayInChatMode = 0x0B,
    AdvancedCombatUI = 0x0C,
    AutoTarget = 0x0D,
    VividTargetingIndicator = 0x0E,
    FellowshipShareXP = 0x0F,
    AcceptLootPermits = 0x10,
    FellowshipShareLoot = 0x11,
    FellowshipAutoAcceptRequests = 0x12,
    SideBySideVitals = 0x13,
    CoordinatesOnRadar = 0x14,
    SpellDuration = 0x15,
    DisableHouseRestrictionEffects = 0x16,
    DragItemOnPlayerOpensSecureTrade = 0x17,
    DisplayAllegianceLogonNotifications = 0x18,
    UseChargeAttack = 0x19,
    UseCraftSuccessDialog = 0x1A,
    ListenToAllegianceChat = 0x1B,
    DisplayDateOfBirth = 0x1C,
    DisplayAge = 0x1D,
    DisplayChessRank = 0x1E,
    DisplayFishingSkill = 0x1F,
    DisplayNumberDeaths = 0x20,
    DisplayTimeStamps = 0x21,
    SalvageMultiple = 0x22,
    ListenToGeneralChat = 0x23,
    ListenToTradeChat = 0x24,
    ListenToLFGChat = 0x25,
    ListenToRoleplayChat = 0x26,
    AppearOffline = 0x27,
    DisplayNumberCharacterTitles = 0x28,
    MainPackPreferred = 0x29,
    LeadMissileTargets = 0x2A,
    UseFastMissiles = 0x2B,
    FilterLanguage = 0x2C,
    ConfirmVolatileRareUse = 0x2D,
    ListenToSocietyChat = 0x2E,
    ShowHelm = 0x2F,
    DisableDistanceFog = 0x30,
    UseMouseTurning = 0x31,
    ShowCloak = 0x32,
    LockUI = 0x33,
    HearPkDeathMessages = 0x34,
}
