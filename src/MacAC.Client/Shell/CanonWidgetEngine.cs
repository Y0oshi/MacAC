using MacAC.Assets;
using MacAC.Client.Arcana;
using MacAC.Client.Extensions;
using MacAC.Client.Graphics;
using MacAC.Client.Shell.Panels;
using MacAC.Client.Shell.Testing;
using MacAC.Cockpit.Input;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Cockpit.Panels.Vitals;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell;

public sealed record CanonWidgetAssets(
    IDatAccess Dats,
    object DatLock,
    Func<uint, (uint Texture, int Width, int Height)> ResolveSprite,
    Func<uint, WidgetDatFont?> ResolveFont,
    WidgetDatFont? DefaultFont,
    BitmapFont? DebugFont,
    ClientControlsIni Controls,
    GlyphComposer Icons,
    BitmapStash TextureCache);

public sealed record VitalsEngineWiring(VitalsModel ViewModel);

public sealed record CommsEngineWiring(
    ChatModel ViewModel,
    Func<IDirectiveBus> CommandBus,
    ChatPaneState Windows,
    SettingsVault? Store = null);

public sealed record RadarEngineWiring(
    Func<WidgetRadarCapture> Snapshot,
    PickPhase Selection,
    Action<bool> SetUiLocked);

public sealed record FightingEngineWiring(
    FightingPhase State,
    SimFightingAttackLedger Attacks);

public sealed record ArcanaEngineWiring(
    Grimoire Spellbook,
    SimArcanaCastLedger Casting,
    ClientThingChart Objects,
    Func<uint> PlayerGuid,
    IReadOnlyDictionary<uint, SpellComponentCard> Components,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveDragIcon,
    Func<uint, uint> ResolveSpellIcon,
    Func<uint, uint> ResolveComponentIcon,
    PickPhase Selection,
    Func<uint, int> SpellLevel,
    Func<uint, IReadOnlyList<ArcanaExamineComponent>> SpellComponents,
    Func<MechMagicSchool, uint> MagicSkill,
    Action<uint> SelectObject,
    Action<uint> UseItem,
    Action<int, int, uint> AddFavorite,
    Action<int, uint> RemoveFavorite,
    Action<uint> SendSpellbookFilter,
    Action<uint> RemoveSpell,
    Action<uint, uint> SetDesiredComponent,
    Func<double> ServerTime);

public sealed record JumpPowerbarEngineWiring(Func<JumpChargeCapture> Snapshot);

public sealed record FpsEngineWiring(
    Func<double> FramesPerSecond,
    Func<double> DegradeMultiplier,
    Func<bool> IsVisible);

public sealed record IndicatorEngineWiring(
    Grimoire Spellbook,
    ClientThingChart Objects,
    Func<uint> PlayerGuid,
    Func<int?> Strength,
    Func<LinkStatusFrame> LinkStatus,
    Func<double> CurrentTime,
    Action RequestLinkStatusPing,
    Action EndCharacterSession,
    Action ExitGame);

public sealed record ToolbarEngineWiring(
    ClientThingChart Objects,
    HotbarStore Shortcuts,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveDragIcon,
    Action<uint> UseItem,
    FightingPhase Combat,
    ItemManaGauge ItemMana,
    Action ToggleCombat,
    GearDealingDriver ItemInteraction,
    Action<HotbarSlot>? SendAddShortcut,
    Action<uint>? SendRemoveShortcut,
    PickPhase Selection,
    Action<Action<uint, float>> SubscribeHealthChanged,
    Action<Action<uint, float>> UnsubscribeHealthChanged,
    Func<uint, bool> IsHealthTarget,
    Func<uint, string?> ResolveName,
    Func<uint, float> HealthPercent,
    Func<uint, bool> HasHealth,
    Func<uint, uint> StackSize,
    Action<uint> SendQueryHealth,
    Action<uint> SendQueryItemMana,
    Func<uint> PlayerGuid,
    Action<uint, uint, int>? SendPutItemInContainer,
    Func<uint, bool> IsVendorSplitExempt);

public sealed record ToonEngineWiring(
    ToonSheetSupplier Provider,
    SimToonTitleLedger Titles,
    ToonTitlePicker TitleResolver,
    Func<uint, SimDirectiveResult> SendSetTitle);

public sealed record KnobsEngineWiring(
    Func<IDirectiveBus> CommandBus,
    Func<bool?> IsGrounded,
    Func<bool> IsUseMouseTurningEnabled,
    Action<string> DisplaySystemMessage,
    Action<string> DisplayMouseTurningMacroLine,
    Func<CameraTurnSettings> LoadCameraTurning,
    Action<CameraTurnSettings> SaveCameraTurning,
    Func<uint, bool> CurrentCharacterOption,
    Func<ReadoutPrefs> LoadDisplay,
    Action<ReadoutPrefs> SaveDisplay,
    Func<SoundPrefs> LoadAudio,
    Action<SoundPrefs> SaveAudio,
    Func<IReadOnlyList<Panels.SettingsKnobsSheetDriver.RasterizeBundlePick>>?
        LoadRenderPackChoices = null,
    Func<long>? LoadRenderPackCatalogRevision = null,
    Func<string?>? LoadRenderPackFailureNotice = null);

public sealed record SocialEngineWiring(
    Func<MacAC.Sim.SimFellowsCapture> FellowshipSnapshot,
    Func<MacAC.Sim.SimAllegianceCapture> AllegianceSnapshot,
    MacAC.Mechanics.Fellows.FriendsLedger Friends,
    MacAC.Mechanics.Fellows.SquelchLedger Squelch,
    Func<IEnumerable<MacAC.Sim.SimFellowMemberCapture>> FellowshipMembers,
    Func<string, bool, SimDirectiveResult> FellowshipCreate,
    Func<uint, SimDirectiveResult> FellowshipRecruit,
    Func<uint, SimDirectiveResult> FellowshipDismiss,
    Func<bool, SimDirectiveResult> FellowshipQuit,
    Func<uint, SimDirectiveResult> FellowshipAssignLeader,
    Func<bool, SimDirectiveResult> FellowshipSetOpen,
    Func<bool, SimDirectiveResult> FellowshipSetPanelOpen,
    PickPhase Selection,
    Func<uint> LocalPlayerGuid,
    Func<MacAC.Sim.SimAllegianceMemberCapture?> AllegianceMonarch,
    Func<uint, MacAC.Sim.SimAllegianceMemberCapture?> AllegiancePatron,
    Func<uint, MacAC.Sim.SimAllegianceMemberCapture?> AllegianceMember,
    Func<uint, IEnumerable<MacAC.Sim.SimAllegianceMemberCapture>> AllegianceVassals,
    Func<uint, SimDirectiveResult> AllegianceSwear,
    Func<uint, SimDirectiveResult> AllegianceBreak,
    Func<uint, SimDirectiveResult> AllegianceKick,
    Func<bool, SimDirectiveResult> AllegianceSetUpdateSubscription,
    MacAC.Sim.Play.ISimBarterLens? Trade = null);

public sealed record MapDwellingEngineWiring(
    Func<MacAC.Mechanics.Realm.DerethDateMoment.Almanac> CurrentCalendar,
    Func<uint> PlayerCellId,
    Func<ObjectCreation.RemotePosition?>? HousePosition = null,
    Func<IReadOnlyList<string>>? HouseLines = null,
    Action? HouseShown = null,
    Func<IReadOnlyList<MacAC.Sim.Play.DwellingPanelLine>>? HousePanelLines = null);

public sealed record QuestEngineWiring(
    MacAC.Sim.Play.ISimContractLens Contracts,
    Func<MacAC.Mechanics.Contracts.QuestCatalogue> Catalog,
    MacAC.Sim.Play.ISimDiaryLens Journal,
    MacAC.Sim.Play.SimDiaryLedger JournalCommands,
    Func<uint> PlayerCell,
    Action<uint> AbandonContract,
    string JournalDirectory,
    Action<string> Report);

public sealed record StashEngineWiring(
    ClientThingChart Objects,
    Func<uint> PlayerGuid,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveDragIcon,
    Func<int?> Strength,
    Grimoire Spellbook,
    Action<uint>? SendUse,
    Action<uint, uint, int>? SendPutItemInContainer,
    Action<uint, uint, uint, uint>? SendStackableSplitToContainer,
    Action<uint, uint, uint>? SendStackableMerge,
    GearDealingDriver ItemInteraction,
    PickPhase Selection);

public sealed record ExternalContainerEngineWiring(
    OpenContainerState State,
    ClientThingChart Objects,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveDragIcon,
    GearDealingDriver ItemInteraction,
    PickPhase Selection,
    Action<uint> SendUse,
    Action<uint, uint, int> SendPutItemInContainer,
    Action<uint, uint, uint, uint> SendStackableSplitToContainer,
    Func<uint, bool> IsWithinUseRange);

public sealed record CanonWidgetPersistenceWiring(
    SettingsVault Store,
    Func<string> CharacterKey,
    Func<(int Width, int Height)> ScreenSize);

public sealed record CanonWidgetProbeWiring(
    bool Enabled,
    string? ScriptPath,
    bool DumpOnStart,
    Action<string> Log,
    Func<FeedAct, bool> PressInput,
    Func<FeedAct, bool, bool> SetInputHeld,
    Testing.ICanonWidgetAutopilotEngine? Runtime = null,
    Action<float, float>? QueueMouseLookDelta = null);

public sealed record CanonWidgetCursorWiring(
    CursorFeedbackDriver Feedback,
    CanonCursorKeeper Manager);

public sealed record RealmTooltipEngineWiring(
    Func<uint?> HoverGuidAtCursor,
    Func<uint, string?> ResolveName,
    Func<bool> Enabled);

public sealed record ConfirmationEngineWiring(
    Action<uint, uint, bool> SendResponse);

public sealed record AssayEngineWiring(
    Func<string> PlayerName,
    Action<uint, string> SendSetInscription,
    Action<string> DisplaySystemMessage,
    Func<int> LocalFactionBits);

public sealed record MerchantEngineWiring(
    MerchantPhase State,
    Func<GearKind, uint, uint, uint, uint, uint> ResolveIcon,
    GearDealingDriver ItemInteraction,
    PickPhase Selection,
    Action<string>? DisplaySystemMessage = null);

public sealed record KeyboardEngineWiring(
    InputRouter? Dispatcher,
    string KeyBindingsFilePath);

public sealed record ToonPickingEngineWiring(
    Func<ISimToonPickLens?> View,
    Func<uint, SimDirectiveResult> Highlight,
    Func<SimDirectiveResult> Enter,
    Func<SimDirectiveResult> RequestDelete,
    Func<SimDirectiveResult> ConfirmDelete,
    Func<SimDirectiveResult> Restore,
    Func<SimDirectiveResult> Cancel,
    Action RequestExit,
    Action? RequestCreate = null,
    bool DirectCharacterLaunch = false);

public sealed record ConnectionEngineWiring(
    Func<ISimLinkLens?> View,
    Action RequestExit,
    bool ShowProgress = true);

public sealed record CanonWidgetEngineWiring(
    WidgetHub Host,
    CanonWidgetAssets Assets,
    VitalsEngineWiring Vitals,
    CommsEngineWiring Chat,
    RadarEngineWiring Radar,
    FightingEngineWiring Combat,
    ArcanaEngineWiring Magic,
    JumpPowerbarEngineWiring JumpPowerbar,
    FpsEngineWiring Fps,
    VividTargetEngineWiring VividTarget,
    IndicatorEngineWiring Indicators,
    ToolbarEngineWiring Toolbar,
    ToonEngineWiring Character,
    StashEngineWiring Inventory,
    ExternalContainerEngineWiring ExternalContainer,
    MerchantEngineWiring Vendor,
    CanonWidgetCursorWiring Cursor,
    RealmTooltipEngineWiring WorldTooltip,
    ConfirmationEngineWiring Confirmations,
    AssayEngineWiring Appraisal,
    KnobsEngineWiring Options,
    SocialEngineWiring Social,
    MapDwellingEngineWiring MapHouse,
    QuestEngineWiring Quests,
    StackSplitGauge StackSplitQuantity,
    BufferedWidgetRegistry? Plugins,
    CanonWidgetPersistenceWiring? Persistence,
    CanonWidgetProbeWiring Probe,
    KeyboardEngineWiring? Keyboard = null,
    ToonPickingEngineWiring? CharacterSelection = null,
    ToonCreationEngineWiring? CharacterCreation = null,
    Action? CaptureScreenshot = null,
    Func<IReadOnlyList<ProjectileTraceSample>>?
        ProjectileDebugSamples = null,
    ConnectionEngineWiring? Connection = null,
    Func<bool>? IsGameplayDisplay = null,
    Action? SynchronizeDisplayPhase = null);

public sealed partial class CanonWidgetEngine : IDisposable
{
    private readonly CanonWidgetEngineWiring _bindings;

    private CreatureDisplayNamePicker? _beastLabels;
    private CanonWindowArrangementPersistence? _persistence;

    private CanonWidgetAutopilotScriptRunner? _automation;

    private readonly CanonPaneWidgetDriver _boardWidget;

    private CommsPaneDriver? _commsPaneDriver;

    private readonly FloatingCommsPaneDriver?[] _floatingCommsDrivers = new FloatingCommsPaneDriver?[4];

    private GameplayConfirmationDriver? _gameplayAckDriver;

    private CanonGearConfirmationDriver? _gearAckDriver;

    private CanonSkillTrainingConfirmationDriver? _aptitudeTrainingAckDriver;

    private WidgetShortcutDigitGraphics? _shortcutDigitVisuals;

    private GearCooldownWidgetDriver? _gearCooldownDriver;

    private VividTargetIndicatorDriver? _vividMarkIndicator;

    private ProjectileDebugOverlayDriver? _missileDiagTopLayer;

    private Panels.VitalsSideBySideDriver? _vitalsFlankByFlank;

    private ToonManagementWidgetMountMarshal? _toonManagementMount;

    private ConnectionWidgetMountMarshal? _connectionMount;
    private ToonCreationWidgetMountMarshal? _toonCreationMount;

    private PluginSidePane? _extensionFlankBoard;

    private IDisposable? _toonSheetSubscription;

    private Panels.ToonBannersDriver? _toonBannersDriver;

    private AssetShutdownTransaction? _shutdown;

    private CanonWidgetEngine(CanonWidgetEngineWiring mappings)
    {
        _bindings = mappings;
        _boardWidget = new CanonPaneWidgetDriver(
            mappings.Host.IsPaneShown,
            mappings.Host.RevealPane,
            mappings.Host.ConcealPane);

        CommsPrefs commsPrefs = mappings.Chat.Store?.LoadChat() ?? CommsPrefs.Default;
        PaneLockExhibit = new CanonWindowLockDisplayDriver(
            mappings.Host.Root.PaneManager);
        PaneDensity = new CanonWindowOpacityDriver(
            mappings.Host.Root.PaneManager,
            commsPrefs.DefaultOpacity,
            commsPrefs.ActiveOpacity);
    }

    private ToonStatDriver.Binding? _toonStatMapping;
    private System.Numerics.Vector2 _previousMonitorDims;

    private enum KnobsPanePage { Gameplay, Character, Configuration }

    private enum SocialPanePage { Friends, Allegiance, Fellowship }

    private enum DiaryPanePage { Contracts, Notes, PageList }

    private sealed class PluginWindowVisibilityDriver(
        Func<bool>? readiness,
        bool beginShown) : IRetainedPaneDriver
    {
        private bool _askedShown = beginShown;

        public void OnShown() => _askedShown = true;

        public void OnConcealed()
        {
            if (readiness?.Invoke() ?? true)
                _askedShown = false;
        }

        public void Dispose()
        {
        }

        internal bool ShouldBeShown() =>
            _askedShown && (readiness?.Invoke() ?? true);
    }
}
