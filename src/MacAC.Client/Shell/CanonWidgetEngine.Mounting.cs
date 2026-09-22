using MacAC.Assets;
using MacAC.Client.Controls;
using MacAC.Client.Link;
using MacAC.Client.Shell.Panels;
using MacAC.Cockpit.Input;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Controls;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Traits;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell;

public sealed partial class CanonWidgetEngine
{
    public static CanonWidgetEngine Mount(CanonWidgetEngineWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        CanonWidgetEngine core = BuildUninitialized(mappings);
        try
        {
            core.Bootstrap();
            return core;
        }
        catch (Exception initializationMiss)
        {
            try
            {
                core.Dispose();
            }
            catch (Exception tidyMiss)
            {
                throw new AggregateException(
                    "Retail UI initialization failed and its partially constructed ownership could not be fully released",
                    initializationMiss,
                    tidyMiss);
            }

            throw;
        }
    }

    /// <summary>
    /// Every panel the retail interface puts up, in the order it mounts them.
    ///
    /// This was thirty bare calls in a row inside the bootstrap. The order is load-bearing — the
    /// toolbar wants the windows it toggles to exist, and the persistence pass at the end wants all
    /// of them — but nothing said so, and nothing could read the list without reading the bootstrap.
    /// As a table the order is the array, and each panel carries the name used when it is the one
    /// that failed.
    /// </summary>
    private static readonly (string Panel, Action<CanonWidgetEngine> Attach)[] PanelsInOrder =
    [
        ("fps readout", static ui => ui.AttachFpsReadout()),
        ("vivid target mark", static ui => ui.AttachVividMarkIndicator()),
        ("projectile debug overlay", static ui => ui.AttachMissileDiagTopLayer()),
        ("vitals", static ui => ui.AttachVitals()),
        ("radar", static ui => ui.AttachRadar()),
        ("chat", static ui => ui.AttachComms()),
        ("floating chat panes", static ui => ui.AttachFloatingCommsPanes()),
        ("toolbar", static ui => ui.AttachToolbar()),
        ("combat", static ui => ui.AttachFighting()),
        ("spellbook", static ui => ui.AttachGrimoire()),
        ("appraisal", static ui => ui.AttachAppraisal()),
        ("effect list", static ui => ui.AttachFxList()),
        ("indicator detail boards", static ui => ui.AttachIndicatorSpecificsBoards()),
        ("options board", static ui => ui.AttachKnobsBoard()),
        ("keyboard settings", static ui => ui.AttachKeyboardSettings()),
        ("indicators", static ui => ui.AttachIndicators()),
        ("jump power bar", static ui => ui.AttachLeapPowerbar()),
        ("popup maker", static ui => ui.AttachPopupMaker()),
        ("tooltips", static ui => ui.AttachHintPresenter()),
        ("social board", static ui => ui.AttachSocialBoard()),
        ("house lookup", static ui => ui.AttachLookupHouseBoard()),
        ("journal", static ui => ui.AttachJournalBoard()),
        ("character sheet", static ui => ui.AttachToon()),
        ("plugin surfaces", static ui => ui.AttachExtensions()),
        ("inventory", static ui => ui.AttachSatchel()),
        ("external container", static ui => ui.AttachExternalVessel()),
        ("vendor", static ui => ui.AttachMerchant()),
        ("secure trade", static ui => ui.AttachSecureBarter()),
        ("salvage", static ui => ui.AttachSalvage()),
        ("item cooldowns", static ui => ui.AttachGearCooldowns()),
    ];

    /// <summary>
    /// Puts every panel up, in order. A panel that throws takes the interface down with it, as it
    /// did before — but it now says which one, instead of leaving a stack trace to be read backwards.
    /// </summary>
    private void MountPanels()
    {
        foreach ((string panel, Action<CanonWidgetEngine> attach) in PanelsInOrder)
        {
            try
            {
                attach(this);
            }
            catch (Exception miss)
            {
                throw new InvalidOperationException(
                    $"The retail {panel} panel could not be mounted.", miss);
            }
        }
    }

    /// <summary>
    /// Imports a layout, reporting it in one place when it is not there. A null means that panel
    /// does not appear; every caller treats it that way.
    /// </summary>
    private ImportedArrangement? ImportOrReport(uint arrangementIdent, string absence)
    {
        ImportedArrangement? arrangement = Import(arrangementIdent);
        if (arrangement is null)
            Console.WriteLine(absence);
        return arrangement;
    }

    /// <inheritdoc cref="ImportOrReport(uint, string)"/>
    private ImportedArrangement? ImportOrReport(uint arrangementIdent, uint trunkElemIdent, string absence)
    {
        ImportedArrangement? arrangement = Import(arrangementIdent, trunkElemIdent);
        if (arrangement is null)
            Console.WriteLine(absence);
        return arrangement;
    }

    private void AttachFpsReadout()
    {
        if (ImportOrReport(CanonFpsDriver.ArrangementId, CanonFpsDriver.ReadoutElemIdent, "[UI] FPS display: SmartBox element 0x10000047 not found")
                is not { } arrangement)
        {
            return;
        }

        var wiring = _bindings.Fps;
        FpsDriver = CanonFpsDriver.Bind(
            arrangement,
            wiring.FramesPerSecond,
            wiring.DegradeMultiplier,
            wiring.IsVisible);
        if (FpsDriver is null)
        {
            Console.WriteLine("[UI] FPS display: SmartBox element isn't UIElement_Text");
            return;
        }

        Host.Root.AddChild(arrangement.Root);
        Console.WriteLine("[UI] retail FPS display from SmartBox LayoutDesc 0x2100000F");
    }

    private void AttachVividMarkIndicator()
    {
        _vividMarkIndicator = VividTargetIndicatorDriver.Mount(
            Host.Root,
            _bindings.Assets,
            _bindings.VividTarget);
        Console.WriteLine(_vividMarkIndicator is null
            ? "[UI] vivid target indicator DAT surfaces not available"
            : "[UI] vivid target indicator mounted from client-enum category 0x10000009");
    }

    private void AttachMissileDiagTopLayer()
    {
        if (_bindings.ProjectileDebugSamples is not { } specimens)
            return;
        _missileDiagTopLayer = ProjectileDebugOverlayDriver.Mount(
            Host.Root,
            specimens,
            _bindings.VividTarget.Camera);
        Console.WriteLine(
            "[PluginUI] projectile collision debug overlay mounted");
    }

    private void AttachVitals()
    {
        if (ImportOrReport(0x2100006Cu, "[UI] vitals: LayoutDesc 0x2100006C not found - vitals not available")
                is not { } arrangement)
        {
            return;
        }

        AttachVitalsArrangement(arrangement);
        CanonPaneCycle.Mount(Host.Root, arrangement.Root, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Vitals,
                Chrome = CanonWindowChrome.Imported,
                Left = 10f,
                Top = 30f,
                RescaleX = true,
                RescaleY = false,
                MinWidth = 40f,
                SubstancePressThrough = false,
            });
        Console.WriteLine("[UI] retail UI active - vitals window from LayoutDesc importer (0x2100006C)");

        AttachFlankVitals();
    }

    private void AttachFlankVitals()
    {
        ElemDetails? details;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(_bindings.Assets.Dats, 0x21000075u);
            arrangement = details is null ? null : ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
        }
        if (details is null || arrangement is null)
        {
            Console.WriteLine("[UI] side vitals: LayoutDesc 0x21000075 not found - SideBySideVitals not available");
            return;
        }

        AttachVitalsArrangement(arrangement);
        CanonPaneCycle.Mount(Host.Root, arrangement.Root, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.FlankVitals,
                Chrome = CanonWindowChrome.Imported,
                Left = 10f,
                Top = 30f,
                RescaleX = true,
                RescaleY = false,
                DatConstraintSrc = details,
                Visible = false,
                SubstancePressThrough = false,
            });
        _vitalsFlankByFlank = new Panels.VitalsSideBySideDriver(
            Host.Root,
            () => _bindings.Options.CurrentCharacterOption(
                (uint)CharacterOptionId.SideBySideVitals),
            PaneLabels.Vitals,
            PaneLabels.FlankVitals);
        Console.WriteLine("[UI] side-by-side vitals window from LayoutDesc importer (0x21000075)");
    }

    private void AttachRadar()
    {
        var arrangement = Import(RadarDriver.LayoutId);
        if (arrangement is null || arrangement.Root is not UiRadar radarTrunk)
        {
            Console.WriteLine("[UI] radar: LayoutDesc 0x21000074 not found or root class mismatch");
            return;
        }

        RadarDriver driver = RadarDriver.Bind(
            arrangement,
            _bindings.Radar.Snapshot,
            oid =>
            {
                if (_bindings.Toolbar.ItemInteraction.OfferPrimaryPress(oid)
                    != GearPrimaryClickResult.NotActive)
                    return;
                _bindings.Radar.Selection.Select(oid, PickChangeSource.Radar);
            },
            setWidgetBolted: _bindings.Radar.SetUiLocked,
            datTypeface: _bindings.Assets.DefaultFont);
        CanonPaneCycle.Mount(Host.Root, radarTrunk, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Radar,
                Chrome = CanonWindowChrome.Imported,
                Left = Math.Max(0f, Host.Root.Width - radarTrunk.Width - 10f),
                Top = 10f,
                Resizable = false,
                RescaleX = false,
                RescaleY = false,
                ConstrainPullToAncestor = true,
                SubstancePressThrough = false,
                Controller = driver,
            });
        Console.WriteLine("[UI] retail radar/compass from LayoutDesc 0x21000074");
    }

    private void AttachComms()
    {
        ElemDetails? details;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(_bindings.Assets.Dats, CommsPaneDriver.LayoutIdent);
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = details is null ? null : ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont,
                texts.Resolve);
        }
        if (details is null || arrangement is null)
        {
            Console.WriteLine("[UI] chat: LayoutDesc 0x2100006F not found");
            return;
        }

        if (_bindings.Chat.Store is { } commsVault)
            _bindings.Chat.Windows.ApplySift(
                ChatPaneState.PrimaryPaneIdent, commsVault.LoadChat().ChatWindowMainFilter);

        CommsPaneDriver? driver = CommsPaneDriver.Bind(
            details,
            arrangement,
            _bindings.Chat.ViewModel,
            _bindings.Chat.CommandBus,
            _bindings.Chat.Windows,
            _bindings.Assets.DefaultFont,
            _bindings.Assets.DebugFont,
            _bindings.Assets.ResolveSprite,
            chosenMarkLabel: () =>
            {
                uint chosen = _bindings.Toolbar.Selection.ChosenObjectTag ?? 0u;
                if (chosen is 0u)
                    return null;
                string? label = _bindings.Toolbar.ResolveName(chosen);
                return string.IsNullOrWhiteSpace(label) ? null : label;
            },
            commsTexts: tag => new DatStringPicker(_bindings.Assets.Dats)
                .Resolve(0x23000001u, DatStringPicker.CalculateDigest(tag)),
            locateTypeface: _bindings.Assets.ResolveFont);
        if (driver is null)
        {
            Console.WriteLine("[UI] chat: needed role elements absent in 0x2100006F");
            return;
        }

        driver.Transcript.Keyboard = Host.Keyboard;
        driver.Input.Keyboard = Host.Keyboard;
        WidgetElem trunk = driver.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Chat,
                Chrome = CanonWindowChrome.Imported,
                Left = 10f,
                Top = 440f,
                DatConstraintSrc = driver.DatPaneDetails,
                AuthoredGeoRevision = 1,
                RescaleX = true,
                RescaleY = true,
                Controller = driver,
                ConditionDriver = driver,
            });
        driver.FastenPane(hnd);
        Host.Root.DefaultPhraseFeed = driver.Input;
        _commsPaneDriver = driver;
        driver.AttachIndicatorPresses(SwitchFloatingCommsPane);
        Console.WriteLine("[UI] retail chat window from LayoutDesc importer (0x2100006F)");
    }

    private void AttachFloatingCommsPanes()
    {
        ElemDetails? details;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(_bindings.Assets.Dats, FloatingCommsPaneDriver.LayoutId);
        }
        if (details is null)
        {
            Console.WriteLine("[UI] floating chat: LayoutDesc 0x2100005B not found");
            return;
        }

        var paneFilters = _bindings.Chat.Windows;
        if (_bindings.Chat.Store is { } vault)
        {
            AttachFloatingCommsPanesBranch(vault, paneFilters);
        }

        (string windowName, float left, float top)[] sockets =
        [
            (PaneLabels.CommsWindow1, 440f, 40f),
            (PaneLabels.CommsWindow2, 440f, 170f),
            (PaneLabels.CommsWindow3, 440f, 300f),
            (PaneLabels.CommsWindow4, 440f, 430f),
        ];

        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        for (int paneIdent = 1; paneIdent <= 4; ++paneIdent)
        {
            ImportedArrangement arrangement;
            lock (_bindings.Assets.DatLock)
            {
                arrangement = ArrangementLoader.Build(
                    details,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve);
            }

            var driver = FloatingCommsPaneDriver.Bind(
                paneIdent,
                details,
                arrangement,
                _bindings.Chat.ViewModel,
                _bindings.Chat.CommandBus,
                paneFilters,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.DebugFont,
                _bindings.Assets.ResolveSprite);
            if (driver is null)
            {
                Console.WriteLine($"[UI] floating chat window {paneIdent}: needed role elements absent in 0x2100005B");
                continue;
            }

            driver.Transcript.Keyboard = Host.Keyboard;
            driver.Input.Keyboard = Host.Keyboard;
            (string paneLabel, float left, float top) = sockets[paneIdent - 1];
            WidgetElem trunk = driver.Root;
            var hnd = CanonPaneCycle.Mount(
                Host.Root,
                trunk,
                _bindings.Assets.ResolveSprite,
                new CanonPaneCycle.Options
                {
                    PaneMoniker = paneLabel,
                    Chrome = CanonWindowChrome.Imported,
                    Left = left,
                    Top = top,
                    DatConstraintSrc = driver.DatPaneInfo,
                    AuthoredGeoRevision = 1,
                    RescaleX = true,
                    RescaleY = true,
                    Visible = false,
                    Controller = driver,
                });
            driver.AffixPane(hnd);
            _floatingCommsDrivers[paneIdent - 1] = driver;
        }

        Console.WriteLine("[UI] retail floating chat windows 1-4 from LayoutDesc importer (0x2100005B)");
    }

    private void AttachFloatingCommsPanesBranch(SettingsVault vault, ChatPaneState paneFilters)
    {
        var comms = vault.LoadChat();
        paneFilters.ApplySift(1, comms.ChatWindow1Filter);
        paneFilters.ApplySift(2, comms.ChatWindow2Filter);
        paneFilters.ApplySift(3, comms.ChatWindow3Filter);
        paneFilters.ApplySift(4, comms.ChatWindow4Filter);
    }

    private void AttachToolbar()
    {
        if (ImportOrReport(0x21000016u, "[UI] toolbar: LayoutDesc 0x21000016 not found")
                is not { } arrangement)
        {
            return;
        }

        var shortcutDigits = PullShortcutDigitVisuals();
        var wiring = _bindings.Toolbar;
        ToolbarDriver = Panels.ToolbarDriver.Bind(
            arrangement, wiring.Objects, wiring.Shortcuts, wiring.ResolveIcon, wiring.UseItem, wiring.Combat,
            shortcutDigits.RegularDigits, shortcutDigits.GhostedDigits,
            shortcutDigits.EmptyDigits, wiring.ItemInteraction,
            wiring.SendAddShortcut, wiring.SendRemoveShortcut,
            flipFighting: wiring.ToggleCombat,
            pickGear: oid => wiring.Selection.Select(oid, PickChangeSource.Toolbar),
            chosenObjectIdent: () => wiring.Selection.ChosenObjectTag ?? 0u,
            pick: wiring.Selection,
            avatarOid: wiring.PlayerGuid,
            transmitPutGearInVessel: wiring.SendPutItemInContainer,
            ammoTypeface: _bindings.Assets.DefaultFont,
            pullGlyphIdents: wiring.ResolveDragIcon);
        ToolbarFeedDriver = new ToolbarInputDriver(ToolbarDriver, wiring.Selection);
        ChosenObjectDriver = Panels.SelectedObjectDriver.Bind(
            arrangement,
            wiring.Selection,
            wiring.SubscribeHealthChanged,
            wiring.UnsubscribeHealthChanged,
            handler => wiring.ItemMana.ItemManaChanged += handler,
            handler => wiring.ItemMana.ItemManaChanged -= handler,
            wiring.IsHealthTarget,
            wiring.ItemInteraction.IsPossessedByAvatar,
            LocateChosenObjectLabel,
            wiring.HealthPercent,
            wiring.HasHealth,
            wiring.StackSize,
            wiring.SendQueryHealth,
            wiring.ItemMana.FetchManaPct,
            wiring.SendQueryItemMana,
            _bindings.Assets.DefaultFont,
            PileDivideQty,
            handler => wiring.Objects.ObjectUpdated += handler,
            handler => wiring.Objects.ObjectUpdated -= handler,
            wiring.IsVendorSplitExempt,
            isCoinstack: oid => wiring.Objects.Get(oid)?.WeenieClassIdent == 273u,
            coinSum: () => wiring.Objects.Get(wiring.PlayerGuid())?.Properties.FetchInt(
                (uint)TraitInt.CoinValue) ?? 0);

        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root, trunk, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Toolbar,
                Chrome = CanonWindowChrome.CollapsibleNineSlice,
                Left = 10f,
                Top = 300f,
                SubstanceWidth = 300f,
                SubstanceHeight = trunk.Height,
                Resizable = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top | MooringRims.Right,
                SubstancePressThrough = true,
                Controller = new RetainedPaneDriverGroup(
                    ToolbarDriver,
                    ChosenObjectDriver),
            });
        ConfigureToolbarFold(arrangement, (WidgetCollapsibleFrame)hnd.OuterCycle, trunk.Height);
        Console.WriteLine("[UI] retail toolbar window from LayoutDesc importer (0x21000016)");
    }

    private void AttachFighting()
    {
        ElemDetails? details;
        ImportedArrangement? arrangement;
        FightingWidgetLabels? captions;
        uint favoriteVacantSprite;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(_bindings.Assets.Dats, FightingWidgetDriver.LayoutId);
            favoriteVacantSprite = details is null
                ? 0u
                : GearListCellTemplate.ResolveEmptySprite(
                    _bindings.Assets.Dats,
                    details,
                    ArcanacastingWidgetDriver.FavoriteRosterIdent);
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = details is null ? null : ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont,
                texts.Resolve);
            captions = details is null ? null : FightingWidgetLabels.Resolve(details, texts);
        }
        if (details is null || arrangement is null || captions is null)
        {
            Console.WriteLine("[UI] combat: LayoutDesc 0x21000073 not found");
            return;
        }
        float fightingWidth = CanonFightingArrangement.FitFavoriteSockets(arrangement);

        FightingWidgetDriver? driver = Panels.FightingWidgetDriver.Bind(
            arrangement,
            _bindings.Combat.State,
            _bindings.Combat.Attacks,
            new Panels.FightingWidgetDriver.BindingsUnit(
                CurrentValue: ident => _bindings.Options.CurrentCharacterOption((uint)ident),
                SetOption: (ident, val) => _bindings.Options.CommandBus().Publish(
                    new SetSingleToonKnobEngineCmd((uint)ident, val))),
            captions,
            shown =>
            {
                if (shown) Host.RevealPane(PaneLabels.Combat);
                else Host.ConcealPane(PaneLabels.Combat);
            });
        if (driver is null)
        {
            Console.WriteLine("[UI] combat: needed controls absent in LayoutDesc 0x21000073");
            return;
        }

        var spellcasting = Panels.ArcanacastingWidgetDriver.Bind(
            arrangement,
            _bindings.Magic.Spellbook,
            _bindings.Magic.Casting,
            _bindings.Magic.Objects,
            _bindings.Magic.PlayerGuid,
            _bindings.Magic.ResolveSpellIcon,
            gear => _bindings.Magic.ResolveDragIcon(
                gear.Type, gear.IconId, gear.GlyphUnderlayIdent,
                gear.GlyphTopLayerIdent, gear.Effects),
            _bindings.Magic.UseItem,
            _bindings.Magic.Selection,
            _bindings.Magic.AddFavorite,
            _bindings.Magic.RemoveFavorite,
            PullShortcutDigitVisuals(),
            favoriteVacantSprite,
            examineArcanum: arcanumIdent => AppraisalDriver?.StudyArcanum(arcanumIdent));
        if (spellcasting is null)
            Console.WriteLine("[UI] spellcasting: needed controls absent in LayoutDesc 0x21000073");

        FightingWidgetDriver = driver;
        ArcanacastingWidgetDriver = spellcasting;
        IRetainedPaneDriver driverHolder = spellcasting is null
            ? driver
            : new RetainedPaneDriverGroup(driver, spellcasting);
        WidgetElem trunk = arrangement.Root;
        CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            CanonFightingArrangement.WithHorizontalRescale(arrangement, new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Combat,
                Chrome = CanonWindowChrome.Imported,
                Left = 0f,
                Top = Math.Max(0f, Host.Root.Height - trunk.Height - 10f),
                SubstanceWidth = fightingWidth,
                DatConstraintSrc = details,
                OuterMoorings = MooringRims.Left | MooringRims.Bottom,
                Visible = false,
                Draggable = false,
                ConstrainPullToAncestor = true,
                SubstancePressThrough = false,
                Controller = driverHolder,
            }));
        driver.SynchronizeVis();
        Console.WriteLine(spellcasting is null
            ? "[UI] retail combat from LayoutDesc 0x21000073; magic binding not available"
            : $"[UI] retail combat + spell bar from LayoutDesc 0x21000073; " +
              $"favorite empty=0x{favoriteVacantSprite:X8}.");
    }

    private void AttachGrimoire()
    {
        ImportedArrangement? arrangement;
        ArcanabookRowStyle? rankStyling;
        ComponentBookTemplateMint? moduleBlueprints;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats,
                ArcanabookWindowDriver.ArrangementIdent,
                ArcanabookWindowDriver.TrunkIdent,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            rankStyling = ArcanabookRowStyle.TryLoad(_bindings.Assets.Dats);
            moduleBlueprints = ComponentBookTemplateMint.TryLoad(
                _bindings.Assets.Dats,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
        }
        if (arrangement is null || rankStyling is null || moduleBlueprints is null)
        {
            Console.WriteLine(
                "[UI] spellbook: layout, spell row, or component list templates not found");
            return;
        }

        WidgetDatFont? rankTypeface = rankStyling.Value.FontDid is 0u
            ? _bindings.Assets.DefaultFont
            : _bindings.Assets.ResolveFont(rankStyling.Value.FontDid)
                ?? _bindings.Assets.DefaultFont;

        ArcanabookWindowDriver? driver = Panels.ArcanabookWindowDriver.Bind(
            arrangement,
            _bindings.Magic.Spellbook,
            _bindings.Magic.Objects,
            _bindings.Magic.PlayerGuid,
            _bindings.Magic.Components,
            _bindings.Magic.Selection,
            _bindings.Magic.ResolveSpellIcon,
            _bindings.Magic.ResolveComponentIcon,
            _bindings.Magic.SpellLevel,
            _bindings.Magic.SelectObject,
            arcanumIdent => ArcanacastingWidgetDriver?.AttachFavorite(arcanumIdent),
            _bindings.Magic.SendSpellbookFilter,
            _bindings.Magic.RemoveSpell,
            arcanumIdent => AppraisalDriver?.StudyArcanum(arcanumIdent),
            (msg, finished) => RevealAck(msg, finished),
            _bindings.Magic.SetDesiredComponent,
            () => ShutPane(PaneLabels.Spellbook),
            moduleBlueprints,
            rankStyling.Value,
            rankTypeface);
        if (driver is null)
        {
            Console.WriteLine("[UI] spellbook: needed controls absent in LayoutDesc 0x21000034");
            return;
        }

        ArcanabookPaneDriver = driver;
        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Spellbook,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 18f,
                Top = 18f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom,
                SubstancePressThrough = false,
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.Magic,
            PaneLabels.Spellbook,
            hnd);
        Console.WriteLine("[UI] retail spellbook/component book from LayoutDesc 0x21000034");
    }

    private void AttachAppraisal()
    {
        ImportedArrangement? arrangement;
        CreatureAssayRowTemplateMint? beastRanks;
        ArcanaExamineComponentTemplateMint? arcanumModuleBlueprints;
        CreatureDisplayNamePicker? beastLabels;
        CanonAssayNamePicker? gearLabels;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats,
                AssayWidgetDriver.ArrangementTag,
                AssayWidgetDriver.TrunkTag,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            beastRanks = CreatureAssayRowTemplateMint.TryLoad(
                _bindings.Assets.Dats,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            arcanumModuleBlueprints =
                ArcanaExamineComponentTemplateMint.TryLoad(
                    _bindings.Assets.Dats,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
            gearLabels = GearLabels;
            beastLabels = _beastLabels;
        }
        if (arrangement is null)
        {
            Console.WriteLine(
                "[UI] examination: LayoutDesc 0x2100006B root 0x100005F2 not found");
            return;
        }

        string? LocateToonBanner(uint bannerIdent)
        {
            lock (_bindings.Assets.DatLock)
                return _bindings.Character.TitleResolver.Resolve(bannerIdent);
        }

        AssayWidgetDriver? driver = AssayWidgetDriver.Bind(
            arrangement,
            _bindings.Inventory.Objects,
            _bindings.Inventory.ItemInteraction,
            _bindings.Inventory.Selection,
            _bindings.Combat.State,
            _bindings.Magic.Spellbook,
            _bindings.Appraisal.PlayerName,
            _bindings.Appraisal.SendSetInscription,
            _bindings.Appraisal.DisplaySystemMessage,
            unhide: () => Host.RevealPane(PaneLabels.Examination),
            shut: () => ShutPane(PaneLabels.Examination),
            beastRankBlueprints: beastRanks,
            beastLabels: beastLabels,
            gearLabels: gearLabels,
            locateArcanumGlyph: _bindings.Magic.ResolveSpellIcon,
            locateModuleGlyph: _bindings.Magic.ResolveComponentIcon,
            arcanumModules: _bindings.Magic.SpellComponents,
            magicAptitude: _bindings.Magic.MagicSkill,
            arcanumModuleBlueprints: arcanumModuleBlueprints,
            locateToonBanner: LocateToonBanner,
            ownFactionBitset: _bindings.Appraisal.LocalFactionBits);
        if (driver is null)
        {
            Console.WriteLine(
                "[UI] examination: needed authored controls are absent");
            return;
        }

        AppraisalDriver = driver;
        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Examination,
                Chrome = CanonWindowChrome.Imported,
                Left = trunk.Left,
                Top = trunk.Top,
                SubstanceWidth = trunk.Width,
                SubstanceHeight = trunk.Height,
                AuthoredGeoRevision = 1,
                Visible = false,
                RescaleX = true,
                RescaleY = true,
                MinWidth = 310f,
                MinHeight = 400f,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstancePressThrough = false,
                Controller = driver,
            });
        BeastAppraisalViewRectWidget =
            arrangement.SeekElem(AssayWidgetDriver.BeastViewRectIdent)
                as WidgetViewport;
        ExaminationCycle = hnd.OuterCycle;
        Console.WriteLine(
            "[UI] retail examination window from LayoutDesc 0x2100006B");
    }

    private void AttachFxList()
    {
        AttachFxListInst(positive: true);
        AttachFxListInst(positive: false);
    }

    private void AttachFxListInst(bool positive)
    {
        uint trunkIdent = positive ? EffectsWidgetDriver.PositiveTrunkIdent : EffectsWidgetDriver.NegativeTrunkIdent;
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        EffectRowTemplateMint? rankBlueprints;
        string pickPrompt;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                EffectsWidgetDriver.LayoutId,
                trunkIdent);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    new DatStringPicker(_bindings.Assets.Dats).Resolve);
            rankBlueprints = EffectRowTemplateMint.TryLoad(
                _bindings.Assets.Dats,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
            pickPrompt = texts.Resolve(
                    0x23000001u,
                    DatStringPicker.CalculateDigest("ID_Effects_Info_SelectASpell"))
                ?? "SELECT A SPELL";
        }
        if (trunkDetails is null || arrangement is null || rankBlueprints is null)
        {
            Console.WriteLine($"[UI] effects: root or row template for 0x{trunkIdent:X8} not found");
            return;
        }

        EffectsWidgetDriver? driver = Panels.EffectsWidgetDriver.Bind(
            arrangement,
            _bindings.Magic.Spellbook,
            positive,
            _bindings.Magic.ServerTime,
            _bindings.Assets.ResolveSprite,
            _bindings.Magic.ResolveSpellIcon,
            rankBlueprints,
            pickPrompt,
            shut: () => ShutPane(
                positive ? PaneLabels.PositiveEffects : PaneLabels.NegativeEffects));
        if (driver is null)
        {
            Console.WriteLine($"[UI] effects: list absent under root 0x{trunkIdent:X8}.");
            return;
        }

        if (positive) PositiveFxListDriver = driver;
        else NegativeFxListDriver = driver;
        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = positive ? PaneLabels.PositiveEffects : PaneLabels.NegativeEffects,
                Chrome = CanonWindowChrome.NineSlice,
                Left = Math.Max(0f, Host.Root.Width - trunk.Width - 12f),
                Top = 18f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            positive ? CanonPaneRegistry.PositiveFxList : CanonPaneRegistry.NegativeFxList,
            positive ? PaneLabels.PositiveEffects : PaneLabels.NegativeEffects,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
    }

    private void AttachIndicatorSpecificsBoards()
    {
        AttachConnectConditionBoard();
        AttachVitaeBoard();
        AttachMiniPlayBoard();
        AttachToonInformationBoard();
    }

    private void AttachToonInformationBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        ToonInfoStrings texts;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                ToonDriver.LayoutId,
                ToonDriver.RootId);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
            texts = ToonInfoStrings.FromDat(tag => locator.LocateAll(
                0x23000001u, DatStringPicker.CalculateDigest(tag)));
        }
        if (trunkDetails is null || arrangement is null) return;

        var driver = ToonDriver.Bind(
            arrangement,
            _bindings.Character.Provider.AssembleSheet,
            _bindings.Assets.DefaultFont,
            () => ShutPane(PaneLabels.CharacterInformation),
            texts,
            _bindings.Character.Provider.EnlistAltered);
        EnrollIndicatorSpecificsBoard(
            CanonPaneRegistry.ToonInformation,
            PaneLabels.CharacterInformation,
            trunkDetails,
            arrangement.Root,
            driver);
    }

    private void AttachConnectConditionBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        ConnectConditionTexts texts;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                LinkStatusWidgetDriver.LayoutId,
                LinkStatusWidgetDriver.RootIdent);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
            var backup = ConnectConditionTexts.English;
            texts = new ConnectConditionTexts(
                locator.Resolve(0x23000001u, 0x0D632F7Fu) ?? backup.Description,
                locator.Resolve(0x23000001u, 0x038FABF3u) ?? backup.Legend,
                locator.Resolve(0x23000001u, 0x05481084u) ?? backup.DisconnectWarning,
                locator.Resolve(0x23000001u, 0x0CADBA93u) ?? backup.PacketLossPrefix,
                locator.Resolve(0x23000001u, 0x0D6485F7u) ?? backup.PingPrefix);
        }
        if (trunkDetails is null || arrangement is null) return;

        LinkStatusWidgetDriver? driver = Panels.LinkStatusWidgetDriver.Bind(
            arrangement,
            _bindings.Indicators.LinkStatus,
            _bindings.Indicators.CurrentTime,
            _bindings.Indicators.RequestLinkStatusPing,
            texts,
            () => ShutPane(PaneLabels.LinkCondition));
        if (driver is null) return;
        ConnectConditionWidgetDriver = driver;
        EnrollIndicatorSpecificsBoard(
            CanonPaneRegistry.ConnectStatus,
            PaneLabels.LinkCondition,
            trunkDetails,
            arrangement.Root,
            driver);
    }

    private void AttachVitaeBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        VitaeTexts texts;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                VitaeWidgetDriver.LayoutId,
                VitaeWidgetDriver.RootId);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
            var backup = VitaeTexts.English;
            texts = new VitaeTexts(
                Locate("ID_Vitae_Text_Full", 0, backup.FullStrength),
                Locate("ID_Vitae_Text_Vitae", 0, backup.LostPrefix),
                Locate("ID_Vitae_Text_Vitae", 1, backup.LostSuffix),
                Locate("ID_Vitae_Text_Skills", 0, backup.SkillsPrefix),
                Locate("ID_Vitae_Text_Skills", 1, backup.SkillsSuffix),
                Locate("ID_Vitae_Text_Experience", 0, backup.RecoveryPrefix),
                Locate("ID_Vitae_Text_Experience", 1, backup.RecoverySuffix));

            string Locate(string tag, int ticket, string backupVal)
                => locator.Resolve(
                    0x23000001u,
                    DatStringPicker.CalculateDigest(tag),
                    ticket) ?? backupVal;
        }
        if (trunkDetails is null || arrangement is null) return;

        VitaeWidgetDriver? driver = Panels.VitaeWidgetDriver.Bind(
            arrangement,
            _bindings.Indicators.Spellbook,
            _bindings.Indicators.Objects,
            _bindings.Indicators.PlayerGuid,
            texts,
            () => ShutPane(PaneLabels.Vitae));
        if (driver is null) return;
        VitaeWidgetDriver = driver;
        EnrollIndicatorSpecificsBoard(
            CanonPaneRegistry.Vitae,
            PaneLabels.Vitae,
            trunkDetails,
            arrangement.Root,
            driver);
    }

    private void AttachMiniPlayBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                MiniGameWidgetDriver.LayoutId,
                MiniGameWidgetDriver.RootId);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    new DatStringPicker(_bindings.Assets.Dats).Resolve);
        }
        if (trunkDetails is null || arrangement is null) return;

        MiniGameWidgetDriver? driver = Panels.MiniGameWidgetDriver.Bind(
            arrangement,
            () => ShutPane(PaneLabels.MiniGame));
        if (driver is null) return;
        MiniPlayWidgetDriver = driver;
        EnrollIndicatorSpecificsBoard(
            CanonPaneRegistry.MiniPlay,
            PaneLabels.MiniGame,
            trunkDetails,
            arrangement.Root,
            driver);
    }

    private void AttachIndicators()
    {
        if (ImportOrReport(IndicatorBarDriver.LayoutId, "[UI] indicator bar: LayoutDesc 0x21000071 not found")
                is not { } arrangement)
        {
            return;
        }

        var wiring = _bindings.Indicators;
        IndicatorBarDriver? driver = Panels.IndicatorBarDriver.Bind(
            arrangement,
            new IndicatorBarWiring(
                wiring.Spellbook,
                wiring.Objects,
                wiring.PlayerGuid,
                wiring.Strength,
                wiring.LinkStatus,
                wiring.CurrentTime,
                boardIdent => _boardWidget.FlipBoard(boardIdent),
                ReqFinishToonSess));
        if (driver is null)
        {
            Console.WriteLine("[UI] indicator bar: one or more authored controls are absent");
            return;
        }

        IndicatorBarDriver = driver;

        lock (_bindings.Assets.DatLock)
        {
            var infos = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, IndicatorBarDriver.LayoutId);
            if (infos is not null)
                driver.FastenPressHighlights(infos, AssembleSwallowedDescendant);
        }

        CanonPaneCycle.Mount(
            Host.Root,
            arrangement.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Indicators,
                Chrome = CanonWindowChrome.Imported,
                Left = 10f,
                Top = 96f,
                Visible = true,
                Resizable = false,
                RescaleX = false,
                RescaleY = false,
                ConstrainPullToAncestor = true,
                SubstancePressThrough = false,
                Controller = driver,
            });
        Console.WriteLine("[UI] retail seven-control indicator bar from LayoutDesc 0x21000071");
    }

    private void AttachKnobsBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                Panels.KnobsBoardDriver.HubLayoutId,
                Panels.KnobsBoardDriver.SocketElementId);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
        }
        if (trunkDetails is null || arrangement is null)
        {
            Console.WriteLine("[UI] options panel: LayoutDesc 0x2100006E slot 0x1000018D not found");
            return;
        }

        var hooks = new Panels.KnobsBoardDriver.CallbacksUnit(
            Toggle: () => FlipPane(PaneLabels.Options),
            RequestExitToCharacterSelection: ReqQuitToToonPick,
            ExitGame: _bindings.Indicators.ExitGame,
            UseMouseTurningSettings: ImposePointerTurningPrefsMacro,
            DisplaySystemMessage: _bindings.Options.DisplaySystemMessage,
            AfterApply: () => _bindings.Options.CommandBus().Publish(
                new SaveToonKnobsEngineCmd()),
            OpenConfigureKeyboard: () => FlipPane(PaneLabels.KeyboardSettings));

        KnobsBoardDriver? driver =
            Panels.KnobsBoardDriver.Bind(arrangement, hooks, _bindings.Assets.ResolveSprite);
        if (driver is null)
        {
            Console.WriteLine("[UI] options panel: needed root didn't build as UiTabPanel");
            return;
        }

        OptionsPanelController = driver;

        lock (_bindings.Assets.DatLock)
        {
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
            bool tied = Panels.ToonKnobsSheetDriver.Bind(
                arrangement,
                driver.ToonSheet,
                blueprintLocator: (blueprintArrangementIdent, blueprintElemIdent) =>
                {
                    var details = ArrangementLoader.ImportInfos(
                        _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent);
                    return details is null
                        ? null
                        : ArrangementLoader.Build(
                            details,
                            _bindings.Assets.ResolveSprite,
                            _bindings.Assets.DefaultFont,
                            _bindings.Assets.ResolveFont,
                            texts.Resolve,
                            blueprintArrangementIdent).Root;
                },
                locateString: (chartIdent, stringIdent) => texts.Resolve(chartIdent, stringIdent),
                new Panels.ToonKnobsSheetDriver.ClientBindings(
                    CurrentValue: ident => _bindings.Options.CurrentCharacterOption((uint)ident),
                    SetOption: (ident, val) => _bindings.Options.CommandBus().Publish(
                        new SetSingleToonKnobEngineCmd((uint)ident, val))));
            if (!tied)
                Console.WriteLine("[UI] options panel: Character tab rows didn't bind");
        }

        lock (_bindings.Assets.DatLock)
        {
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);

            if (!Panels.CommsKnobsDatDefaults.TryRead(
                    _bindings.Assets.Dats, out float datDefaultDensity, out float datEngagedDensity))
            {
                Console.WriteLine(
                    "[UI] options panel: Chat tab opacity DAT defaults didn't resolve "
                    + "(DID 0x78000001) - falling back to retail's ChatInterface base-"
                    + "constructor values (0.5/1.0)");
            }

            if (!Panels.CommsKnobsDatCaptions.TryRead(
                    _bindings.Assets.Dats, texts,
                    out Panels.CommsKnobsDatCaptions.Legend defaultDensityLegend,
                    out Panels.CommsKnobsDatCaptions.Legend engagedDensityLegend))
            {
                Console.WriteLine(
                    "[UI] options panel: Chat tab opacity slider captions didn't resolve "
                    + "(DID 0x78000000) - rows render with no caption rather than invented "
                    + "English.");
            }

            bool commsTied = Panels.CommsKnobsSheetDriver.Bind(
                arrangement,
                driver.CommsSheet,
                blueprintLocator: (blueprintArrangementIdent, blueprintElemIdent) =>
                {
                    var details = ArrangementLoader.ImportInfos(
                        _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent);
                    return details is null
                        ? null
                        : ArrangementLoader.Build(
                            details,
                            _bindings.Assets.ResolveSprite,
                            _bindings.Assets.DefaultFont,
                            _bindings.Assets.ResolveFont,
                            texts.Resolve,
                            blueprintArrangementIdent).Root;
                },
                locateString: (chartIdent, stringIdent) => texts.Resolve(chartIdent, stringIdent),
                new Panels.CommsKnobsSheetDriver.BindingsDef(
                    CurrentDefaultOpacity: () => PaneDensity.DefaultDensity,
                    CurrentActiveOpacity: () => PaneDensity.EngagedDensity,
                    SetDefaultOpacity: PaneDensity.AssignDefaultDensity,
                    SetActiveOpacity: PaneDensity.AssignEngagedDensity,
                    FlushOpacity: PersistCommsDensity,
                    DefaultOpacityDatDefault: datDefaultDensity,
                    ActiveOpacityDatDefault: datEngagedDensity,
                    DefaultOpacityCaption: defaultDensityLegend,
                    ActiveOpacityCaption: engagedDensityLegend,
                    CurrentFilter: _bindings.Chat.Windows.FetchSift,
                    SetFilter: (paneIdent, val) =>
                    {
                        _bindings.Chat.Windows.ApplySift(paneIdent, val);
                        PersistCommsPaneFilters();
                    }));
            if (!commsTied)
                Console.WriteLine("[UI] options panel: Chat tab rows didn't bind");
        }

        lock (_bindings.Assets.DatLock)
        {
            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);

            bool settingsTied = Panels.SettingsKnobsSheetDriver.Bind(
                arrangement,
                driver.SettingsSheet,
                blueprintLocator: (blueprintArrangementIdent, blueprintElemIdent) =>
                {
                    var details = ArrangementLoader.ImportInfos(
                        _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent);
                    return details is null
                        ? null
                        : ArrangementLoader.Build(
                            details,
                            _bindings.Assets.ResolveSprite,
                            _bindings.Assets.DefaultFont,
                            _bindings.Assets.ResolveFont,
                            texts.Resolve,
                            blueprintArrangementIdent).Root;
                },
                locateString: (chartIdent, stringIdent) => texts.Resolve(chartIdent, stringIdent),
                new Panels.SettingsKnobsSheetDriver.Bindings(
                    LoadDisplay: _bindings.Options.LoadDisplay,
                    SaveDisplay: _bindings.Options.SaveDisplay,
                    LoadAudio: _bindings.Options.LoadAudio,
                    SaveAudio: _bindings.Options.SaveAudio,
                    LoadCameraTurning: _bindings.Options.LoadCameraTurning,
                    SaveCameraTurning: _bindings.Options.SaveCameraTurning,
                    LoadChat: () => _bindings.Chat.Store?.LoadChat() ?? CommsPrefs.Default,
                    SaveChat: comms => _bindings.Chat.Store?.PersistChat(comms))
                {
                    RenderPacks = _bindings.Options.LoadRenderPackChoices is { } pull
                        ? new Panels.SettingsKnobsSheetDriver.RenderPackWiring(pull)
                        {
                            PullRev =
                                _bindings.Options.LoadRenderPackCatalogRevision,
                            PullMissNotice =
                                _bindings.Options.LoadRenderPackFailureNotice,
                        }
                        : null,
                },
                locateSprite: _bindings.Assets.ResolveSprite,
                datTypeface: _bindings.Assets.DefaultFont,
                diagTypeface: _bindings.Assets.DebugFont,
                onHandResolutions: Graphics.DisplayModeRegistry.WindowedResolutions,
                resolutionDefault: Graphics.DisplayModeRegistry.DesktopResolution);
            if (!settingsTied)
                Console.WriteLine("[UI] options panel: Config tab rows didn't bind");
        }

        driver.ActivateTabs();

        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            driver.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Options,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 150f,
                Top = 80f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.Options,
            PaneLabels.Options,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
        Console.WriteLine("[UI] retail Options panel from LayoutDesc importer (0x2100006E slot 0x1000018D)");
    }

    private void AttachKeyboardSettings()
    {
        var keyboard = _bindings.Keyboard;
        if (keyboard is null || keyboard.Dispatcher is null)
        {
            Console.WriteLine(
                "[UI] keyboard config: no InputRouter wired - Configure Keyboard "
                + "screen will not open (button stays inert)");
            return;
        }
        InputRouter router = keyboard.Dispatcher;

        ElemDetails? details;
        ImportedArrangement? arrangement;
        CanonActionMapFrame? capture;
        DatStringPicker texts;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(_bindings.Assets.Dats, Panels.KeyboardSettingsDriver.LayoutId);
            texts = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = details is null
                ? null
                : ArrangementLoader.Build(
                    details,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve);
            capture = CanonActionMapReader.Read(_bindings.Assets.Dats);
        }
        if (arrangement is null || capture is null)
        {
            Console.WriteLine(
                "[UI] keyboard config: LayoutDesc 0x21000009 or the DAT ActionMap "
                + "singleton (0x26000000) not found - Configure Keyboard will not open");
            return;
        }

        string unmappedTrail = UnmappedTagMappingsTrail(keyboard.KeyBindingsFilePath);
        CanonUnmappedKeyBindings unmapped = CanonUnmappedKeyBindings.PullOrVacant(unmappedTrail);
        CanonKeymapProfileStore keymaps = new CanonKeymapProfileStore(keyboard.KeyBindingsFilePath);

        string? LocateKeymapBlueprint(string tag, string fileLabel)
        {
            var variables = new Dictionary<uint, string>
            {
                [DatStringPicker.CalculateDigest("LABEL")] = fileLabel,
                [DatStringPicker.CalculateDigest("KEYMAP")] = fileLabel,
                [DatStringPicker.CalculateDigest("FILENAME")] = fileLabel,
                [DatStringPicker.CalculateDigest("NAME")] = fileLabel,
                [DatStringPicker.CalculateDigest("VALUE")] = fileLabel,
            };
            lock (_bindings.Assets.DatLock)
                return texts.LocateBlueprint(0x23000004u, tag, variables);
        }

        void RevealKeymapMsg(string? msg)
        {
            if (!string.IsNullOrWhiteSpace(msg) && DialogFactory is not null)
                DialogFactory.CraftMsg(msg, fifoTag: 0x10000001u, precedence: true);
        }

        void PersistMirrors()
        {
            router.Bindings.StoreToFile(keyboard.KeyBindingsFilePath);
            unmapped.PersistToFile(unmappedTrail);
        }

        void ProcessPersistOutcome(
            CanonKeymapSaveResult outcome,
            string askedLabel,
            Action onStored)
        {
            switch (outcome.Status)
            {
                case CanonKeymapSaveStatus.Saved:
                    try
                    {
                        PersistMirrors();
                    }
                    catch (Exception miss)
                    {
                        Console.WriteLine($"keyboard config: JSON mirror save failed: {miss.Message}");
                    }
                    onStored();
                    return;

                case CanonKeymapSaveStatus.Exists:
                    string? overwrite = LocateKeymapBlueprint(
                        "ID_KeyMapOverwriteKeymap_Label", outcome.FileName);
                    if (overwrite is null || DialogFactory is null) return;
                    DialogFactory.CraftAck(
                        overwrite,
                        blob =>
                        {
                            if (!blob.FetchBoolean(CanonPromptProperty.AckOutcome)) return;
                            ProcessPersistOutcome(
                                keymaps.Save(askedLabel, router.Bindings, overwrite: true),
                                askedLabel,
                                onStored);
                        },
                        fifoTag: 0x10000001u,
                        precedence: true);
                    return;

                case CanonKeymapSaveStatus.ReadOnly:
                    RevealKeymapMsg(LocateKeymapBlueprint(
                        "ID_KeyMapCantOverwriteReadOnlyKeymap_Label", outcome.FileName));
                    return;

                default:
                    Console.WriteLine(
                        $"keyboard config: keymap save failed ({outcome.Status}): {outcome.Error}");
                    return;
            }
        }

        var driver = Panels.KeyboardSettingsDriver.Bind(
            arrangement,
            capture,
            blueprintLocator: (blueprintArrangementIdent, blueprintElemIdent) =>
            {
                lock (_bindings.Assets.DatLock)
                {
                    var blueprintDetails = ArrangementLoader.ImportInfos(
                        _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent);
                    return blueprintDetails is null
                        ? null
                        : ArrangementLoader.Build(
                            blueprintDetails,
                            _bindings.Assets.ResolveSprite,
                            _bindings.Assets.DefaultFont,
                            _bindings.Assets.ResolveFont,
                            texts.Resolve,
                            blueprintArrangementIdent).Root;
                }
            },
            locateString: (chartIdent, stringIdent) => texts.Resolve(chartIdent, stringIdent),
            new Panels.KeyboardSettingsDriver.Bindings(
                CurrentForAction: act => router.Bindings.ForAct(act).ToArray(),
                SetForAction: (act, newMappings) =>
                {
                    var updated = ReplicateWithout(router.Bindings, act);
                    foreach (CockpitBinding binding in newMappings)
                        updated.Add(binding);
                    router.AssignMappings(updated);
                },
                CurrentForUnmapped: tag => unmapped.Get(tag.InputMapId, tag.ActionId),
                SetForUnmapped: (tag, chords) => unmapped.Set(tag.InputMapId, tag.ActionId, chords),
                BeginCapture: onOutcome => router.CommenceGrab(
                    chord => onOutcome(chord == default ? null : chord)),
                Save: () =>
                {
                    try
                    {
                        ProcessPersistOutcome(
                            keymaps.PersistEngaged(router.Bindings),
                            keymaps.LatestFileLabel,
                            static () => { });
                    }
                    catch (Exception miss)
                    {
                        Console.WriteLine($"keyboard config: save failed: {miss.Message}");
                    }
                },
                Toggle: () => FlipPane(PaneLabels.KeyboardSettings),
                ResolveTemplate: (tag, variables) =>
                {
                    lock (_bindings.Assets.DatLock)
                    {
                        return texts.LocateBlueprint(0x23000004u, tag, variables);
                    }
                },
                ShowMessage: msg =>
                {
                    if (DialogFactory is null) return;
                    DialogFactory.CraftMsg(
                        msg,
                        fifoTag: 0x10000001u,
                        precedence: true);
                },
                ConfirmOverwrite: (msg, onOutcome) =>
                {
                    if (DialogFactory is null) { onOutcome(false); return; }
                    DialogFactory.CraftAck(
                        msg,
                        blob => onOutcome(blob.FetchBoolean(CanonPromptProperty.AckOutcome)),
                        fifoTag: 0x10000001u,
                        precedence: true);
                },
                OpenCaptureInstructions: actCaption =>
                {
                    if (DialogFactory is null) return 0u;
                    string? phrase = texts.LocateBlueprint(
                        0x23000004u,
                        "ID_ActionKeyMap_MapInstructions",
                        new Dictionary<uint, string>
                        {
                            [DatStringPicker.CalculateDigest("ACTION")] = actCaption,
                        });
                    if (phrase is null) return 0u; // no invented English
                    try
                    {
                        return DialogFactory.CraftPause(
                            phrase,
                            fifoTag: 0x10000001u,
                            precedence: true);
                    }
                    catch (Exception miss)
                    {
                        Console.WriteLine(
                            "[UI] keyboard config: capture-instruction dialog could not "
                            + $"build - capture refused. {miss.Message}");
                        return 0u;
                    }
                },
                CloseCaptureInstructions: ctx =>
                    DialogFactory?.ShutPopup(ctx),
                CurrentKeymapFilename: () => keymaps.LatestFileLabel,
                OpenLoadKeymap: onFetched =>
                {
                    if (DialogFactory is null) return;
                    var files = keymaps.RosterFiles();
                    int chosen = files
                        .Select(static (label, ordinal) => (name: label, index: ordinal))
                        .FirstOrDefault(
                            duo => string.Equals(
                                duo.name,
                                keymaps.LatestFileLabel,
                                StringComparison.OrdinalIgnoreCase),
                            (name: string.Empty, index: 0)).index;
                    DialogFactory.CraftAckMenu(
                        files,
                        chosen,
                        blob =>
                        {
                            int choice = blob.FetchInt32(CanonPromptProperty.MenuPick, -1);
                            if (choice < 0 || choice >= files.Count) return;
                            if (!keymaps.TryPull(
                                    files[choice],
                                    router.Bindings,
                                    out KeyBindingBook fetched,
                                    out string? problem))
                            {
                                Console.WriteLine($"keyboard config: keymap load failed: {problem}");
                                return;
                            }
                            router.AssignMappings(fetched);
                            try { PersistMirrors(); }
                            catch (Exception miss)
                            {
                                Console.WriteLine(
                                    $"keyboard config: loaded profile JSON mirror failed: {miss.Message}");
                            }
                            onFetched();
                        },
                        fifoTag: 0x10000001u);
                },
                OpenSaveKeymap: onStored =>
                {
                    if (DialogFactory is null) return;
                    DialogFactory.CraftAckPhraseFeed(
                        string.Empty,
                        blob =>
                        {
                            string moniker = blob.FetchString(CanonPromptProperty.PhraseFeedOutcome)
                                ?? string.Empty;
                            if (moniker.Length is 0) return;
                            ProcessPersistOutcome(
                                keymaps.Save(moniker, router.Bindings, overwrite: false),
                                moniker,
                                onStored);
                        },
                        fifoTag: 0x10000001u);
                }),
            locateBlueprintTypeface: (blueprintArrangementIdent, blueprintElemIdent) =>
            {
                lock (_bindings.Assets.DatLock)
                {
                    var blueprintDetails = ArrangementLoader.ImportInfos(
                        _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent);
                    return blueprintDetails is null || blueprintDetails.FontDid is 0u
                        ? null
                        : _bindings.Assets.ResolveFont(blueprintDetails.FontDid);
                }
            });

        if (driver is null)
        {
            Console.WriteLine("[UI] keyboard config: needed window root didn't build");
            return;
        }

        KeyboardConfigController = driver;
        WidgetElem trunk = arrangement.Root;
        CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.KeyboardSettings,
                Chrome = CanonWindowChrome.Imported,
                Left = Math.Max(0f, (Host.Root.Width - trunk.Width) * 0.5f),
                Top = Math.Max(0f, (Host.Root.Height - trunk.Height) * 0.5f),
                Visible = false,
                DatConstraintSrc = details,
                SubstancePressThrough = false,
            });
        Console.WriteLine("[UI] retail Configure Keyboard screen from gmKeyboardUI LayoutDesc 0x21000009");
    }

    private void AttachLeapPowerbar()
    {
        ElemDetails? details;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            details = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, JumpPowerbarDriver.LayoutId);
            DatStringPicker powerbarTexts = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = details is null ? null : ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont,
                powerbarTexts.Resolve);
        }
        if (arrangement is null)
        {
            Console.WriteLine("[UI] jump powerbar: LayoutDesc 0x21000072 not found");
            return;
        }

        JumpPowerbarDriver? driver = Panels.JumpPowerbarDriver.Bind(
            arrangement,
            _bindings.JumpPowerbar.Snapshot,
            shown =>
            {
                if (shown) Host.RevealPane(PaneLabels.LeapPowerbar);
                else Host.ConcealPane(PaneLabels.LeapPowerbar);
            });
        if (driver is null)
        {
            Console.WriteLine("[UI] jump powerbar: needed JumpMode meter absent in LayoutDesc 0x21000072");
            return;
        }

        LeapPowerbarDriver = driver;
        WidgetElem trunk = arrangement.Root;
        CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.LeapPowerbar,
                Chrome = CanonWindowChrome.Imported,
                Left = Math.Max(0f, (Host.Root.Width - trunk.Width) * 0.5f),
                Top = Math.Max(0f, Host.Root.Height - trunk.Height - 105f),
                Visible = false,
                DatConstraintSrc = details,
                Resizable = true,
                RescaleX = true,
                RescaleY = true,
                ConstrainPullToAncestor = true,
                SubstancePressThrough = false,
                Controller = driver,
            });
        driver.AlignVis();
        Console.WriteLine("[UI] retail jump bar from gmFloatyPowerBarUI LayoutDesc 0x21000072");
    }

    private void AttachSocialBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                Panels.SocialBoardDriver.HostArrangementId,
                Panels.SocialBoardDriver.SlotElemId);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
        }
        if (trunkDetails is null || arrangement is null)
        {
            Console.WriteLine("[UI] social panel: LayoutDesc 0x2100006E slot 0x1000018F not found");
            return;
        }

        RowTemplatePicker rankBlueprints = new Panels.RowTemplatePicker(
            (arrangementIdent, elemIdent) => ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, arrangementIdent, elemIdent),
            details =>
            {
                DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
                return ArrangementLoader.Build(
                    details,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve).Root;
            });
        WidgetElem? BlueprintLocator(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
                return rankBlueprints.Resolve(blueprintArrangementIdent, blueprintElemIdent);
        }

        DatStringPicker fellowshipTexts = new DatStringPicker(_bindings.Assets.Dats);

        var hooks = new Panels.SocialBoardDriver.Callbacks(
            Toggle: () => FlipPane(PaneLabels.SocialPanel),
            Fellowship: new Panels.SocialFellowshipSheetDriver.Bindings(
                Snapshot: _bindings.Social.FellowshipSnapshot,
                Members: _bindings.Social.FellowshipMembers,
                TemplateResolver: BlueprintLocator,
                Create: _bindings.Social.FellowshipCreate,
                Recruit: _bindings.Social.FellowshipRecruit,
                Dismiss: _bindings.Social.FellowshipDismiss,
                Quit: _bindings.Social.FellowshipQuit,
                AssignLeader: _bindings.Social.FellowshipAssignLeader,
                SetOpen: _bindings.Social.FellowshipSetOpen,
                SetPanelOpen: _bindings.Social.FellowshipSetPanelOpen,
                Selection: _bindings.Social.Selection,
                LocalPlayerGuid: _bindings.Social.LocalPlayerGuid,
                CurrentCharacterOption: ident => _bindings.Options.CurrentCharacterOption((uint)ident),
                SetCharacterOption: (ident, val) => _bindings.Options.CommandBus().Publish(
                    new SetSingleToonKnobEngineCmd((uint)ident, val)),
                ResolveString: (chartIdent, stringIdent) => fellowshipTexts.Resolve(chartIdent, stringIdent)),
            Allegiance: new Panels.SocialAllegianceSheetDriver.Bindings(
                Snapshot: _bindings.Social.AllegianceSnapshot,
                Monarch: _bindings.Social.AllegianceMonarch,
                Patron: _bindings.Social.AllegiancePatron,
                Member: _bindings.Social.AllegianceMember,
                Vassals: _bindings.Social.AllegianceVassals,
                Swear: _bindings.Social.AllegianceSwear,
                Break: _bindings.Social.AllegianceBreak,
                Kick: _bindings.Social.AllegianceKick,
                SetUpdateSubscription: _bindings.Social.AllegianceSetUpdateSubscription,
                Selection: _bindings.Social.Selection,
                LocalPlayerGuid: _bindings.Social.LocalPlayerGuid,
                CurrentCharacterOption: ident => _bindings.Options.CurrentCharacterOption((uint)ident),
                SetCharacterOption: (ident, val) => _bindings.Options.CommandBus().Publish(
                    new SetSingleToonKnobEngineCmd((uint)ident, val)),
                TemplateResolver: BlueprintLocator,
                ResolveString: (chartIdent, stringIdent) => fellowshipTexts.Resolve(chartIdent, stringIdent),
                ResolveWorldObjectName: oid => _bindings.Inventory.Objects.Get(oid)?.FetchAppropriateLabel(),
                ShowConfirmation: RevealAck,
                ResolvePlayerTemplate: (tag, avatarLabel) =>
                {
                    lock (_bindings.Assets.DatLock)
                    {
                        return fellowshipTexts.LocateBlueprint(
                            0x23000001u,
                            tag,
                            new Dictionary<uint, string>
                            {
                                [Panels.DatStringPicker.AvatarVariable] = avatarLabel,
                            });
                    }
                }),
            Friends: _bindings.Social.Friends,
            Squelch: _bindings.Social.Squelch,
            TemplateResolver: BlueprintLocator,
            FriendsActions: new Panels.SocialFriendsSheetDriver.Acts(
                AddFriend: label => _bindings.Options.CommandBus().Publish(
                    new AddBuddyEngineCmd(label)),
                RemoveFriend: oid => _bindings.Options.CommandBus().Publish(
                    new RemoveBuddyEngineCmd(oid)),
                CurrentAppearOffline: () => _bindings.Options.CurrentCharacterOption(
                    (uint)CharacterOptionId.AppearOffline),
                SetAppearOffline: val => _bindings.Options.CommandBus().Publish(
                    new SetSingleToonKnobEngineCmd(
                        (uint)CharacterOptionId.AppearOffline, val))),
            SquelchActions: new Panels.SocialSquelchSheetDriver.ClientActions(
                SquelchCharacter: label => _bindings.Options.CommandBus().Publish(
                    new ModifyToonMuteEngineCmd(true, 0u, label, 1u)),
                SquelchAccount: label => _bindings.Options.CommandBus().Publish(
                    new ModifyAccountMuteEngineCmd(true, label)),
                RemoveCharacterSquelch: (oid, label) => _bindings.Options.CommandBus().Publish(
                    new ModifyToonMuteEngineCmd(false, oid, label, 1u)),
                RemoveAccountSquelch: label => _bindings.Options.CommandBus().Publish(
                    new ModifyAccountMuteEngineCmd(false, label))));

        Panels.SocialBoardDriver? driver;
        lock (_bindings.Assets.DatLock)
            driver = Panels.SocialBoardDriver.Bind(arrangement, hooks);
        if (driver is null)
        {
            Console.WriteLine("[UI] social panel: needed root didn't build as UiTabPanel");
            return;
        }

        driver.ActivateTabs();
        SocialPanelController = driver;

        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            driver.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.SocialPanel,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 200f,
                Top = 140f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.SocialBoard,
            PaneLabels.SocialPanel,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
        Console.WriteLine("[UI] retail social panel from LayoutDesc importer (0x2100006E slot 0x1000018F)");
    }

    private void AttachJournalBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                Panels.JournalBoardDriver.HubArrangementIdent,
                Panels.JournalBoardDriver.SocketElemIdent);
            DatStringPicker locator = new DatStringPicker(_bindings.Assets.Dats);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    locator.Resolve);
        }
        if (trunkDetails is null || arrangement is null)
        {
            Console.WriteLine(
                "[UI] journal panel: LayoutDesc 0x2100006E slot 0x10000559 not found");
            return;
        }

        RowTemplatePicker rankBlueprints = new Panels.RowTemplatePicker(
            (arrangementIdent, elemIdent) => ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, arrangementIdent, elemIdent),
            details =>
            {
                DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
                return ArrangementLoader.Build(
                    details,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve).Root;
            });

        var hooks = new Panels.JournalBoardDriver.ClientCallbacks(
            Toggle: () => FlipPane(PaneLabels.Journal),
            Contracts: new Panels.DiaryContractsPageDriver.Bindings(
                Contracts: _bindings.Quests.Contracts,
                Catalog: _bindings.Quests.Catalog,
                Now: () => DateTime.UtcNow,
                TemplateResolver: (blueprintArrangementIdent, blueprintElemIdent) =>
                {
                    lock (_bindings.Assets.DatLock)
                        return rankBlueprints.Resolve(blueprintArrangementIdent, blueprintElemIdent);
                },
                Abandon: _bindings.Quests.AbandonContract),
            Notes: new Panels.DiaryNotesPageDriver.Bindings(
                Journal: _bindings.Quests.Journal,
                Commands: _bindings.Quests.JournalCommands,
                PlayerCell: _bindings.Quests.PlayerCell,
                Now: () => DateTime.UtcNow),
            SaveJournal: PersistJournal,
            PageList: openSheet => new Panels.DiaryPageListDriver.Bindings(
                Journal: _bindings.Quests.Journal,
                Commands: _bindings.Quests.JournalCommands,
                OpenPage: openSheet,
                TemplateResolver: (blueprintArrangementIdent, blueprintElemIdent) =>
                {
                    lock (_bindings.Assets.DatLock)
                        return rankBlueprints.Resolve(blueprintArrangementIdent, blueprintElemIdent);
                },
                Now: () => DateTime.UtcNow));

        Panels.JournalBoardDriver? driver;
        lock (_bindings.Assets.DatLock)
            driver = Panels.JournalBoardDriver.Bind(arrangement, hooks);
        if (driver is null)
        {
            Console.WriteLine("[UI] journal panel: needed root didn't build as UiTabPanel");
            return;
        }

        driver.EngageTabs();
        JournalPanelController = driver;

        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            driver.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Journal,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 230f,
                Top = 160f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.Journal,
            PaneLabels.Journal,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
        Console.WriteLine(
            "[UI] retail journal panel from LayoutDesc importer (0x2100006E slot 0x10000559)");
    }

    private void AttachLookupHouseBoard()
    {
        ElemDetails? trunkDetails;
        ImportedArrangement? arrangement;
        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        lock (_bindings.Assets.DatLock)
        {
            trunkDetails = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats,
                Panels.LookupHouseBoardDriver.HubArrangementTag,
                Panels.LookupHouseBoardDriver.SocketElemTag);
            arrangement = trunkDetails is null
                ? null
                : ArrangementLoader.Build(
                    trunkDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve);
        }
        if (trunkDetails is null || arrangement is null)
        {
            Console.WriteLine("[UI] Map/House panel: LayoutDesc 0x2100006E slot 0x1000018C not found");
            return;
        }

        RowTemplatePicker hotspotBlueprint = new Panels.RowTemplatePicker(
            (blueprintArrangementIdent, blueprintElemIdent) => ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, blueprintArrangementIdent, blueprintElemIdent),
            details => ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont).Root);
        WidgetElem? LocateHotspotBlueprint(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
                return hotspotBlueprint.Resolve(blueprintArrangementIdent, blueprintElemIdent);
        }
        ElemDetails? LocateHotspotBlueprintDetails(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
                return hotspotBlueprint.LocateDetails(blueprintArrangementIdent, blueprintElemIdent);
        }

        WidgetElem? AssembleSwallowedGlyph(ElemDetails glyphDetails)
        {
            lock (_bindings.Assets.DatLock)
                return ArrangementLoader.Build(
                    glyphDetails,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont).Root;
        }

        var wiring = _bindings.MapHouse;
        var hooks = new Panels.LookupHouseBoardDriver.CallbacksDef(
            Toggle: () => FlipPane(PaneLabels.MapHouse),
            Map: new Panels.MapPageDriver.Bindings(
                CurrentCalendar: wiring.CurrentCalendar,
                PlayerCellId: wiring.PlayerCellId,
                HousePosition: wiring.HousePosition ?? (static () => null),
                TemplateResolver: LocateHotspotBlueprint,
                IconBuilder: AssembleSwallowedGlyph,
                TemplateInfoResolver: LocateHotspotBlueprintDetails),
            House: new Panels.DwellingPageDriver.Bindings(
                Lines: wiring.HouseLines ?? (static () => Array.Empty<string>()),
                OnShown: wiring.HouseShown,
                TemplateResolver: LocateHotspotBlueprint,
                PanelLines: wiring.HousePanelLines));

        Panels.LookupHouseBoardDriver? driver;
        lock (_bindings.Assets.DatLock)
            driver = Panels.LookupHouseBoardDriver.Bind(trunkDetails, arrangement, hooks);
        if (driver is null)
        {
            Console.WriteLine("[UI] Map/House panel: needed root didn't build as UiTabPanel");
            return;
        }

        driver.ArmTabs();
        MapHousePanelController = driver;

        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            driver.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.MapHouse,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 240f,
                Top = 160f,
                Visible = false,
                RescaleX = false,
                RescaleY = false,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.LookupHouse,
            PaneLabels.MapHouse,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
        Console.WriteLine("[UI] retail Map/House panel from LayoutDesc importer (0x2100006E slot 0x1000018C)");
    }

    private void AttachPopupMaker()
    {
        if (DialogFactory is not null)
            return;

        uint arrangementIdent;
        try
        {
            lock (_bindings.Assets.DatLock)
            {
                arrangementIdent = CanonDataIdResolver.Resolve(
                    _bindings.Assets.Dats,
                    2u,
                    5u);
            }
        }
        catch (Exception problem)
        {
            Console.WriteLine(
                "[UI] retail dialog catalog will retry after resource "
                + $"recovery: {problem.Message}");
            return;
        }

        if (arrangementIdent is 0u)
        {
            Console.WriteLine("[UI] retail dialog catalog could not be resolved");
            return;
        }

        ImportedArrangement? BuildArrangement(CanonPromptType kind)
        {
            uint trunkElemIdent = CanonPromptMint.RootElementIdent(kind);
            if (trunkElemIdent is 0u)
                return null;
            lock (_bindings.Assets.DatLock)
            {
                return ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementIdent,
                    trunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
            }
        }

        DialogFactory = new CanonPromptMint(Host.Root, BuildArrangement);
        DatStringPicker ackTexts = new DatStringPicker(_bindings.Assets.Dats);
        string? ConstructAck(uint kind, string bareLabel)
        {
            string? tag = kind switch
            {
                1u => "ID_Allegiance_AcceptSwearConfirmation",
                4u => "ID_Fellowship_FellowshipRequest",
                _ => null,
            };
            if (tag is null)
                return null;
            lock (_bindings.Assets.DatLock)
            {
                return ackTexts.LocateBlueprint(
                    0x23000001u,
                    tag,
                    new Dictionary<uint, string>
                    {
                        [DatStringPicker.AvatarVariable] = bareLabel,
                    });
            }
        }
        _gameplayAckDriver = new GameplayConfirmationDriver(
            DialogFactory,
            _bindings.Confirmations.SendResponse,
            ConstructAck);
        _gearAckDriver = new CanonGearConfirmationDriver(
            DialogFactory,
            GearDealing);
        _aptitudeTrainingAckDriver =
            new CanonSkillTrainingConfirmationDriver(DialogFactory);
        Console.WriteLine(
            $"[UI] retail DialogFactory from LayoutDesc 0x{arrangementIdent:X8}; confirmation root 0x15");
    }

    private void AttachHintPresenter()
    {
        if (HintPresenter is not null)
            return;

        ImportedArrangement? BuildHintArrangement(uint arrangementDid, uint trunkElemIdent)
        {
            lock (_bindings.Assets.DatLock)
            {
                return ArrangementLoader.Import(
                    _bindings.Assets.Dats,
                    arrangementDid,
                    trunkElemIdent,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont);
            }
        }

        HintPresenter = new CanonTooltipExhibitor(Host.Root, BuildHintArrangement)
        {
            RealmHoverOidSupplier = () => _bindings.WorldTooltip.HoverGuidAtCursor(),
            RealmHoverLabelLocator = _bindings.WorldTooltip.ResolveName,
            RealmHintsTurnedOn = () => _bindings.WorldTooltip.Enabled(),
        };

        if (_bindings.Chat.Store is { } vault)
        {
            var misc = vault.PullMisc();
            HintPresenter.Enabled = misc.TooltipEnable;
            Host.Root.HintDelayMsec = (int)(misc.TooltipDelaySeconds * 1000f);
        }
    }

    private void AttachToon()
    {
        if (ImportOrReport(0x2100002Eu, "[UI] character: LayoutDesc 0x2100002E not found")
                is not { } arrangement)
        {
            return;
        }
        var supplier = _bindings.Character.Provider;
        ToonSheet latestSheet = supplier.AssembleSheet();
        uint GlyphDidLocate(uint enumVal, uint bucket)
        {
            lock (_bindings.Assets.DatLock)
                return CanonDataIdResolver.Resolve(_bindings.Assets.Dats, enumVal, bucket);
        }
        _toonStatMapping = ToonStatDriver.Bind(
            arrangement,
            () => latestSheet,
            _bindings.Assets.DefaultFont,
            _bindings.Assets.ResolveFont(0x40000001u) ?? _bindings.Assets.DefaultFont,
            _bindings.Assets.ResolveSprite,
            (req, finished) => ProcessToonEmit(supplier, req, finished),
            () => ShutPane(PaneLabels.Character),
            GlyphDidLocate);
        _toonSheetSubscription = supplier.EnlistAltered(() =>
        {
            latestSheet = supplier.AssembleSheet();
            _toonStatMapping?.Refresh();
        });

        RowTemplatePicker bannerRankBlueprints = new Panels.RowTemplatePicker(
            (arrangementIdent, elemIdent) => ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, arrangementIdent, elemIdent),
            details =>
            {
                DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
                return ArrangementLoader.Build(
                    details,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.DefaultFont,
                    _bindings.Assets.ResolveFont,
                    texts.Resolve).Root;
            });
        WidgetElem? BannerBlueprintLocator(uint blueprintArrangementIdent, uint blueprintElemIdent)
        {
            lock (_bindings.Assets.DatLock)
                return bannerRankBlueprints.Resolve(blueprintArrangementIdent, blueprintElemIdent);
        }
        string? BannerLocator(uint bannerIdent)
        {
            lock (_bindings.Assets.DatLock)
                return _bindings.Character.TitleResolver.Resolve(bannerIdent);
        }
        _toonBannersDriver = Panels.ToonBannersDriver.Bind(
            arrangement.Root,
            _bindings.Character.Titles,
            BannerLocator,
            BannerBlueprintLocator,
            _bindings.Character.SendSetTitle);

        ElemDetails? hubConstraint;
        lock (_bindings.Assets.DatLock)
            hubConstraint = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, 0x2100006Eu, 0x100005FEu);

        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            arrangement.Root,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Character,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 540f,
                Top = 18f,
                SubstanceHeight = 362f,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainRescaleToAncestor = true,
                DatConstraintSrc = hubConstraint,
                DatConstraintSrcIsOuterCycle = true,
                Visible = false,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom,
                SubstancePressThrough = false,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.Character,
            PaneLabels.Character,
            hnd);
        Console.WriteLine("[UI] retail character window from LayoutDesc importer (0x2100002E)");
    }

    private void AttachExtensions()
    {
        if (_bindings.Plugins is null) return;

        IMarkupIconPicker glyphLocator = new CanonMarkupIconPicker(
            _bindings.Assets.Dats,
            _bindings.Assets.Icons,
            _bindings.Toolbar.Objects);

        foreach (var board in _bindings.Plugins.Drain())
        {
            try
            {
                string xml = board.MarkupSubstance
                    ?? File.ReadAllText(board.MarkupPath);
                var elem = MarkupDoc.Build(
                    xml,
                    board.Binding,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.Controls,
                    _bindings.Assets.DefaultFont,
                    glyphLocator);

                if (Host.PaneKeeper.TryGet(board.PaneLabel, out _))
                {
                    throw new InvalidOperationException(
                        $"Plugin window '{board.PaneLabel}' is by now registered. "
                        + "Window ids has to be unique within one plugin");
                }

                Func<bool>? readiness = elem.ShownSrc;
                var vis = new PluginWindowVisibilityDriver(
                    readiness,
                    board.Descriptor.BeginShown);
                elem.ShownSrc = vis.ShouldBeShown;
                elem.Visible = vis.ShouldBeShown();

                Host.Root.AddChild(elem);
                _bindings.Plugins.ConcludeMount(board, Host.Root, elem);
                int authoredGeoRev = CanonWindowKeeper.CalculateAuthoredGeoRev(
                    elem.Width, elem.Height, elem.MinWidth, elem.MinHeight, elem.Resizable);
                var hnd = Host.PaneKeeper.Register(
                    board.PaneLabel,
                    elem,
                    elem,
                    vis,
                    authoredGeoRev: authoredGeoRev);
                _bindings.Plugins.ConcludePaneMount(
                    board,
                    () => Host.PaneKeeper.Unregister(board.PaneLabel));

                if (board.Descriptor.ShelfButtonVisible)
                {
                    if (_extensionFlankBoard is null)
                    {
                        _extensionFlankBoard = new PluginSidePane(
                            Host.PaneKeeper,
                            glyphLocator.LocateDid,
                            _bindings.Assets.DefaultFont);
                        Host.Root.AddChild(_extensionFlankBoard);
                        Host.PaneKeeper.Register(
                            PaneLabels.ExtensionShelf,
                            _extensionFlankBoard,
                            _extensionFlankBoard,
                            driver: _extensionFlankBoard);
                    }
                    _extensionFlankBoard.Add(board.Owner, board.Descriptor, hnd);
                }

                Console.WriteLine(
                    $"[UI] plugin UI window loaded: {board.PaneLabel} "
                    + $"({board.MarkupPath})");
            }
            catch (Exception exc)
            {
                _bindings.Plugins.FailMount(board);
                Console.WriteLine($"[UI] plugin UI panel '{board.MarkupPath}' could not load: {exc.Message}");
            }
        }
    }

    private void AttachSatchel()
    {
        if (ImportOrReport(0x21000023u, "[UI] inventory: LayoutDesc 0x21000023 not found")
                is not { } arrangement)
        {
            return;
        }

        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root, trunk, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Inventory,
                Chrome = CanonWindowChrome.NineSlice,
                Left = trunk.Left,
                Top = trunk.Top,
                SubstanceWidth = trunk.Width,
                SubstanceHeight = trunk.Height,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top | MooringRims.Bottom,
            });
        _boardWidget.EnrollPrimaryBoard(
            CanonPaneRegistry.Inventory,
            PaneLabels.Inventory,
            hnd);

        uint insides, flankBag, primaryBundle;
        IReadOnlyDictionary<uint, uint> paperdollVacantSprites;
        EffigyClickMap? paperdollPressLookup;
        lock (_bindings.Assets.DatLock)
        {
            insides = GearListCellTemplate.ResolveEmptySprite(_bindings.Assets.Dats, 0x21000021u, 0x100001C6u);
            flankBag = GearListCellTemplate.ResolveEmptySprite(_bindings.Assets.Dats, 0x21000022u, 0x100001CAu);
            primaryBundle = GearListCellTemplate.ResolveEmptySprite(_bindings.Assets.Dats, 0x21000022u, 0x100001C9u);
            paperdollVacantSprites = EffigySlotBackgrounds.LocateVacantSprites(_bindings.Assets.Dats);
            paperdollPressLookup = EffigyClickMap.Load(_bindings.Assets.Dats);
        }

        var wiring = _bindings.Inventory;
        Action<uint, uint>? alertCombineAttempt = ToolbarDriver is null
            ? null
            : ToolbarDriver.ReplaceFullyMergedShortcut;
        StashDriver satchel = StashDriver.Bind(
            arrangement, wiring.Objects, wiring.PlayerGuid, wiring.ResolveIcon, wiring.Strength, wiring.Selection,
            _bindings.Assets.DefaultFont, _bindings.Character.Provider.ToonLabel,
            insides, flankBag, primaryBundle, wiring.SendUse,
            wiring.SendPutItemInContainer, wiring.SendStackableSplitToContainer, wiring.SendStackableMerge,
            alertCombineAttempt, wiring.ItemInteraction,
            () => ShutPane(PaneLabels.Inventory),
            PileDivideQty,
            wiring.ResolveDragIcon,
            wiring.Spellbook);
        SatchelBoardDriver = satchel;
        EffigyDriver paperdoll = EffigyDriver.Bind(
            arrangement, wiring.Objects, wiring.PlayerGuid, wiring.ResolveIcon, wiring.Selection, wiring.ItemInteraction,
            insides, _bindings.Assets.DefaultFont, paperdollPressLookup,
            wiring.ResolveDragIcon, paperdollVacantSprites);
        Host.PaneKeeper.FastenDriver(
            PaneLabels.Inventory,
            new RetainedPaneDriverGroup(satchel, paperdoll));

        PaperdollViewportWidget = arrangement.SeekElem(0x100001D5u) as WidgetViewport;
        InventoryFrame = (WidgetNineSlicePane)hnd.OuterCycle;
        Console.WriteLine("[UI] retail inventory window from LayoutDesc importer (0x21000023)");
    }

    private void AttachExternalVessel()
    {
        ImportedArrangement? arrangement;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats,
                ExternalContainerDriver.LayoutId,
                ExternalContainerDriver.TrunkId,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
        }
        if (arrangement is null)
        {
            Console.WriteLine("[UI] external container: LayoutDesc 0x21000008 not found");
            return;
        }

        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            ExternalContainerDriver.BuildPaneKnobs(trunk));

        uint insidesVacant;
        uint vesselVacant;
        lock (_bindings.Assets.DatLock)
        {
            insidesVacant = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                ExternalContainerDriver.LayoutId,
                ExternalContainerDriver.InsidesRosterIdent);
            vesselVacant = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                ExternalContainerDriver.LayoutId,
                ExternalContainerDriver.VesselRosterIdent);
        }

        var wiring = _bindings.ExternalContainer;
        ExternalVesselDriver = ExternalContainerDriver.Bind(
            arrangement,
            wiring.State,
            wiring.Objects,
            wiring.Selection,
            wiring.ItemInteraction,
            PileDivideQty,
            wiring.ResolveIcon,
            wiring.ResolveDragIcon,
            wiring.SendUse,
            wiring.SendPutItemInContainer,
            wiring.SendStackableSplitToContainer,
            wiring.IsWithinUseRange,
            hnd,
            insidesVacant,
            vesselVacant);
        Host.PaneKeeper.FastenDriver(
            PaneLabels.ExternalVessel,
            ExternalVesselDriver);
        Console.WriteLine(
            "[UI] retail external-container strip mounted from LayoutDesc 0x21000008");
    }

    private void AttachMerchant()
    {
        ImportedArrangement? arrangement;
        uint vacantSocketSprite;
        uint buyingVacantSocketSprite;
        uint sellingVacantSocketSprite;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats,
                MerchantWidgetDriver.LayoutId,
                MerchantWidgetDriver.RootId,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            vacantSocketSprite = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                MerchantWidgetDriver.LayoutId,
                MerchantWidgetDriver.GearRosterIdent);
            buyingVacantSocketSprite = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                MerchantWidgetDriver.LayoutId,
                MerchantWidgetDriver.BuyingRosterIdent);
            sellingVacantSocketSprite = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                MerchantWidgetDriver.LayoutId,
                MerchantWidgetDriver.SellingRosterIdent);
        }
        if (arrangement is null)
        {
            Console.WriteLine("[UI] vendor: LayoutDesc 0x21000012 root 0x100000B7 not found");
            return;
        }

        WidgetElem trunk = arrangement.Root;
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Vendor,
                Chrome = CanonWindowChrome.NineSlice,
                Left = trunk.Left,
                Top = trunk.Top,
                SubstanceWidth = trunk.Width,
                SubstanceHeight = trunk.Height,
                MinWidth = trunk.Width,
                MinHeight = trunk.Height,
                Visible = false,
                RescaleX = false,
                RescaleY = false,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                PaintChromeMiddle = false,
            });

        var wiring = _bindings.Vendor;
        MerchantDriver = MerchantWidgetDriver.Bind(
            arrangement,
            wiring.State,
            hnd,
            wiring.ResolveIcon,
            _bindings.Inventory.Objects,
            _bindings.Inventory.PlayerGuid,
            wiring.ItemInteraction,
            wiring.Selection,
            PileDivideQty,
            _bindings.Assets.DefaultFont,
            _bindings.Assets.DebugFont,
            _bindings.Assets.ResolveSprite,
            vacantSocketSprite,
            buyingVacantSocketSprite,
            sellingVacantSocketSprite,
            DialogFactory,
            wiring.DisplaySystemMessage);
        if (MerchantDriver is null)
        {
            Console.WriteLine("[UI] vendor: needed authored controls are absent");
            return;
        }

        Host.PaneKeeper.FastenDriver(PaneLabels.Vendor, MerchantDriver);
        Console.WriteLine("[UI] retail vendor browse panel mounted from LayoutDesc 0x21000012");
    }

    private void AttachSalvage()
    {
        ImportedArrangement? arrangement;
        uint vacantSocket;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats, SalvageWidgetDriver.LayoutId, SalvageWidgetDriver.RootId,
                _bindings.Assets.ResolveSprite, _bindings.Assets.DefaultFont, _bindings.Assets.ResolveFont);
            vacantSocket = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats, SalvageWidgetDriver.LayoutId, SalvageWidgetDriver.GearRosterTag);
        }
        if (arrangement is null)
        {
            Console.WriteLine("[UI] salvage window layout is not available");
            return;
        }
        var satchel = _bindings.Inventory;
        SalvageWidgetDriver? driver = SalvageWidgetDriver.Bind(arrangement,
            new SalvageWidgetDriver.Bindings(
                satchel.Objects,
                satchel.ItemInteraction.IsPossessedByAvatar,
                satchel.ItemInteraction.TrySalvageGearList,
                () => _bindings.Options.CurrentCharacterOption((uint)CharacterOptionId.SalvageMultiple),
                satchel.ResolveIcon,
                shown =>
                {
                    if (shown) Host.RevealPane(PaneLabels.Salvage);
                    else Host.ConcealPane(PaneLabels.Salvage);
                },
                _bindings.Options.DisplaySystemMessage,
                vacantSocket));
        if (driver is null)
        {
            Console.WriteLine("[UI] salvage window controls are not available");
            return;
        }
        WidgetElem trunk = arrangement.Root;
        const float cycleInset = 2f * CanonChromeSprites.Border;
        float width = MathF.Min(trunk.Width, MathF.Max(240f, Host.Root.Width - cycleInset));
        CanonPaneCycle.Mount(Host.Root, trunk, _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.Salvage,
                Chrome = CanonWindowChrome.NineSlice,
                Left = MathF.Max(0f, (Host.Root.Width - width - cycleInset) * 0.5f),
                Top = MathF.Max(0f, (Host.Root.Height - trunk.Height - cycleInset) * 0.5f),
                SubstanceWidth = width,
                SubstanceHeight = trunk.Height,
                MinWidth = 240f,
                MinHeight = trunk.Height,
                Visible = false,
                RescaleX = true,
                RescaleY = false,
                ResizableRims = RescaleRims.Left | RescaleRims.Right,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                Controller = driver,
            });
        SalvageDriver = driver;
        satchel.ItemInteraction.PolicyActionRequested += driver.ProcessRuleAct;
    }

    private void AttachSecureBarter()
    {
        if (_bindings.Social.Trade is not { } barterLens)
        {
            Console.WriteLine("[UI] secure trade: no runtime trade view bound");
            return;
        }

        ImportedArrangement? arrangement;
        uint selfVacantSocketSprite;
        uint partnerVacantSocketSprite;
        lock (_bindings.Assets.DatLock)
        {
            arrangement = ArrangementLoader.Import(
                _bindings.Assets.Dats,
                Panels.SecureBarterWidgetDriver.LayoutId,
                Panels.SecureBarterWidgetDriver.RootId,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
            selfVacantSocketSprite = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                Panels.SecureBarterWidgetDriver.LayoutId,
                Panels.SecureBarterWidgetDriver.SelfRosterIdent);
            partnerVacantSocketSprite = GearListCellTemplate.ResolveEmptySprite(
                _bindings.Assets.Dats,
                Panels.SecureBarterWidgetDriver.LayoutId,
                Panels.SecureBarterWidgetDriver.PartnerRosterIdent);
        }
        if (arrangement is null)
        {
            Console.WriteLine(
                "[UI] secure trade: LayoutDesc 0x2100000D root 0x1000007A not found");
            return;
        }

        var driver =
            Panels.SecureBarterWidgetDriver.Bind(
                arrangement,
                new Panels.SecureBarterWidgetDriver.Bindings(
                    Trade: barterLens,
                    Objects: _bindings.Inventory.Objects,
                    ResolveIcon: _bindings.Inventory.ResolveIcon,
                    OpenTrade: partner => _bindings.Options.CommandBus().Publish(
                        new OpenBarterNegotiationsEngineCmd(partner)),
                    CloseTrade: () => _bindings.Options.CommandBus().Publish(
                        new CloseBarterNegotiationsEngineCmd()),
                    AddToTrade: gear => _bindings.Options.CommandBus().Publish(
                        new AddToBarterEngineCmd(gear)),
                    AcceptTrade: (selfApproved, partnerApproved, partner) =>
                        _bindings.Options.CommandBus().Publish(new AcceptBarterEngineCmd(
                            partner, selfApproved, partnerApproved)),
                    DeclineTrade: () => _bindings.Options.CommandBus().Publish(
                        new DeclineBarterEngineCmd()),
                    ResetTrade: () => _bindings.Options.CommandBus().Publish(
                        new ResetBarterEngineCmd()),
                    SetWindowVisible: shown =>
                    {
                        if (shown) Host.RevealPane(PaneLabels.SecureBarter);
                        else Host.ConcealPane(PaneLabels.SecureBarter);
                    },
                    SelfEmptySlotSprite: selfVacantSocketSprite,
                    PartnerEmptySlotSprite: partnerVacantSocketSprite,
                    FormatTotalItems: tally =>
                    {
                        lock (_bindings.Assets.DatLock)
                        {
                            DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
                            return texts.LocateBlueprint(
                                0x23000001u,
                                "ID_SecureTrade_TotalItemsLabel",
                                new Dictionary<uint, string>
                                {
                                    [DatStringPicker.CalculateDigest("ITEMS")] =
                                        tally.ToString(),
                                }) ?? tally.ToString();
                        }
                    }));
        if (driver is null)
        {
            Console.WriteLine("[UI] secure trade: needed authored grids are absent");
            return;
        }

        WidgetElem trunk = arrangement.Root;
        CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = PaneLabels.SecureBarter,
                Chrome = CanonWindowChrome.NineSlice,
                Left = MathF.Max(0f, (Host.Root.Width - trunk.Width) * 0.5f),
                Top = MathF.Max(0f, (Host.Root.Height - trunk.Height) * 0.5f),
                SubstanceWidth = trunk.Width,
                SubstanceHeight = trunk.Height,
                MinWidth = trunk.Width,
                MinHeight = trunk.Height,
                Visible = false,
                RescaleX = false,
                RescaleY = false,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
            });

        SecureBarterDriver = driver;
        Host.PaneKeeper.FastenDriver(PaneLabels.SecureBarter, driver);
        _bindings.Inventory.ItemInteraction.SecureTradeRequested +=
            driver.ReqSecureBarter;
        Console.WriteLine(
            "[UI] retail secure trade panel mounted from LayoutDesc 0x2100000D");
    }

    private void AttachGearCooldowns()
    {
        GearCooldownAssets? holdings;
        lock (_bindings.Assets.DatLock)
            holdings = GearCooldownAssets.TryLoad(_bindings.Assets.Dats);

        if (holdings is null)
        {
            Console.WriteLine(
                "[UI] shared UIItem cooldown overlays absent from LayoutDesc 0x21000037");
            return;
        }

        _gearCooldownDriver = GearCooldownWidgetDriver.Bind(
            Host.Root,
            _bindings.Magic.Spellbook,
            _bindings.Magic.Objects,
            _bindings.Magic.ServerTime,
            holdings.Value);
        Console.WriteLine(
            "[UI] retail shared item cooldown overlays ready (10 DAT-authored steps)");
    }
}
