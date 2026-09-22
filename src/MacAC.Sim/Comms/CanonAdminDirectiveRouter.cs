using MacAC.Wire.Messages;

namespace MacAC.Sim.Comms;

public sealed partial class CanonAdminDirectiveRouter
{
    public sealed record FeedbackWiring(
        Action<string> ShowSystemMessage,
        Action<string> ShowClientLocalMessage,
        Action<uint, bool> SetSingleCharacterOption,
        Action<string> RequestAllegianceInfo);

    public sealed record ActionWiring(
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

    private const string LabelWanted = "Please specify an actual name.";
    private const string ParticipantWanted = "Please specify the name of an allegiance member.";
    private const string GuestWanted = "Please specify the guest's name.";
    private const string AllegianceHelp =
        "Please see @help Allegiance for more information on how to use this command.";
    private const string HouseHelp =
        "Please see @help House for more information on how to use this command.";

    private readonly FeedbackWiring _feedback;
    private readonly ActionWiring _actions;
    private readonly Dictionary<ClientDirectiveId, Action<Args>> _verbs;

    public CanonAdminDirectiveRouter(
        FeedbackWiring feedback,
        ActionWiring actions)
    {
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _verbs = new Dictionary<ClientDirectiveId, Action<Args>>
        {
            [ClientDirectiveId.AllegianceInfo] = AllegianceDetails,
            [ClientDirectiveId.AllegianceBoot] = AllegianceBoot,
            [ClientDirectiveId.AllegianceBan] = AllegianceBan,
            [ClientDirectiveId.AllegianceChat] = AllegianceChat,
            [ClientDirectiveId.AllegianceBroadcast] = AllegianceBroadcast,
            [ClientDirectiveId.AllegianceOfficer] = AllegianceOfficer,
            [ClientDirectiveId.AllegianceOfficerTitle] = AllegianceOfficerBanner,
            [ClientDirectiveId.AllegianceName] = AllegianceName,
            [ClientDirectiveId.AllegianceLock] = AllegianceMutex,
            [ClientDirectiveId.AllegianceHouse] = AllegianceHouse,
            [ClientDirectiveId.AllegianceMotd] = AllegianceMotd,
            [ClientDirectiveId.AllegianceUnrecognizedSubcommand] = _ => Local(AllegianceHelp),
            [ClientDirectiveId.HouseOpenStatus] = a =>
                _actions.SetOpenHouseStatus(a.Whole.Equals("open", StringComparison.OrdinalIgnoreCase)),
            [ClientDirectiveId.HouseStorage] = HouseDepot,
            [ClientDirectiveId.HouseBoot] = HouseBoot,
            [ClientDirectiveId.HouseBootAll] = _ => _actions.BootEveryone(),
            [ClientDirectiveId.HouseGuests] = HouseGuests,
            [ClientDirectiveId.HouseHooks] = HouseTaps,
            [ClientDirectiveId.HouseUnrecognizedSubcommand] = _ => Local(HouseHelp),
        };
    }

    public bool TryPerform(ClientDirectiveId directive, string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_verbs.TryGetValue(directive, out var verb))
            return false;
        verb(new Args(arguments));
        return true;
    }

    private void Local(string phrase) => _feedback.ShowClientLocalMessage(phrase);

    // Requires a non-empty trimmed value, otherwise emits refusal
    private bool Need(string val, string refusal)
    {
        if (val.Length is not 0)
            return true;
        Local(refusal);
        return false;
    }

    private static bool Is(string word, string anticipated) =>
        word.Equals(anticipated, StringComparison.OrdinalIgnoreCase);

    private readonly struct Args(string raw)
    {
        private static readonly char[] Blanks = [' ', '\t', '\r', '\n'];

        private readonly string _trimmed = raw.Trim();

        public string Whole => raw;

        public string Head
        {
            get
            {
                int cut = _trimmed.IndexOfAny(Blanks);
                return cut < 0 ? _trimmed : _trimmed[..cut];
            }
        }

        public string Rear
        {
            get
            {
                int cut = _trimmed.IndexOfAny(Blanks);
                return cut < 0 ? string.Empty : _trimmed[(cut + 1)..].TrimStart();
            }
        }

        public Args Rest => new(Rear);
    }
}
