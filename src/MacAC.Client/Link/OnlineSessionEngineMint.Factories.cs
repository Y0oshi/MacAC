using MacAC.Dat;
using MacAC.Client.Paging;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Comms;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Link;

internal sealed partial class OnlineSessionEngineMint
{
    public OnlineSessionHarbor Create(
        OnlineSessionDriver driver,
        OnlineSessionConnectOptions linkKnobs)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(linkKnobs);
        var restartHub = new GraphicalEngineEpochResetHost(
            _world.LiveEntities,
            () => _world.RenderSceneShadow?.EmptyRefreshBoundary());
        var restart =
            OnlineSessionResetManifest.Create(
                BuildRestartMappings(restartHub));
        LoginDirectiveSequence signinDirectives = new LoginDirectiveSequence(
            _signinDirectives,
            _signinDirectiveDelay,
            new SimCommsDirectiveFeedback(_domain.Communication),
            _commands,
            miss => _conditionWriter.SignInDirectiveFailed(
                _sessIdent,
                miss.CommandIndex,
                miss.Command,
                miss.Error),
            _momentSupplier);
        return new OnlineSessionHarbor(driver, new OnlineSessionHarborWiring(
            Routing: new(
                CreateEventRouter,
                sess => _commands.Attach(new OnlineSessionDirectiveRouter(
                    BuildDirectiveMappings(sess)))),
            Reset: restart.Execute,
            Selection: new(
                SetPlayerIdentity: ident => _avatar.Identity.SrvOid = ident,
                SetVitalsIdentity: ident => _widget.Vitals?.AssignOwnAvatarOid(ident),
                SetChatIdentity: _domain.Communication.Chat.ApplyOwnAvatarOid,
                MarkPersistent: _world.WorldState.FlagPersistent,
                SetVanishProbeIdentity: ident => ActorVanishProbe.AvatarGuid = ident,
                ClearCombat: _domain.Actions.Combat.Clear,
                ArmLoginTunnel: _world.Teleport.ArmSigninTunnel),
            EnteredWorld: new(
                SetActiveCharacter: _dealing.Settings.AssignEngagedToon,
                RestoreLayout: () =>
                {
                    _dealing.Settings.AssignGameplayReadout(true);
                    _widget.RetailUi?.ReinstateArrangement();
                    _widget.RetailUi?.SatchelBoardDriver?.Populate();
                    _widget.Paperdoll?.FlagStale();
                    _widget.RetailUi?.RedeclareSocialBoardFollowingRealmListing();
                },
                SyncToolbar: () => _widget.RetailUi?.SynchronizeToolbarPaneBtns(),
                LoadCharacterSettings: label =>
                {
                    _dealing.Settings.PullToonCtx(label);
                    _widget.RetailUi?.PullJournal(label);
                },
                ArmPlayerModeAutoEntry: _dealing.PlayerModeAutoEntry.Arm,
                ResumeWorldAudio: () => _world.WorldAudio?.ReactivateForRealmListing()),
            Connecting: (hub, port, user) =>
                _domain.Communication.Chat.OnSysMsg(
                    $"connecting to {hub}:{port} as {user}",
                    commsKind: 1),
            Connected: () =>
            {
                _domain.Communication.Chat.OnSysMsg(
                    "connected — character list received",
                    commsKind: 1);
                _conditionWriter.Connected(_sessIdent);
            },
            Roster: lineup => _conditionWriter.CharacterList(_sessIdent, lineup),
            CharacterEntered: pick => _conditionWriter.EnteredWorld(
                _sessIdent,
                pick.CharacterId,
                pick.CharacterName),
            LoginCommands: signinDirectives,
            CharacterCreated: persona => _conditionWriter.ToonBuilt(
                _sessIdent,
                persona.Guid,
                persona.Name),
            CreationFailed: rejection => _conditionWriter.CreationFailed(
                _sessIdent,
                rejection.RawCode,
                rejection.Reason,
                rejection.AttemptedName)),
            linkKnobs with { PollConnectionDuringTicks = true });
    }

    private OnlineSessionResetWiring BuildRestartMappings(
        ISimEpochResetHarbor restartHub)
    {
        return new()
        {
            PointerGrab = _dealing.GameplayInput.RewindSess,
            AvatarExhibit = ResetPlayerPresentation,
            WarpExhibit =
            _world.Teleport.RestartGenExhibit,
            RealmSound = () => _world.WorldAudio?.SuspendForSessRestart(),
            SessPopups = () => _widget.RetailUi?.RestartSessTransientWidget(),
            PrefsToonCtx =
            _dealing.Settings.ReinstateDefaultToonCtx,
            EquippedDescendants = _world.EquippedChildren.Clear,
            DealingExhibit =
            _dealing.SelectionInteractions.RestartGenExhibit,
            PickExhibit = _world.SelectionScene.Reset,
            MoteVis = _world.ParticleVisibility.Reset,
            IncomingSignalFifo = _world.InboundEvents.Clear,
            OnlineLiveness = _world.Liveness.Clear,
            CoreGen = gen =>
                _domain.Runtime.RestartGen(gen, restartHub),
            SessPersonaExhibit = ResetIdentityPresentation,
            NetworkFxList = _world.EntityEffects.WipeNetworkPhase,
            AnimTapCycles = _world.AnimationHookFrames.Clear,
            OnlineExhibit = _world.Presentation.Clear,
            DistantTravelTelemetry = _world.RemoteMovementObservations.Clear,
        };
    }

    private IOnlineSessionEventRouting CreateEventRouter(RealmSession sess)
    {
        sess.BlobVersions = new DddVersions(
            _world.Dats.GatewayIteration, _world.Dats.ChamberIteration, _world.Dats.LanguageIteration);
        SkillBook? aptitudeChart = _world.Dats.Get<SkillBook>(0x0E000004u);
        if (_widget.CharacterSheet is not null)
        {
            _widget.CharacterSheet.SkillTable = aptitudeChart;
            _widget.CharacterSheet.ExperienceTable =
                ToonSheetSupplier.PullExperienceChart(_world.Dats, _trace);
        }

        OnlineSessionEventRouter course = new OnlineSessionEventRouter(
            sess,
            _world.EntitySession.BuildDrain(),
            new OnlineAmbienceSessionSink(
                _world.Environment.ImposeAdminEnvirons,
                _world.Environment.SynchronizeFromSrv),
            BuildSatchelMappings(),
            BuildToonMappings(aptitudeChart),
            new OnlineSocialSessionWiring(
                _domain.Communication.Chat,
                _domain.Communication.TurbineChat,
                _domain.Communication.Friends,
                _domain.Communication.Squelch,
                (phrase, kind) => _domain.Communication.AddText(phrase, kind),
                Fellowship: _domain.Runtime.FellowshipHolder,
                Allegiance: _domain.Runtime.AllegianceHolder,
                Trade: _domain.Runtime.BarterHolder,
                House: _domain.Runtime.HouseOwner,
                Contracts: _domain.Runtime.ContractsHolder,
                PlayerGuid: () => _avatar.Identity.SrvOid));
        return new GraphicalSessionSignalCourse(
            course,
            _domain.Runtime,
            _world.PlacementProjection,
            _world.PlacementRetries,
            _world.FirstEntryDrive,
            _ =>
            {
                _world.Teleport.OnOwnAvatarLeadListingFinished();
                sess.TransmitHouseAsk();
            },
            _world.AcceptedPositionDrive,
            _world.RemotePlacementDrive);
    }

    private OnlineStashSessionWiring BuildSatchelMappings()
    {
        return new(
        _domain.Inventory.Objects,
        PlayerGuid: () => _avatar.Identity.SrvOid,
        OnShortcuts: _domain.Inventory.Shortcuts.Load,
        OnUseDone: problem =>
        {
            _domain.Inventory.ExternalVessels.ImposeUseDone(problem);
            _domain.Actions.SpellCast.CompleteUse(problem);
            _domain.Actions.Transactions.CompleteUse(problem);
        },
        _domain.Inventory.ItemMana,
        ExternalContainers: _domain.Inventory.ExternalVessels,
        OnAppraisal: appraisal =>
        {
            if (_widget.RetailUi is { } canonWidget)
                canonWidget.ProcessAppraisal(appraisal);
            else
                _dealing.ItemInteraction.AdmitAppraisalResponse(appraisal.Guid);
        },
        Vendor: _domain.Inventory.Vendor);
    }

    private OnlineToonSessionWiring BuildToonMappings(
        SkillBook? aptitudeChart)
    {
        OnlineSkillCreditPicker aptitudeCreditLocator = new OnlineSkillCreditPicker(aptitudeChart);
        return new OnlineToonSessionWiring(
            _domain.Actions.Combat,
            _domain.Character,
            ResolveSkillFormulaBonus: aptitudeCreditLocator.Resolve,
            OnSkillsUpdated: (execAptitude, leapAptitude) =>
                _travelStats.Apply("skills"),
            OnConfirmationRequest: req =>
                _widget.RetailUi?.ProcessAckReq(req),
            OnConfirmationDone: done =>
                _widget.RetailUi?.ProcessAckDone(done),
            ClientTime: ClientTickerInstant,
            OnMovementStatsUpdated: () => _travelStats.Apply("stats"),
            OnCharacterOptionsChanged: (_, options2) =>
            {
                _dealing.Settings.SynchronizeCommsFromSrvKnobs(options2);
                _dealing.Settings.AssignWidgetBolted(
                    _domain.Character.Options.GetOptionBit(CharacterOptionId.LockUI));
                // open option-bearing panels re-read live
                // bits at every seed (login + reconnect) - see
                // EnginePreferencesDriver.ServerOptionsSeeded.
                _dealing.Settings.AlertSrvKnobsSeeded();
            });
    }

    private OnlineSessionDirectiveWiring BuildDirectiveMappings(
        RealmSession sess)
    {
        void TransmitSingleToonKnob(uint knobIdent, bool val) =>
            _domain.Character.Options.TrySetKnob(
                knobIdent,
                val,
                transmitAutoPersist: sess.TransmitSetSingleToonKnob);

        void PersistToonKnobsIfStale() =>
            _domain.Character.Options.TryDrain(() =>
            {
                var echo = ToonOptionsBlobSource.Capture(
                    _domain.Character,
                    _domain.Inventory.Shortcuts);
                sess.TransmitSetToonKnobs(
                    echo.Options1,
                    echo.Options2,
                    echo.Shortcuts,
                    echo.FavoriteSpells,
                    echo.DesiredComponents,
                    echo.SpellbookFilters);
            });

        return new(
        ClientCommands: new ClientDirectiveDriver.Mappings(
            TeleportToLifestone: sess.TransmitWarpToLifestone,
            TeleportToMarketplace: sess.TransmitWarpToMarketplace,
            TeleportToPkArena: sess.TransmitWarpToPkArena,
            TeleportToPkLiteArena: sess.TransmitWarpToPkLiteArena,
            TeleportToHouse: sess.TransmitWarpToHouse,
            TeleportToMansion: sess.TransmitWarpToMansion,
            QueryAge: sess.TransmitAskAge,
            QueryBirth: sess.TransmitAskBirth,
            ToggleFrameRate: _dealing.Settings.FlipCycleRate,
            ToggleUiLock: () =>
            {
                bool bolted = !_domain.Character.Options.GetOptionBit(
                    CharacterOptionId.LockUI);
                TransmitSingleToonKnob((uint)CharacterOptionId.LockUI, bolted);
                _dealing.Settings.AssignWidgetBolted(bolted);
            },
            ShowSystemMessage:
                phrase => _domain.Communication.Chat.OnSysMsg(phrase, 0x00u),
            ShowClientLocalMessage:
                phrase => _domain.Communication.AddText(
                    phrase, CanonLogTextType.ClientLocal),
            ShowWeenieError:
                code =>
                {
                    (string? wording, CanonLogTextType kind) = WeenieErrorText.Resolve(code, null);
                    if (wording is not null)
                        _domain.Communication.AddText(wording, kind);
                    else
                        Console.WriteLine($"[weenie-error] unmapped code=0x{code:X4}");
                },
            PlayerPublicWeenieBitfield: () =>
                _domain.EntityObjects.Objects.Get(_avatar.Identity.SrvOid)?
                    .PublicWeenieBitfield,
            ClientVersion: () =>
                typeof(OnlineSessionEngineMint).Assembly
                    .GetName().Version?.ToString(3)
                ?? "unknown",
            CurrentPosition: () => _avatar.Controller.Controller?.CellPosition,
            LastOutsideCorpsePosition: () =>
                _domain.Character.LocalPlayer.FetchLocus(0x0Eu),
            ShowConfirmation: (msg, finished) =>
                _widget.RetailUi?.RevealAck(msg, finished),
            Suicide: sess.TransmitSuicide,
            ClearChat: _ => _domain.Communication.Chat.Clear(),
            SetChatLogFile: AssignCommsTraceFile,
            SaveUi: label => _widget.RetailUi?.PersistNamedArrangement(label),
            LoadUi: label => _widget.RetailUi?.ReinstateNamedArrangement(label),
            SaveAutoUi: () => _widget.RetailUi?.PersistArrangement(),
            LoadAutoUi: () => _widget.RetailUi?.ReinstateArrangement(),
            IsAway: () =>
                _domain.EntityObjects.Objects.Get(_avatar.Identity.SrvOid)?
                    .Properties.FetchBool(0x6Eu) == true,
            SetAway: sess.TransmitSetAfkManner,
            SetAwayMessage: sess.TransmitSetAfkMsg,
            AcceptLootPermits: () =>
                _domain.Character.Options.GetOptionBit(
                    CharacterOptionId.AcceptLootPermits),
            SetAcceptLootPermits: val =>
                TransmitSingleToonKnob(
                    (uint)CharacterOptionId.AcceptLootPermits, val),
            DisplayConsent: sess.TransmitReadoutConsent,
            ClearConsent: sess.TransmitWipeConsent,
            RemoveConsent: sess.TransmitDropConsent,
            SendEmote: sess.TransmitEmote,
            _domain.Communication.Friends,
            AddFriend: sess.TransmitAppendFriend,
            RemoveFriend: sess.TransmitDropFriend,
            ClearFriends: sess.TransmitWipeFriends,
            RequestLegacyFriends: sess.TransmitLegacyFriendsRosterReq,
            _domain.Communication.Squelch,
            ModifyCharacterSquelch: sess.TransmitModifyToonSquelch,
            ModifyAccountSquelch: sess.TransmitModifyAcctSquelch,
            ModifyGlobalSquelch: sess.TransmitModifyGlobalSquelch,
            LastTeller: () =>
                _domain.Communication.DirectiveMarks.LastIncomingTellSender,
            ClearDesiredComponents: sess.TransmitWipeWantedModules,
            HasOpenVendor: () => false,
            FillComponentBuyList: (_, _) => { },
            EnterPkLite: sess.TransmitJoinPkLite,
            IsUsingTurbineChat: () => _domain.Communication.TurbineChat.Enabled,
            SetChatTitle: _ => { },
            SetSingleCharacterOption: TransmitSingleToonKnob,
            AddPlayerPermission: sess.TransmitAppendAvatarPermission,
            RemovePlayerPermission: sess.TransmitDropAvatarPermission,
            RequestAvailableHouses: sess.TransmitRosterOnHandHouses,
            RequestChannelIndex: sess.TransmitOrdinalLanes,
            RequestChannelList: sess.TransmitRosterLane,
            JoinGmChannel: sess.TransmitOnLane,
            LeaveGmChannel: sess.TransmitOffLane,
            RecallAllegianceHometown: sess.TransmitRecallAllegianceHometown,
            RequestAllegianceInfo: sess.TransmitAllegianceDetailsReq,
            AbandonHouse: sess.TransmitAbandonHouse,
            Administration: new ClientDirectiveDriver.AdministrationWiring(
                BreakAllegianceBoot: sess.TransmitBreakAllegianceBoot,
                AllegianceChatBoot: sess.TransmitAllegianceCommsBoot,
                AllegianceChatGag: sess.TransmitAllegianceCommsGag,
                AllegianceBroadcast: phrase =>
                    sess.TransmitLane(0x02000000u, phrase),
                ListAllegianceBans: sess.TransmitRosterAllegianceBans,
                AddAllegianceBan: sess.TransmitAppendAllegianceBan,
                RemoveAllegianceBan: sess.TransmitDropAllegianceBan,
                ListAllegianceOfficers: sess.TransmitRosterAllegianceOfficers,
                ClearAllegianceOfficers: sess.TransmitWipeAllegianceOfficers,
                SetAllegianceOfficer: sess.TransmitSetAllegianceOfficer,
                RemoveAllegianceOfficer: sess.TransmitDropAllegianceOfficer,
                ListAllegianceOfficerTitles: sess.TransmitRosterAllegianceOfficerBanners,
                ClearAllegianceOfficerTitles: sess.TransmitWipeAllegianceOfficerBanners,
                SetAllegianceOfficerTitle: sess.TransmitSetAllegianceOfficerBanner,
                QueryAllegianceName: sess.TransmitAskAllegianceLabel,
                SetAllegianceName: sess.TransmitSetAllegianceLabel,
                ClearAllegianceName: sess.TransmitWipeAllegianceLabel,
                AllegianceLockAction: sess.TransmitAllegianceLockAct,
                SetAllegianceApprovedVassal: sess.TransmitSetAllegianceApprovedVassal,
                AllegianceHouseAction: sess.TransmitAllegianceHouseAct,
                QueryMotd: sess.TransmitAskMotd,
                SetMotd: sess.TransmitSetMotd,
                ClearMotd: sess.TransmitWipeMotd,
                SetOpenHouseStatus: sess.TransmitSetOpenHouseCondition,
                AddPermanentGuest: sess.TransmitAppendPermanentGuest,
                RemovePermanentGuest: sess.TransmitDropPermanentGuest,
                RemoveAllPermanentGuests: sess.TransmitDropAllPermanentGuests,
                ChangeStoragePermission: sess.TransmitEditDepotPermission,
                AddAllStoragePermission: sess.TransmitAppendAllDepotPermission,
                RemoveAllStoragePermission: sess.TransmitDropAllDepotPermission,
                RequestFullGuestList: sess.TransmitReqWholeGuestRoster,
                BootSpecificHouseGuest: sess.TransmitBootSpecificHouseGuest,
                BootEveryone: sess.TransmitBootEveryone,
                SetHooksVisibility: sess.TransmitSetTapsVis,
                ModifyAllegianceGuestPermission:
                    sess.TransmitModifyAllegianceGuestPermission,
                ModifyAllegianceStoragePermission:
                    sess.TransmitModifyAllegianceDepotPermission),
            IsPersistentDaylight: () =>
                _domain.Character.Options.GetOptionBit(
                    CharacterOptionId.PersistentAtDay),
            SetPersistentDaylight: turnedOn =>
                TransmitSingleToonKnob(
                    (uint)CharacterOptionId.PersistentAtDay,
                    turnedOn),
            SetLandscapeRadius: radius =>
                _dealing.Settings.StoreReadout(
                    _dealing.Settings.Readout with
                    {
                        LandscapeDrawDistance = radius,
                    }),
            SetFieldOfView: deg =>
                _dealing.Settings.StoreReadout(
                    _dealing.Settings.Readout with
                    {
                        FieldOfView = deg,
                    })),
        _domain.Communication.Chat,
        _domain.Communication.TurbineChat,
        PlayerGuid: () => _avatar.Identity.SrvOid,
        SendTalk: sess.TransmitTalk,
        SendTell: sess.TransmitTell,
        SendChannel: sess.TransmitLane,
        SendTurbineChat: sess.TransmitTurbineCommsTo,
        AddShortcut: sess.TransmitAppendShortcut,
        RemoveShortcut: sess.TransmitDropShortcut,
        AddFavorite: sess.TransmitAppendArcanumFavorite,
        RemoveFavorite: sess.TransmitDropArcanumFavorite,
        SetSpellbookFilter: sess.TransmitGrimoireSift,
        ForgetSpell: sess.TransmitDropArcanum,
        SetDesiredComponent: sess.TransmitSetWantedModuleTier,
        ClearDesiredComponents: sess.TransmitWipeWantedModules,
        RaiseAttribute: sess.TransmitEmitAttr,
        RaiseVital: sess.TransmitEmitVital,
        RaiseSkill: sess.TransmitEmitAptitude,
        TrainSkill: sess.TransmitTrainAptitude,
        AddFriend: sess.TransmitAppendFriend,
        RemoveFriend: sess.TransmitDropFriend,
        ClearFriends: sess.TransmitWipeFriends,
        RequestLegacyFriends: sess.TransmitLegacyFriendsRosterReq,
        OpenTradeNegotiations: sess.TransmitOpenBarterNegotiations,
        CloseTradeNegotiations: sess.TransmitShutBarterNegotiations,
        AddToTrade: gear => sess.TransmitAppendToBarter(gear),
        AcceptTrade: (partner, selfApproved, partnerApproved) =>
            sess.TransmitAdmitBarter(
                partner, 0d, 0u, partner, selfApproved, partnerApproved),
        DeclineTrade: sess.TransmitDeclineBarter,
        ResetTrade: sess.TransmitRestartBarter,
        ModifyCharacterSquelch: sess.TransmitModifyToonSquelch,
        ModifyAccountSquelch: sess.TransmitModifyAcctSquelch,
        ModifyGlobalSquelch: sess.TransmitModifyGlobalSquelch,
        Communication: _domain.Communication,
        CharacterState: _domain.Character,
        SendSingleCharacterOption: TransmitSingleToonKnob,
        SaveCharacterOptions: PersistToonKnobsIfStale,
        SendSetTitle: sess.TransmitSetBanner,
        SendFellowshipCreate: sess.TransmitFellowshipBuild,
        SendFellowshipRecruit: sess.TransmitFellowshipRecruit,
        SendFellowshipDismiss: sess.TransmitFellowshipDismiss,
        SendFellowshipQuit: sess.TransmitFellowshipQuit,
        SendFellowshipAssignNewLeader: sess.TransmitFellowshipAssignNewLeader,
        SendFellowshipChangeOpenness: sess.TransmitFellowshipEditOpenness,
        SendFellowshipUpdateRequest: sess.TransmitFellowshipRefreshReq,
        SendAllegianceSwear: sess.TransmitAllegianceSwear,
        SendAllegianceBreak: sess.TransmitAllegianceBreak,
        SendAllegianceKick: sess.TransmitAllegianceKick,
        SendAllegianceInfoRequest: sess.TransmitAllegianceDetailsReq,
        SendAllegianceUpdateRequest: sess.TransmitAllegianceRefreshReq,
        Log: _trace,
        ResolvePose: directive => _commsPostures.Resolve(
            directive,
            male: _domain.EntityObjects.Objects
                .Get(_avatar.Identity.SrvOid)?
                .Properties.FetchInt(0x71u) == 1),
        ExecuteMotion: locomotion => _avatar.Controller.RunLocomotion(locomotion),
        SendSoulEmote: sess.TransmitSoulEmote);
    }
}
