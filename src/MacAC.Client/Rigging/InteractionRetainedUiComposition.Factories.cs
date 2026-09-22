using System.Collections.Concurrent;
using MacAC.Client.Arcana;
using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Client.Telemetry;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Cockpit.Panels.Vitals;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Traits;
using MacAC.Sim;
using MacAC.Wire.Messages;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed partial class CanonDealingRetainedWidgetAssemblyMint
{

    public ExternalContainerLifespanDriver BuildExternalVesselLifecycle(
        DealingRetainedWidgetDependencies dependencies,
        DeferredOnlineSessionWidgetAuthority sess)
    {
        return new(
            dependencies.Inventory.ExternalVessels,
            dependencies.Inventory.Objects,
            oid => sess.LatestSess?.TransmitNoLongerViewingInsides(oid));
    }

    public GearDealingDriver BuildGearDealing(
        DealingRetainedWidgetDependencies dependencies,
        DealingWidgetLateWiring late)
    {
        var sess = late.Session;
        var pick = late.Selection;
        return new GearDealingDriver(
            dependencies.Inventory.Objects,
            dependencies.Actions.Transactions,
            dependencies.Actions.Interaction,
            playerGuid: () => dependencies.PlayerIdentity.SrvOid,
            transmitUse: null,
            transmitExamine: oid => sess.LatestSess?.TransmitEvaluate(oid),
            transmitUseWithMark: (src, mark) =>
                sess.LatestSess?.TransmitUseWithMark(src, mark),
            transmitWield: (gear, bitmask) =>
                sess.LatestSess?.TransmitFetchAndWieldGear(gear, bitmask),
            transmitDiscard: gear => sess.LatestSess?.TransmitDiscardGear(gear),
            transmitHand: (mark, gear, quantity) =>
                sess.LatestSess?.TransmitHandObject(mark, gear, quantity),
            pullOnAvatarOpensSecureBarter: () =>
                dependencies.Character.Options.DragItemOnPlayerOpensSecureTrade,
            toast: dependencies.Toast,
            primedForSatchelReq: () => sess.IsInWorld,
            avatarOnTerrain: () =>
                dependencies.PlayerMode.IsPlayerMode
                && dependencies.AvatarController.Controller is { IsAirborne: false },
            inNonFightingManner: () =>
                dependencies.Actions.Combat.LatestMode == FightingManner.NonCombat,
            fightingPhase: dependencies.Actions.Combat,
            transmitEditFightingManner: manner =>
                sess.LatestSess?.TransmitChangeCombatMode(manner),
            isModuleBundle: dependencies.ArcanaCatalog.IsModuleBundle,
            placeInBackpack: pick.DispatchLift,
            backpackVesselIdent: () => late.SatchelVessel.Current(
                dependencies.PlayerIdentity.SrvOid),
            terrainObjectIdent: () =>
                dependencies.Inventory.ExternalVessels.LatestVesselIdent,
            engagedMerchantIdent: () => dependencies.Inventory.Vendor.MerchantIdent,
            transmitDivideToRealm: (gear, quantity) =>
                sess.LatestSess?.TransmitStackableDivideTo3D(gear, quantity),
            chosenObjectIdent: () =>
                dependencies.Actions.Selection.ChosenObjectTag ?? 0u,
            pileDivideQty: dependencies.StackSplitQuantity,
            sysMsg:
                phrase => dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal),
            interfacePhrase: (phrase, kind) => dependencies.Communication.AddText(phrase, kind),
            transmitPutGearInVessel: (gear, vessel, stance) =>
                sess.LatestSess?.TransmitPutGearInVessel(
                    gear,
                    vessel,
                    stance),
            transmitDivideToVessel: (gear, vessel, stance, quantity) =>
                sess.LatestSess?.TransmitStackableDivideToVessel(
                    gear,
                    vessel,
                    stance,
                    quantity),
            transmitStackableCombine: (src, mark, quantity) =>
                sess.LatestSess?.TransmitStackableCombine(src, mark, quantity),
            reqExternalVessel: oid =>
            {
                var container = dependencies.Inventory.Objects.Get(oid);
                bool isCorpse = container is not null
                    && ((PublicWeenieBits)(container.PublicWeenieBitfield ?? 0u)
                        & PublicWeenieBits.Corpse) != 0;
                dependencies.Inventory.ExternalVessels.ReqOpen(oid, isCorpse);
            },
            reqUse: pick.RequestUse,
            transmitPurchase: (merchantOid, gearOid, quantity, alternateCurrencyIdent) =>
            {
                if (sess.LatestSess is not { } engagedSess || !sess.IsInWorld)
                    return false;
                engagedSess.SendBuy(merchantOid, gearOid, quantity, alternateCurrencyIdent);
                return true;
            },
            transmitPurchaseAll: (merchantOid, gearList, alternateCurrencyIdent) =>
            {
                if (sess.LatestSess is not { } engagedSess || !sess.IsInWorld)
                    return false;
                engagedSess.SendBuy(merchantOid, gearList, alternateCurrencyIdent);
                return true;
            },
            transmitVend: (merchantOid, gearList) =>
            {
                if (sess.LatestSess is not { } engagedSess || !sess.IsInWorld)
                    return false;
                engagedSess.TransmitVend(merchantOid, gearList);
                return true;
            },
            transmitSalvage: (toolOid, gearOids) =>
            {
                if (sess.LatestSess is not { } engagedSess || !sess.IsInWorld)
                    return false;
                engagedSess.TransmitSalvage(toolOid, gearOids);
                return true;
            });
    }

    public ArcanaEngine BuildMagicCore(
        DealingRetainedWidgetDependencies dependencies,
        DealingWidgetLateWiring late,
        GearDealingDriver gearDealing)
    {
        return ArcanaEngine.Create(
            dependencies.ArcanaCatalog,
            dependencies.Actions.SpellCast,
            dependencies.SpellCastOperations,
            dependencies.Inventory.Objects,
            ownAvatarIdent: () => dependencies.PlayerIdentity.SrvOid,
            acctLabel: () => late.Session.AcctMoniker
                ?? dependencies.Options.LiveUser
                ?? string.Empty,
            haltCompletely: dependencies.CombatAttackOperations.ReadyAssaultReq,
            transmitUntargeted: arcanumIdent =>
                late.Session.LatestSess?.TransmitCastingUntargetedArcanum(arcanumIdent),
            transmitTargeted: (mark, arcanumIdent) =>
                late.Session.LatestSess?.TransmitCastingTargetedArcanum(
                    mark,
                    arcanumIdent),
            readoutMsg:
                phrase => dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal),
            incrementOccupied: gearDealing.IncrementOccupiedTally,
            canTransmit: () => late.Session.IsInWorld);
    }

    public RetainedWidgetAssembly BuildKeptWidget(
        DealingRetainedWidgetDependencies dependencies,
        DealingWidgetLateWiring late,
        CanonWidgetEngineLease tenancy,
        SimFightingAttackLedger fightingAssault,
        GearDealingDriver gearDealing,
        ArcanaEngine magic,
        Action<DealingRetainedWidgetAssemblyPoint> checkpoint)
    {
        IDisposable? feedGrab = null;
        IDisposable? satchelVessel = null;
        try
        {
            VitalsModel vitals = dependencies.ExistingVitals
                ?? new VitalsModel(
                    dependencies.Actions.Combat,
                    dependencies.Character.LocalPlayer);
            WidgetHub hub = tenancy.AcquireHost(
                () => new WidgetHub(
                    dependencies.GpuDevice,
                    dependencies.GpuFrameSource,
                    dependencies.ShadersDirectory,
                    dependencies.DebugFont,
                    dependencies.HostQuiescence));
            checkpoint(DealingRetainedWidgetAssemblyPoint.UiHostAcquired);
            hub.TextRenderer.LinearTwinResolver = dependencies.TextureCache.FetchOrBuildLinearWidgetTwin;
            feedGrab = dependencies.RetainedInputCapture.Bind(hub.Root);
            checkpoint(DealingRetainedWidgetAssemblyPoint.InputCaptureBound);
            hub.Root.WidgetBolted = dependencies.Character.Options.GetOptionBit(
                CharacterOptionId.LockUI);

            CursorFeedbackDriver curFeedback = new CursorFeedbackDriver(
                gearDealing,
                realmMarkSupplier: () =>
                    late.Selection.ChooseAtCursor(includeSelf: true) ?? 0u,
                fightingMannerSupplier: () =>
                    dependencies.Actions.Combat.LatestMode);
            CanonCursorKeeper curKeeper = new CanonCursorKeeper(dependencies.Dats, dependencies.DatLock);
            checkpoint(DealingRetainedWidgetAssemblyPoint.CursorAssetsCreated);

            ToonTitlePicker toonBannerLocator = new ToonTitlePicker(dependencies.Dats);
            DatStringPicker toonWidgetTexts = new DatStringPicker(dependencies.Dats);
            ToonSheetSupplier toonSheet = new ToonSheetSupplier(
                dependencies.Inventory.Objects,
                dependencies.Character.LocalPlayer,
                playerGuid: () => dependencies.PlayerIdentity.SrvOid,
                engagedToonLabel: () => dependencies.Settings.EngagedToonTag,
                backupSheet: SpecimenBlob.SampleCharacter,
                canTransmitEmit: () => late.SimCore.IsInWorld,
                transmitEmitAttr: (statIdent, price) =>
                    late.SimCore.Advance(
                        SimProgressionKind.Attribute,
                        statIdent,
                        price),
                transmitEmitVital: (statIdent, price) =>
                    late.SimCore.Advance(
                        SimProgressionKind.Vital,
                        statIdent,
                        price),
                transmitEmitAptitude: (statIdent, price) =>
                    late.SimCore.Advance(
                        SimProgressionKind.Skill,
                        statIdent,
                        price),
                transmitTrainAptitude: (statIdent, credits) =>
                    late.SimCore.Advance(
                        SimProgressionKind.TrainSkill,
                        statIdent,
                        credits),
                banners: dependencies.Character.Titles,
                locateReadoutBanner: bannerIdent =>
                {
                    lock (dependencies.DatLock) return toonBannerLocator.Resolve(bannerIdent);
                },
                locateWidgetString: tag =>
                {
                    lock (dependencies.DatLock)
                        return toonWidgetTexts.Resolve(0x23000001u, DatStringPicker.CalculateDigest(tag));
                });
            checkpoint(DealingRetainedWidgetAssemblyPoint.CharacterSheetCreated);

            uint MagicAptitudeTier(MechMagicSchool school)
            {
                static uint AptitudeIdent(MechMagicSchool val) => val switch
                {
                    MechMagicSchool.CreatureEnchantment => 0x1Fu,
                    MechMagicSchool.ItemEnchantment => 0x20u,
                    MechMagicSchool.LifeMagic => 0x21u,
                    MechMagicSchool.WarMagic => 0x22u,
                    MechMagicSchool.VoidMagic => 0x2Bu,
                    _ => 0u,
                };

                uint aptitudeIdent = AptitudeIdent(school);
                if (aptitudeIdent is not 0u)
                    return dependencies.Character.LocalPlayer.FetchAptitude(aptitudeIdent)?.LatestTier ?? 0u;

                uint highest = 0u;
                foreach (MechMagicSchool contender in new[]
                {
                    MechMagicSchool.CreatureEnchantment,
                    MechMagicSchool.ItemEnchantment,
                    MechMagicSchool.LifeMagic,
                    MechMagicSchool.WarMagic,
                    MechMagicSchool.VoidMagic,
                })
                {
                    highest = Math.Max(
                        highest,
                        dependencies.Character.LocalPlayer.FetchAptitude(
                            AptitudeIdent(contender))?.LatestTier ?? 0u);
                }
                return highest;
            }

            foreach (IMouse pointer in dependencies.Input.Mice)
                hub.WirePointer(pointer);
            checkpoint(DealingRetainedWidgetAssemblyPoint.MouseInputWired);
            foreach (IKeyboard keyboard in dependencies.Input.Keyboards)
                hub.WireKeyboard(keyboard);
            checkpoint(DealingRetainedWidgetAssemblyPoint.KeyboardInputWired);

            (uint, int, int) LocateChrome(uint ident)
            {
                uint texture = dependencies.TextureCache.FetchOrPushRasterizeCanvas(
                    ident,
                    out int width,
                    out int height);
                return (texture, width, height);
            }

            GlyphComposer glyphComposer = new GlyphComposer(dependencies.Dats, dependencies.TextureCache);
            ClientControlsIni controls = dependencies.Options.AcDir is { } acDirection
                ? ClientControlsIni.Load(Path.Combine(acDirection, "controls", "controls.ini"))
                : ClientControlsIni.Parse(string.Empty);
            WidgetDatFont? defaultTypeface;
            lock (dependencies.DatLock)
                defaultTypeface = WidgetDatFont.Load(dependencies.Dats, dependencies.TextureCache);
            var datTypefaceStash = new ConcurrentDictionary<uint, WidgetDatFont?>();
            if (defaultTypeface is not null)
                datTypefaceStash.TryAdd(WidgetDatFont.DefaultTypefaceIdent, defaultTypeface);
            WidgetDatFont? LocateDatTypeface(uint typefaceDid) =>
                datTypefaceStash.GetOrAdd(typefaceDid, ident =>
                {
                    lock (dependencies.DatLock)
                        return WidgetDatFont.Load(dependencies.Dats, dependencies.TextureCache, ident);
                });
            dependencies.Log(defaultTypeface is not null
                ? "[UI] vitals dat-font 0x40000000 loaded for numeric overlay."
                : "[UI] vitals dat-font 0x40000000 unavailable — falling back to debug font.");
            checkpoint(DealingRetainedWidgetAssemblyPoint.UiAssetsCreated);

            hub.Root.Width = dependencies.Window.Size.X;
            hub.Root.Height = dependencies.Window.Size.Y;
            ChatModel comms = BuildCommsLensModel(dependencies);
            var arrangementVault =
                dependencies.Settings.ArrangementStore;
            CanonWidgetPersistenceWiring? persistence = arrangementVault is null
                ? null
                : new CanonWidgetPersistenceWiring(
                    arrangementVault,
                    CharacterKey: () => dependencies.Settings.EngagedToonTag,
                    ScreenSize: () => (dependencies.Window.Size.X, dependencies.Window.Size.Y));
            void InspectTrace(string msg) => dependencies.Log("[UI-PROBE] " + msg);
            string screenshotFolder =
                dependencies.Options.WidgetSensorTurnedOn
                && dependencies.Options.AutomationArtifactDirectory is { } artifactFolder
                    ? Path.Combine(artifactFolder, "screenshots")
                    : !string.IsNullOrWhiteSpace(dependencies.ScreenshotsDirectory)
                        ? dependencies.ScreenshotsDirectory
                        : Path.Combine(
                            Path.GetDirectoryName(dependencies.KeyBindingsFilePath)!,
                            "screenshots");
            FrameScreenshotDriver screenshots = new FrameScreenshotDriver(
                dependencies.BackbufferReader,
                screenshotFolder,
                InspectTrace,
                dependencies.RenderPackDiagnostics);
            checkpoint(DealingRetainedWidgetAssemblyPoint.UiProbeCreated);

            CanonWidgetAssets holdings = new CanonWidgetAssets(
                dependencies.Dats,
                dependencies.DatLock,
                LocateChrome,
                LocateDatTypeface,
                defaultTypeface,
                dependencies.DebugFont,
                controls,
                glyphComposer,
                dependencies.TextureCache);
            DatStringPicker toonCreationTexts = new DatStringPicker(dependencies.Dats);
            ChargenSkillScorePicker chargenAptitudeScoreLocator = new ChargenSkillScorePicker(
                dependencies.Runtime.CharacterCreation.Options);
            MacAC.Mechanics.Contracts.QuestCatalogue? contractRegistry = null;
            MacAC.Mechanics.Contracts.QuestCatalogue questRegistry()
            {
                if (contractRegistry is not null)
                    return contractRegistry;
                lock (dependencies.DatLock)
                    contractRegistry = MacAC.Assets.QuestTableReader.Load(dependencies.Dats);
                return contractRegistry;
            }

            CanonWidgetEngineWiring mappings = new CanonWidgetEngineWiring(
                Host: hub,
                Assets: holdings,
                Vitals: new VitalsEngineWiring(vitals),
                Chat: new CommsEngineWiring(
                    comms,
                    () => late.Session.Commands,
                    dependencies.Communication.CommsPanes,
                    arrangementVault),
                Radar: new RadarEngineWiring(
                    late.Radar.Snapshot,
                    dependencies.Actions.Selection,
                    dependencies.Settings.ReqWidgetBolted),
                Combat: new FightingEngineWiring(
                    dependencies.Actions.Combat,
                    fightingAssault),
                Magic: new ArcanaEngineWiring(
                    dependencies.Character.Spellbook,
                    magic.Casting,
                    dependencies.Inventory.Objects,
                    () => dependencies.PlayerIdentity.SrvOid,
                    dependencies.ArcanaCatalog.Components,
                    glyphComposer.FetchGlyph,
                    glyphComposer.FetchPullGlyph,
                    glyphComposer.FetchArcanumGlyph,
                    glyphComposer.FetchArcanumModuleGlyph,
                    dependencies.Actions.Selection,
                    dependencies.ArcanaCatalog.FetchArcanumTier,
                    magic.FetchExamineModules,
                    MagicAptitudeTier,
                    oid => dependencies.Actions.Selection.Select(
                        oid,
                        PickChangeSource.Inventory),
                    oid => late.Session.TryUseGear(oid, dependencies.Log),
                    (tab, locus, arcanumIdent) =>
                        late.SimCore.AppendFavorite(tab, locus, arcanumIdent),
                    (tab, arcanumIdent) =>
                        late.SimCore.RemoveFavorite(tab, arcanumIdent),
                    filters => late.SimCore.AssignGrimoireSift(filters),
                    arcanumIdent => late.SimCore.DropArcanum(arcanumIdent),
                    (moduleIdent, quantity) =>
                        late.SimCore.SetDesiredComponent(
                            moduleIdent,
                            quantity),
                    dependencies.ClientTime),
                JumpPowerbar: new JumpPowerbarEngineWiring(
                    () => dependencies.AvatarController.Controller?.JumpCharge ?? default),
                Fps: BuildFpsMappings(
                    dependencies.BuildingDegrades,
                    () => dependencies.Settings.ReadoutPreview.ShowFps),
                VividTarget: new VividTargetEngineWiring(
                    dependencies.Actions.Selection,
                    () => dependencies.PlayerIdentity.SrvOid,
                    () => dependencies.Character.Options.GetOptionBit(
                        CharacterOptionId.VividTargetingIndicator),
                    late.Selection.LocateVividObjectiveDetails,
                    late.PickCam.WidgetCapture),
                Indicators: new IndicatorEngineWiring(
                    dependencies.Character.Spellbook,
                    dependencies.Inventory.Objects,
                    () => dependencies.PlayerIdentity.SrvOid,
                    () => dependencies.Character.LocalPlayer.FetchNetAttr(
                        SelfState.StatKind.Strength),
                    () => late.Session.ConnectCondition,
                    dependencies.ClientTime,
                    () => late.Session.LatestSess?.ReqConnectConditionPing(),
                    EndCharacterSession: dependencies.TeleportSink.ReqSignout,
                    ExitGame: dependencies.Window.Close),
                Toolbar: new ToolbarEngineWiring(
                    dependencies.Inventory.Objects,
                    dependencies.Inventory.Shortcuts,
                    glyphComposer.FetchGlyph,
                    glyphComposer.FetchPullGlyph,
                    oid => late.Session.TryUseGear(oid, dependencies.Log),
                    dependencies.Actions.Combat,
                    dependencies.Inventory.ItemMana,
                    dependencies.CombatModeCommands.Toggle,
                    gearDealing,
                    listing => late.SimCore.AppendShortcut(listing),
                    ordinal => late.SimCore.DropShortcut(ordinal),
                    dependencies.Actions.Selection,
                    handler => dependencies.Actions.Combat.HealthChanged += handler,
                    handler => dependencies.Actions.Combat.HealthChanged -= handler,
                    late.Selection.ShouldShowHealth,
                    oid => dependencies.Inventory.Objects.Get(oid)?.FetchAppropriateLabel(),
                    dependencies.Actions.Combat.FetchHealthPct,
                    dependencies.Actions.Combat.HasHealth,
                    oid =>
                        (uint)(dependencies.Inventory.Objects.Get(oid)?.StackSize ?? 0),
                    oid => late.Session.LatestSess?.TransmitAskHealth(oid),
                    oid => late.Session.LatestSess?.TransmitAskGearMana(oid),
                    () => dependencies.PlayerIdentity.SrvOid,
                    (gear, vessel, stance) =>
                        late.Session.LatestSess?.TransmitPutGearInVessel(
                            gear,
                            vessel,
                            stance),
                    oid =>
                        dependencies.Inventory.Vendor.MerchantIdent is not 0u
                        && dependencies.Inventory.Objects.Get(oid) is { } merchantContender
                        && merchantContender.VesselTag == dependencies.Inventory.Vendor.MerchantIdent
                        && VendorSplitRules.IsDivideExempt(merchantContender.Type)),
                Character: new ToonEngineWiring(
                    toonSheet,
                    dependencies.Character.Titles,
                    toonBannerLocator,
                    SendSetTitle: late.SimCore.SetTitle),
                Inventory: new StashEngineWiring(
                    dependencies.Inventory.Objects,
                    () => dependencies.PlayerIdentity.SrvOid,
                    glyphComposer.FetchGlyph,
                    glyphComposer.FetchPullGlyph,
                    () => dependencies.Character.LocalPlayer.FetchNetAttr(
                        SelfState.StatKind.Strength),
                    dependencies.Character.Spellbook,
                    oid => late.Session.LatestSess?.SendUse(oid),
                    (gear, vessel, stance) =>
                        late.Session.LatestSess?.TransmitPutGearInVessel(
                            gear,
                            vessel,
                            stance),
                    (gear, vessel, stance, quantity) =>
                        late.Session.LatestSess?.TransmitStackableDivideToVessel(
                            gear,
                            vessel,
                            stance,
                            quantity),
                    (src, mark, quantity) =>
                        late.Session.LatestSess?.TransmitStackableCombine(
                            src,
                            mark,
                            quantity),
                    gearDealing,
                    dependencies.Actions.Selection),
                ExternalContainer: new ExternalContainerEngineWiring(
                    dependencies.Inventory.ExternalVessels,
                    dependencies.Inventory.Objects,
                    glyphComposer.FetchGlyph,
                    glyphComposer.FetchPullGlyph,
                    gearDealing,
                    dependencies.Actions.Selection,
                    oid => late.Session.LatestSess?.SendUse(oid),
                    (gear, vessel, stance) =>
                        late.Session.LatestSess?.TransmitPutGearInVessel(
                            gear,
                            vessel,
                            stance),
                    (gear, vessel, stance, quantity) =>
                        late.Session.LatestSess?.TransmitStackableDivideToVessel(
                            gear,
                            vessel,
                            stance,
                            quantity),
                    late.Selection.IsWithinExternalVesselUseRange),
                Vendor: new MerchantEngineWiring(
                    dependencies.Inventory.Vendor,
                    glyphComposer.FetchGlyph,
                    gearDealing,
                    dependencies.Actions.Selection,
                    phrase => dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal)),
                Cursor: new CanonWidgetCursorWiring(curFeedback, curKeeper),
                WorldTooltip: new RealmTooltipEngineWiring(
                    HoverGuidAtCursor: () => late.Selection.ChooseAtCursor(includeSelf: true),
                    ResolveName: oid => dependencies.Inventory.Objects.Get(oid)?.FetchAppropriateLabel(),
                    Enabled: () => dependencies.Character.Options.GetOptionBit(
                        CharacterOptionId.ShowTooltips)),
                Confirmations: new ConfirmationEngineWiring(
                    (kind, ctx, approved) =>
                        late.Session.LatestSess?.TransmitAckResponse(
                            kind,
                            ctx,
                            approved)),
                Appraisal: new AssayEngineWiring(
                    toonSheet.ToonLabel,
                    (gear, inscription) =>
                        late.Session.LatestSess?.TransmitSetInscription(
                            gear,
                            inscription),
                    phrase =>
                        dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal),
                    LocalFactionBits: () =>
                        dependencies.Character.LocalPlayer.Properties.FetchInt(
                            (uint)TraitInt.Faction1Bits)),
                Options: new KnobsEngineWiring(
                    CommandBus: () => late.Session.Commands,
                    IsGrounded: () =>
                        dependencies.PlayerMode.IsPlayerMode
                        && dependencies.AvatarController.Controller is { } onlineDriver
                            ? !onlineDriver.IsAirborne
                            : (bool?)null,
                    IsUseMouseTurningEnabled: () =>
                        ToonOptionChart.TryGet(
                            CharacterOptionId.UseMouseTurning,
                            out ToonOptionChartEntry entry)
                        && (dependencies.Character.Options.Options2 & entry.Mask) is not 0u,
                    DisplaySystemMessage: phrase =>
                        dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal),
                    DisplayMouseTurningMacroLine: phrase =>
                        dependencies.Communication.AddText(phrase, CanonLogTextType.Magic),
                    LoadCameraTurning: dependencies.Settings.FetchCamTurning,
                    SaveCameraTurning: dependencies.Settings.StoreCamTurning,
                    CurrentCharacterOption: dependencies.Character.Options.GetOptionBit,
                    LoadDisplay: () => dependencies.Settings.Readout,
                    SaveDisplay: dependencies.Settings.StoreReadout,
                    LoadAudio: () => dependencies.Settings.Audio,
                    SaveAudio: dependencies.Settings.StoreSound,
                    LoadRenderPackChoices: dependencies.RenderPackCatalog is null
                        ? null
                        : () => dependencies.RenderPackCatalog.Freeze().Listings
                            .Select(listing =>
                                new SettingsKnobsSheetDriver.RasterizeBundlePick(
                                    listing.Descriptor.Id,
                                    listing.Descriptor.DisplayName,
                                    listing.Descriptor.PackVersion.ToString(),
                                    listing.IsCompatible,
                                    listing.IncompatibilityReason,
                                    listing.Descriptor.QualityPresets
                                        .Select(preset =>
                                        {
                                            listing.PresetIncompatibilityReasons.TryGetValue(
                                                preset.Id,
                                                out string? cause);
                                            return new SettingsKnobsSheetDriver.RasterizeBundlePresetPick(
                                                preset.Id,
                                                preset.DisplayName,
                                                listing.IsCompatible && cause is null,
                                                cause ?? listing.IncompatibilityReason)
                                            {
                                                SettingSubstitutions = preset.SettingOverrides,
                                                UpperHousedGpuOctets = preset.MaxResidentGpuBytes,
                                                UpperIncrementalGpuMillisP50 =
                                                    preset.MaxIncrementalGpuMillisecondsP50,
                                                UpperIncrementalGpuMillisP99 =
                                                    preset.MaxIncrementalGpuMillisecondsP99,
                                                UpperIncrementalCpuMillisP50 =
                                                    preset.MaxIncrementalCpuMillisecondsP50,
                                                UpperIncrementalCpuMillisP99 =
                                                    preset.MaxIncrementalCpuMillisecondsP99,
                                            };
                                        })
                                        .ToArray())
                                {
                                    FeatureSummary = listing.Descriptor.FeatureSummary,
                                    Prefs = listing.Descriptor.Settings,
                                })
                            .ToArray(),
                    LoadRenderPackCatalogRevision: dependencies.RenderPackCatalog is null
                        ? null
                        : () => dependencies.RenderPackCatalog.Rev,
                    LoadRenderPackFailureNotice: dependencies.RenderPackDiagnostics is null
                        ? null
                        : () => dependencies.RenderPackDiagnostics().FailureReason),
                Social: new SocialEngineWiring(
                    () => dependencies.Runtime.Fellowship.Snapshot,
                    () => dependencies.Runtime.Allegiance.Snapshot,
                    dependencies.Communication.Friends,
                    dependencies.Communication.Squelch,
                    dependencies.Runtime.Fellowship.FetchParticipants,
                    late.SimCore.FellowshipCreate,
                    late.SimCore.FellowshipRecruit,
                    late.SimCore.FellowshipDismiss,
                    late.SimCore.FellowshipQuit,
                    late.SimCore.FellowshipAssignLeader,
                    late.SimCore.FellowshipSetOpen,
                    late.SimCore.FellowshipSetBoardOpen,
                    dependencies.Actions.Selection,
                    () => dependencies.PlayerIdentity.SrvOid,
                    AllegianceMonarch: () =>
                        dependencies.Runtime.Allegiance.TryFetchMonarch(out var monarch)
                            ? monarch
                            : (SimAllegianceMemberCapture?)null,
                    AllegiancePatron: oid =>
                        dependencies.Runtime.Allegiance.TryGetPatron(oid, out var patron)
                            ? patron
                            : (SimAllegianceMemberCapture?)null,
                    AllegianceMember: oid =>
                        dependencies.Runtime.Allegiance.TryFetchParticipant(oid, out var participant)
                            ? participant
                            : (SimAllegianceMemberCapture?)null,
                    AllegianceVassals: dependencies.Runtime.Allegiance.GetVassals,
                    AllegianceSwear: late.SimCore.AllegianceSwear,
                    AllegianceBreak: late.SimCore.AllegianceBreak,
                    AllegianceKick: late.SimCore.AllegianceKick,
                    AllegianceSetUpdateSubscription: late.SimCore.AllegianceSetRefreshSubscription,
                    Trade: dependencies.Runtime.Trade),
                MapHouse: new MapDwellingEngineWiring(
                    CurrentCalendar: dependencies.CurrentCalendar,
                    PlayerCellId: () => dependencies.AvatarController.Controller?.CellId ?? 0u,
                    HousePosition: () => dependencies.Runtime.HouseOwner.Position,
                    HouseLines: () => dependencies.Runtime.HouseOwner.Lines,
                    HousePanelLines: () => dependencies.Runtime.HouseOwner.BoardStrokes),
                Quests: new QuestEngineWiring(
                    Contracts: dependencies.Runtime.ContractsHolder.View,
                    Catalog: questRegistry,
                    Journal: dependencies.Runtime.JournalHolder.View,
                    JournalCommands: dependencies.Runtime.JournalHolder,
                    PlayerCell: () => dependencies.AvatarController.Controller?.CellId ?? 0u,
                    AbandonContract: contractIdent =>
                        late.Session.LatestSess?.TransmitAbandonContract(contractIdent),
                    JournalDirectory: System.IO.Path.Combine(
                        MacAC.Host.UserStateLayout.Locate().Data,
                        "journal"),
                    Report: msg =>
                        dependencies.Communication.Chat.OnSysMsg(msg, 0x0Fu)),
                StackSplitQuantity: dependencies.StackSplitQuantity,
                Plugins: dependencies.UiRegistry,
                Persistence: persistence,
                Probe: new CanonWidgetProbeWiring(
                    dependencies.Options.WidgetSensorTurnedOn,
                    dependencies.Options.UiProbeScript,
                    dependencies.Options.UiProbeDump,
                    InspectTrace,
                    act => dependencies.InputRouter?.TryInvokeAutomationAct(act) == true,
                    (act, pinned) =>
                        dependencies.InputRouter?.TrySetAutomationActPinned(act, pinned) == true,
                    late.Automation,
                    QueueMouseLookDelta: (dx, dy) =>
                        dependencies.GameplayInputFrame?.Invoke()?.EnqueueRawPointerDiff(dx, dy)),
                Keyboard: new KeyboardEngineWiring(
                    dependencies.InputRouter,
                    dependencies.KeyBindingsFilePath),
                CharacterSelection: new ToonPickingEngineWiring(
                    () => late.SimCore.ToonPick,
                    late.SimCore.ToonPickHighlight,
                    late.SimCore.ToonPickJoin,
                    late.SimCore.ToonPickReqErase,
                    late.SimCore.ToonPickConfirmErase,
                    late.SimCore.ToonPickRevert,
                    late.SimCore.ToonPickAbort,
                    dependencies.Window.Close,
                    DirectCharacterLaunch: dependencies.Options.LiveCharacterSelector is not null),
                CharacterCreation: new ToonCreationEngineWiring(
                        () => late.SimCore.ToonCreation,
                        late.SimCore.ToonCreationPickLineage,
                        late.SimCore.ToonCreationPickGender,
                        late.SimCore.ToonCreationPickBlueprint,
                        late.SimCore.ToonCreationSetAttr,
                        late.SimCore.ToonCreationSetAttrLock,
                        late.SimCore.ToonCreationTrainAptitude,
                        late.SimCore.ToonCreationSpecializeAptitude,
                        late.SimCore.ToonCreationUntrainAptitude,
                        late.SimCore.ToonCreationPickBeginArea,
                        late.SimCore.ToonCreationComplete,
                        RequestExit: () => { },
                        SetAppearanceIndex: late.SimCore.ToonCreationSetLooksOrdinal,
                        SetShade: late.SimCore.ToonCreationSetShade,
                        ResolveText: tag =>
                        {
                            lock (dependencies.DatLock)
                            {
                                return toonCreationTexts.Resolve(
                                    0x23000002u,
                                    DatStringPicker.CalculateDigest(tag));
                            }
                        },
                        SetName: late.SimCore.ToonCreationSetLabel,
                        AcknowledgeRejection: late.SimCore.ToonCreationAcknowledgeRejection,
                        RandomizeCharacter: late.SimCore.ToonCreationRandomizeToon,
                        RandomizeAppearance: late.SimCore.ToonCreationRandomizeLooks,
                        RandomizeClothing: late.SimCore.ToonCreationRandomizeClothing,
                        GetSkillScore: chargenAptitudeScoreLocator.Resolve,
                    OpenOnStart: dependencies.Options.OpenCharacterCreationOnStart),
                CaptureScreenshot: () =>
                {
                    if (screenshots.TryReqCanonScreenshot(
                            out string trail,
                            out string problem))
                    {
                        dependencies.Communication.AddText(
                            $"Screenshot saved to {trail}",
                            CanonLogTextType.ClientLocal);
                    }
                    else
                    {
                        dependencies.Communication.AddText(
                            $"Screenshot failed: {problem}",
                            CanonLogTextType.ClientLocal);
                    }
                },
                ProjectileDebugSamples: dependencies.Automation is null
                    ? null
                    : dependencies.Automation.GrabMissileDiagSpecimens,
                Connection: new ConnectionEngineWiring(
                    () => late.SimCore.Connection, dependencies.Window.Close,
                    ShowProgress: dependencies.Options.LiveCharacterSelector is null),
                IsGameplayDisplay: () => dependencies.Settings.IsGameplayReadout,
                SynchronizeDisplayPhase: () =>
                {
                    if (late.SimCore.Connection?.Snapshot.Status is
                            MacAC.Sim.Presence.SimLinkStatus.Failed or
                            MacAC.Sim.Presence.SimLinkStatus.Unsupported
                        || late.SimCore.ToonPick?.Snapshot.Error is not null)
                        dependencies.Settings.FinishStraightLaunch();
                    dependencies.Settings.AssignGameplayReadout(
                        !dependencies.Options.LiveMode || late.SimCore.ToonPick?.Snapshot.Lifecycle ==
                            MacAC.Sim.Presence.SimToonPickLifespan.InWorld);
                });
            var core = tenancy.Mount(
                () => CanonWidgetEngine.BuildUninitialized(mappings));
            checkpoint(DealingRetainedWidgetAssemblyPoint.UiRuntimeMounted);
            dependencies.Settings.SrvKnobsSeeded = () =>
            {
                core.OptionsPanelController?.OnSrvOptionsSeeded();
                core.FightingWidgetDriver?.OnSrvKnobsSeeded();
            };
            satchelVessel = late.SatchelVessel.Bind(core);
            checkpoint(DealingRetainedWidgetAssemblyPoint.InventoryContainerBound);

            late.AdoptFeedGrab(feedGrab);
            feedGrab = null;
            late.AdoptSatchelVessel(satchelVessel);
            satchelVessel = null;
            return new RetainedWidgetAssembly(
                hub,
                core,
                vitals,
                comms,
                toonSheet,
                screenshots);
        }
        catch (Exception miss)
        {
            List<Exception>? tidy = null;
            TryFree(ref satchelVessel, "inventory container", ref tidy);
            TryFree(ref feedGrab, "input capture", ref tidy);
            if (tidy is not null)
            {
                tidy.Insert(0, miss);
                throw new AggregateException(
                    "Retained UI construction and local binding rollback failed",
                    tidy);
            }
            throw;
        }
    }
    internal static FpsEngineWiring BuildFpsMappings(
        BuildingDegradeDriver structureDegrades,
        Func<bool> isShown)
    {
        ArgumentNullException.ThrowIfNull(structureDegrades);
        ArgumentNullException.ThrowIfNull(isShown);
        return new FpsEngineWiring(
            () => structureDegrades.Fps,
            () => structureDegrades.EngagedMultiplier,
            isShown);
    }

    internal static ChatModel BuildCommsLensModel(DealingRetainedWidgetDependencies dependencies)
    {
        return new(
            dependencies.Communication.Chat,
            displayLimit: 200,
            directiveMarks: dependencies.Communication.DirectiveMarks)
        {
            OnInterfacePhrase = phrase =>
                dependencies.Communication.AddText(phrase, CanonLogTextType.ClientLocal),
        };
    }
}
