using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fellows;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Shell;

public sealed partial class ClientDirectiveDriver
{
    public sealed record AdministrationWiring(
        Action<string, bool> BreakAllegianceBoot,
        Action<string, string> AllegianceChatBoot,
        Action<string, bool> AllegianceChatGag,
        Action<string> AllegianceBroadcast,
        Action ListAllegianceBans,
        Action<string> AddAllegianceBan,
        Action<string> RemoveAllegianceBan,
        Action ListAllegianceOfficers,
        Action ClearAllegianceOfficers,
        Action<string, uint> SetAllegianceOfficer,
        Action<string> RemoveAllegianceOfficer,
        Action ListAllegianceOfficerTitles,
        Action ClearAllegianceOfficerTitles,
        Action<uint, string> SetAllegianceOfficerTitle,
        Action QueryAllegianceName,
        Action<string> SetAllegianceName,
        Action ClearAllegianceName,
        Action<uint> AllegianceLockAction,
        Action<string> SetAllegianceApprovedVassal,
        Action<uint> AllegianceHouseAction,
        Action QueryMotd,
        Action<string> SetMotd,
        Action ClearMotd,
        Action<bool> SetOpenHouseStatus,
        Action<string> AddPermanentGuest,
        Action<string> RemovePermanentGuest,
        Action RemoveAllPermanentGuests,
        Action<string, bool> ChangeStoragePermission,
        Action AddAllStoragePermission,
        Action RemoveAllStoragePermission,
        Action RequestFullGuestList,
        Action<string> BootSpecificHouseGuest,
        Action BootEveryone,
        Action<bool> SetHooksVisibility,
        Action<bool> ModifyAllegianceGuestPermission,
        Action<bool> ModifyAllegianceStoragePermission);

    public sealed record Mappings(
        Action TeleportToLifestone,
        Action TeleportToMarketplace,
        Action TeleportToPkArena,
        Action TeleportToPkLiteArena,
        Action TeleportToHouse,
        Action TeleportToMansion,
        Action QueryAge,
        Action QueryBirth,
        Action ToggleFrameRate,
        Action ToggleUiLock,
        Action<string> ShowSystemMessage,
        Action<string> ShowClientLocalMessage,
        Action<uint> ShowWeenieError,
        Func<uint?> PlayerPublicWeenieBitfield,
        Func<string> ClientVersion,
        Func<Locus?> CurrentPosition,
        Func<Locus?> LastOutsideCorpsePosition,
        Action<string, Action<bool>> ShowConfirmation,
        Action Suicide,
        Action<bool> ClearChat,
        Func<string, ChatTranscriptResult> SetChatLogFile,
        Action<string> SaveUi,
        Action<string> LoadUi,
        Action SaveAutoUi,
        Action LoadAutoUi,
        Func<bool> IsAway,
        Action<bool> SetAway,
        Action<string> SetAwayMessage,
        Func<bool> AcceptLootPermits,
        Action<bool> SetAcceptLootPermits,
        Action DisplayConsent,
        Action ClearConsent,
        Action<string> RemoveConsent,
        Action<string> SendEmote,
        FriendsLedger Friends,
        Action<string> AddFriend,
        Action<uint> RemoveFriend,
        Action ClearFriends,
        Action RequestLegacyFriends,
        SquelchLedger Squelch,
        Action<bool, uint, string, uint> ModifyCharacterSquelch,
        Action<bool, string> ModifyAccountSquelch,
        Action<bool, uint> ModifyGlobalSquelch,
        Func<string?> LastTeller,
        Action ClearDesiredComponents,
        Func<bool> HasOpenVendor,
        Action<uint?, uint> FillComponentBuyList,
        Action EnterPkLite,
        Func<bool> IsUsingTurbineChat,
        Action<string> SetChatTitle,
        Action<uint, bool> SetSingleCharacterOption,
        Action<string> AddPlayerPermission,
        Action<string> RemovePlayerPermission,
        Action<uint> RequestAvailableHouses,
        Action RequestChannelIndex,
        Action<uint> RequestChannelList,
        Action<uint> JoinGmChannel,
        Action<uint> LeaveGmChannel,
        Action RecallAllegianceHometown,
        Action<string> RequestAllegianceInfo,
        Action AbandonHouse,
        AdministrationWiring Administration,
        Func<bool> IsPersistentDaylight,
        Action<bool> SetPersistentDaylight,
        Action<int> SetLandscapeRadius,
        Action<float> SetFieldOfView);

    private readonly Mappings _bindings;

    private readonly CanonAdminDirectiveRouter _administration;

    public ClientDirectiveDriver(Mappings bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        var acts = bindings.Administration;
        _administration = new CanonAdminDirectiveRouter(
            new CanonAdminDirectiveRouter.FeedbackWiring(
                bindings.ShowSystemMessage,
                bindings.ShowClientLocalMessage,
                bindings.SetSingleCharacterOption,
                bindings.RequestAllegianceInfo),
            new CanonAdminDirectiveRouter.ActionWiring(
                acts.BreakAllegianceBoot,
                acts.AllegianceChatBoot,
                acts.AllegianceChatGag,
                acts.AllegianceBroadcast,
                acts.ListAllegianceBans,
                acts.AddAllegianceBan,
                acts.RemoveAllegianceBan,
                acts.ListAllegianceOfficers,
                acts.ClearAllegianceOfficers,
                acts.SetAllegianceOfficer,
                acts.RemoveAllegianceOfficer,
                acts.ListAllegianceOfficerTitles,
                acts.ClearAllegianceOfficerTitles,
                acts.SetAllegianceOfficerTitle,
                acts.QueryAllegianceName,
                acts.SetAllegianceName,
                acts.ClearAllegianceName,
                acts.AllegianceLockAction,
                acts.SetAllegianceApprovedVassal,
                acts.AllegianceHouseAction,
                acts.QueryMotd,
                acts.SetMotd,
                acts.ClearMotd,
                acts.SetOpenHouseStatus,
                acts.AddPermanentGuest,
                acts.RemovePermanentGuest,
                acts.RemoveAllPermanentGuests,
                acts.ChangeStoragePermission,
                acts.AddAllStoragePermission,
                acts.RemoveAllStoragePermission,
                acts.RequestFullGuestList,
                acts.BootSpecificHouseGuest,
                acts.BootEveryone,
                acts.SetHooksVisibility,
                acts.ModifyAllegianceGuestPermission,
                acts.ModifyAllegianceStoragePermission));
    }

    private readonly record struct MuteArguments(
        bool AccountWide, uint MessageType, string Name);

    private static readonly IReadOnlyDictionary<uint, string> MsgKinds =
        new Dictionary<uint, string>
        {
            [1] = "All",
            [2] = "Speech",
            [3] = "Tell",
            [6] = "Combat",
            [7] = "Magic",
            [12] = "Emote",
            [16] = "Appraisal",
            [17] = "Spellcasting",
            [18] = "Allegiance",
            [19] = "Fellowship",
            [21] = "Combat_Enemy",
            [22] = "Combat_Self",
            [23] = "Recall",
            [24] = "Craft",
            [25] = "Salvaging",
        };

    private const string StandardEmotes =
        "Standard Emotes:\n"
        + "Note: These commands should be bound on either side by asterisks. (Example: *wave*)\n"
        + "ShakeFist; Beckon; BeSeeingYou; BlowKiss; BowDeep; ClapHands; Cry; Laugh; Nod; Point; Shrug; Wave; Akimbo; HeartyLaugh; Salute; TapFoot; WaveHigh; WaveLow; Yawn; Stretch; Cringe; Kneel; Plead; Shiver; Shoo; Slouch; Spit; Surrender; Woah; Winded; YMCA; Eat; Drink; Teapot; Pray; Mock; Cheer; Helper; Warm Hands; Scratch Head; Shake Head\n\n";

    private const string CanonEndurancePhrase =
        "The endurance attribute has a number of abilities tied to it.\n"
        + "First, some combination of strength and endurance (with endurance being more important) now allows one to regenerate hit points at a faster rate the higher one's endurance is.  This bonus is in addition to any regeneration spells one may have placed upon themselves.  This endurance regeneration bonus caps at around 110%.\n"
        + "Second, the higher a player's Endurance, the less stamina one uses while attacking.  This benefit is tied to Endurance only, and it caps out at around 50% less stamina used per attack.  The minimum stamina used per attack remains one.\n"
        + "Third, the higher a player's Endurance, the more likely they are not to use a point of stamina to successfully evade a missile or melee attack.  A player is required to have Melee Defense for melee attacks or Missile Defense for missile attacks trained or specialized in order for this specific ability to work.  This benefit is tied to Endurance only, and it caps out at around a 75% chance to avoid losing a point of stamina per successful evasion.\n"
        + "Fourth, some combination of strength and endurance (the two are roughly of equivalent importance) now allows one to partially resist drain and harm attacks, up to a maximum of roughly 50%.\n"
        + "Fifth, some combination of strength and endurance (the two are roughly of equivalent importance) now allows one to have a level of \"natural resistances\" to the 7 damage types, the same as a certain level of life protections.  This caps out at a 50% resistance (the equivalent to level 5 life prots) to these damage types.  This resistance is not additive to life protections: higher level life protections will overwrite these natural resistances, although life vulns will take these natural resistances into account, if the player does not have a higher level life protection cast upon him.\n"
        + "The natural resistances, drain resistances, and regeneration rate info are now visible on the Character Information Panel, in what was once the Burden panel.  This panel now displays the above three Endurance benefits, the burden info, as well as information about your age, birth date, and number of deaths.\n"
        + "The 5 categories for the endurance benefits are, in order from lowest benefit to highest: Poor, Mediocre, Hardy, Resilient, and Indomitable, with each range of benefits divided up equally amongst the 5 (e.g. Poor describes having anywhere from 1-10% resistance against drain health attacks, etc.).\n";

    private const string AwayHelp =
        "@afk - Turns on AFK (away-from-keyboard) mode. When set to AFK, other players that send you directed chatyou will receive a customizable message that your are not currently at the keyboard.\n"
        + "@afk on - Turns on AFK mode. When set to AFK, other players that send you directed chatyou will receive a customizable message that your are not currently at the keyboard.\n"
        + "@afk off - Turn off AFK mode.\n"
        + "@afk msg <message> - Set the message that will be sent to players that send you directed chat while you are in AFK mode. Issuing \"@afk msg\" with no message will set your AFK message back to the default. Your custom AFK message is limited to 192 characters.\n";
}
