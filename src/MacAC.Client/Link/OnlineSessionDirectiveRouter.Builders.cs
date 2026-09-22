using MacAC.Client.Shell;

namespace MacAC.Client.Link;

internal sealed partial class OnlineSessionDirectiveRouter
{
    private ClientDirectiveDriver.Mappings AssembleGuardedClientDirectives(
        ClientDirectiveDriver.Mappings src)
    {
        return new(
        TeleportToLifestone: () => CallClient(static bindings => bindings.TeleportToLifestone()),
        TeleportToMarketplace: () => CallClient(static bindings => bindings.TeleportToMarketplace()),
        TeleportToPkArena: () => CallClient(static bindings => bindings.TeleportToPkArena()),
        TeleportToPkLiteArena: () => CallClient(static bindings => bindings.TeleportToPkLiteArena()),
        TeleportToHouse: () => CallClient(static bindings => bindings.TeleportToHouse()),
        TeleportToMansion: () => CallClient(static bindings => bindings.TeleportToMansion()),
        QueryAge: () => CallClient(static bindings => bindings.QueryAge()),
        QueryBirth: () => CallClient(static bindings => bindings.QueryBirth()),
        ToggleFrameRate: () => CallClient(static bindings => bindings.ToggleFrameRate()),
        ToggleUiLock: () => CallClient(static bindings => bindings.ToggleUiLock()),
        ShowSystemMessage: phrase => CallClient(bindings => bindings.ShowSystemMessage(phrase)),
        ShowClientLocalMessage: phrase =>
            CallClient(bindings => bindings.ShowClientLocalMessage(phrase)),
        ShowWeenieError: problem => CallClient(bindings => bindings.ShowWeenieError(problem)),
        PlayerPublicWeenieBitfield: () =>
            ScanClient(static bindings => bindings.PlayerPublicWeenieBitfield(), default(uint?)),
        ClientVersion: () => ScanClient(static bindings => bindings.ClientVersion(), string.Empty),
        CurrentPosition: () =>
            ScanClient(static bindings => bindings.CurrentPosition(), default(MacAC.Mechanics.Kinetics.Locus?)),
        LastOutsideCorpsePosition: () =>
            ScanClient(static bindings => bindings.LastOutsideCorpsePosition(), default(MacAC.Mechanics.Kinetics.Locus?)),
        ShowConfirmation: (phrase, hook) =>
            CallClient(bindings => bindings.ShowConfirmation(phrase, hook)),
        Suicide: () => CallClient(static bindings => bindings.Suicide()),
        ClearChat: all => CallClient(bindings => bindings.ClearChat(all)),
        SetChatLogFile: label => ScanClient(
            bindings => bindings.SetChatLogFile(label),
            default(MacAC.Mechanics.Comms.ChatTranscriptResult)),
        SaveUi: label => CallClient(bindings => bindings.SaveUi(label)),
        LoadUi: label => CallClient(bindings => bindings.LoadUi(label)),
        SaveAutoUi: () => CallClient(static bindings => bindings.SaveAutoUi()),
        LoadAutoUi: () => CallClient(static bindings => bindings.LoadAutoUi()),
        IsAway: () => ScanClient(static bindings => bindings.IsAway(), false),
        SetAway: away => CallClient(bindings => bindings.SetAway(away)),
        SetAwayMessage: msg => CallClient(bindings => bindings.SetAwayMessage(msg)),
        AcceptLootPermits: () => ScanClient(static bindings => bindings.AcceptLootPermits(), false),
        SetAcceptLootPermits: admit => CallClient(bindings => bindings.SetAcceptLootPermits(admit)),
        DisplayConsent: () => CallClient(static bindings => bindings.DisplayConsent()),
        ClearConsent: () => CallClient(static bindings => bindings.ClearConsent()),
        RemoveConsent: label => CallClient(bindings => bindings.RemoveConsent(label)),
        SendEmote: phrase => CallClient(bindings => bindings.SendEmote(phrase)),
        Friends: src.Friends,
        AddFriend: label => CallClient(bindings => bindings.AddFriend(label)),
        RemoveFriend: ident => CallClient(bindings => bindings.RemoveFriend(ident)),
        ClearFriends: () => CallClient(static bindings => bindings.ClearFriends()),
        RequestLegacyFriends: () => CallClient(static bindings => bindings.RequestLegacyFriends()),
        Squelch: src.Squelch,
        ModifyCharacterSquelch: (append, ident, label, commsKind) =>
            CallClient(bindings => bindings.ModifyCharacterSquelch(append, ident, label, commsKind)),
        ModifyAccountSquelch: (append, label) =>
            CallClient(bindings => bindings.ModifyAccountSquelch(append, label)),
        ModifyGlobalSquelch: (append, commsKind) =>
            CallClient(bindings => bindings.ModifyGlobalSquelch(append, commsKind)),
        LastTeller: () => ScanClient(static bindings => bindings.LastTeller(), default(string?)),
        ClearDesiredComponents: () => CallClient(static bindings => bindings.ClearDesiredComponents()),
        HasOpenVendor: () => ScanClient(static bindings => bindings.HasOpenVendor(), false),
        FillComponentBuyList: (moduleIdent, markTally) =>
            CallClient(bindings => bindings.FillComponentBuyList(moduleIdent, markTally)),
        EnterPkLite: () => CallClient(static bindings => bindings.EnterPkLite()),
        IsUsingTurbineChat: () => ScanClient(static bindings => bindings.IsUsingTurbineChat(), false),
        SetChatTitle: banner => CallClient(bindings => bindings.SetChatTitle(banner)),
        SetSingleCharacterOption: (knobIdent, val) =>
            CallClient(bindings => bindings.SetSingleCharacterOption(knobIdent, val)),
        AddPlayerPermission: label => CallClient(bindings => bindings.AddPlayerPermission(label)),
        RemovePlayerPermission: label => CallClient(bindings => bindings.RemovePlayerPermission(label)),
        RequestAvailableHouses: houseKind => CallClient(bindings => bindings.RequestAvailableHouses(houseKind)),
        RequestChannelIndex: () => CallClient(static bindings => bindings.RequestChannelIndex()),
        RequestChannelList: laneIdent => CallClient(bindings => bindings.RequestChannelList(laneIdent)),
        JoinGmChannel: laneIdent => CallClient(bindings => bindings.JoinGmChannel(laneIdent)),
        LeaveGmChannel: laneIdent => CallClient(bindings => bindings.LeaveGmChannel(laneIdent)),
        RecallAllegianceHometown: () => CallClient(static bindings => bindings.RecallAllegianceHometown()),
        RequestAllegianceInfo: label => CallClient(bindings => bindings.RequestAllegianceInfo(label)),
        AbandonHouse: () => CallClient(static bindings => bindings.AbandonHouse()),
        Administration: AssembleGuardedAdministration(),
        IsPersistentDaylight: () =>
            ScanClient(static bindings => bindings.IsPersistentDaylight(), false),
        SetPersistentDaylight: val =>
            CallClient(bindings => bindings.SetPersistentDaylight(val)),
        SetLandscapeRadius: radius =>
            CallClient(bindings => bindings.SetLandscapeRadius(radius)),
        SetFieldOfView: deg =>
            CallClient(bindings => bindings.SetFieldOfView(deg)));
    }

    private ClientDirectiveDriver.AdministrationWiring AssembleGuardedAdministration()
    {
        return new(
            BreakAllegianceBoot: (label, acct) =>
                CallClient(bindings => bindings.Administration.BreakAllegianceBoot(label, acct)),
            AllegianceChatBoot: (label, cause) =>
                CallClient(bindings => bindings.Administration.AllegianceChatBoot(label, cause)),
            AllegianceChatGag: (label, turnedOn) =>
                CallClient(bindings => bindings.Administration.AllegianceChatGag(label, turnedOn)),
            AllegianceBroadcast: phrase =>
                CallClient(bindings => bindings.Administration.AllegianceBroadcast(phrase)),
            ListAllegianceBans: () =>
                CallClient(static bindings => bindings.Administration.ListAllegianceBans()),
            AddAllegianceBan: label =>
                CallClient(bindings => bindings.Administration.AddAllegianceBan(label)),
            RemoveAllegianceBan: label =>
                CallClient(bindings => bindings.Administration.RemoveAllegianceBan(label)),
            ListAllegianceOfficers: () =>
                CallClient(static bindings => bindings.Administration.ListAllegianceOfficers()),
            ClearAllegianceOfficers: () =>
                CallClient(static bindings => bindings.Administration.ClearAllegianceOfficers()),
            SetAllegianceOfficer: (label, tier) =>
                CallClient(bindings => bindings.Administration.SetAllegianceOfficer(label, tier)),
            RemoveAllegianceOfficer: label =>
                CallClient(bindings => bindings.Administration.RemoveAllegianceOfficer(label)),
            ListAllegianceOfficerTitles: () =>
                CallClient(static bindings => bindings.Administration.ListAllegianceOfficerTitles()),
            ClearAllegianceOfficerTitles: () =>
                CallClient(static bindings => bindings.Administration.ClearAllegianceOfficerTitles()),
            SetAllegianceOfficerTitle: (tier, banner) =>
                CallClient(bindings => bindings.Administration.SetAllegianceOfficerTitle(tier, banner)),
            QueryAllegianceName: () =>
                CallClient(static bindings => bindings.Administration.QueryAllegianceName()),
            SetAllegianceName: label =>
                CallClient(bindings => bindings.Administration.SetAllegianceName(label)),
            ClearAllegianceName: () =>
                CallClient(static bindings => bindings.Administration.ClearAllegianceName()),
            AllegianceLockAction: act =>
                CallClient(bindings => bindings.Administration.AllegianceLockAction(act)),
            SetAllegianceApprovedVassal: label =>
                CallClient(bindings => bindings.Administration.SetAllegianceApprovedVassal(label)),
            AllegianceHouseAction: act =>
                CallClient(bindings => bindings.Administration.AllegianceHouseAction(act)),
            QueryMotd: () =>
                CallClient(static bindings => bindings.Administration.QueryMotd()),
            SetMotd: motd =>
                CallClient(bindings => bindings.Administration.SetMotd(motd)),
            ClearMotd: () =>
                CallClient(static bindings => bindings.Administration.ClearMotd()),
            SetOpenHouseStatus: open =>
                CallClient(bindings => bindings.Administration.SetOpenHouseStatus(open)),
            AddPermanentGuest: label =>
                CallClient(bindings => bindings.Administration.AddPermanentGuest(label)),
            RemovePermanentGuest: label =>
                CallClient(bindings => bindings.Administration.RemovePermanentGuest(label)),
            RemoveAllPermanentGuests: () =>
                CallClient(static bindings => bindings.Administration.RemoveAllPermanentGuests()),
            ChangeStoragePermission: (label, turnedOn) =>
                CallClient(bindings => bindings.Administration.ChangeStoragePermission(label, turnedOn)),
            AddAllStoragePermission: () =>
                CallClient(static bindings => bindings.Administration.AddAllStoragePermission()),
            RemoveAllStoragePermission: () =>
                CallClient(static bindings => bindings.Administration.RemoveAllStoragePermission()),
            RequestFullGuestList: () =>
                CallClient(static bindings => bindings.Administration.RequestFullGuestList()),
            BootSpecificHouseGuest: label =>
                CallClient(bindings => bindings.Administration.BootSpecificHouseGuest(label)),
            BootEveryone: () =>
                CallClient(static bindings => bindings.Administration.BootEveryone()),
            SetHooksVisibility: shown =>
                CallClient(bindings => bindings.Administration.SetHooksVisibility(shown)),
            ModifyAllegianceGuestPermission: turnedOn =>
                CallClient(bindings => bindings.Administration.ModifyAllegianceGuestPermission(turnedOn)),
            ModifyAllegianceStoragePermission: turnedOn =>
                CallClient(bindings => bindings.Administration.ModifyAllegianceStoragePermission(turnedOn)));
    }
}
