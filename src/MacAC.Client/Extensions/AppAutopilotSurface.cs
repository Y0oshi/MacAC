using MacAC.Assets;
using MacAC.Client.SimBridge;
using MacAC.Extensibility.Automation;
using MacAC.Extensibility.World;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.PluginHosting;
using MacAC.Sim;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
    : IAutomationCockpit, ISelfSheet, ISpellbook, ICastingControls, IChatControls,
      ICombatControls, IEquipmentControls, IItemControls,
      ILootControls, IFellowshipControls, IEnchantmentControls,
      ISimCommsWatcher,
      INavigationControls, IWorldObjectControls, IWorldTimeControls,
      ILoginControls, INetworkControls, IRecoveryControls,
      IProjectileControls, ISelectionControls, IDisposable
{
    private readonly PluginSlashRegistry _extensionDirectives;

    private const int CeilingExtensionCommsMsgs = 512;

    private const double CounterpartHeartbeatSecs = 5d;

    private readonly object _latch = new();

    private readonly IWorldPulse? _signals;

    private readonly OwnExtensionCounterpartRegistry _peers;

    private readonly string[] _counterpartTags;

    private double _counterpartHeartbeatLeftover;

    private SimCore? _runtime;

    private SimCommsLedger? _communication;

    private SimToonLedger? _toon;

    private SimArcanaCastLedger? _casting;

    private Grimoire? _grimoire;

    private ArcanaCatalog _magicRegistry = ArcanaCatalog.Empty;

    private IReadOnlyDictionary<uint, string> _aptitudeLabels =
        new Dictionary<uint, string>();

    private IReadOnlyDictionary<uint, uint> _aptitudeGlyphs =
        new Dictionary<uint, uint>();

    private Func<int, string> _speciesLabel = static _ => string.Empty;

    private IGenesisPaletteColorSource? _swatchTints;

    private Func<uint, uint, bool>? _wield;

    private Func<bool>? _equipmentOccupied;

    private Func<uint, bool>? _useGear;

    private Func<uint, uint, bool>? _enactGear;

    private Func<uint, uint, uint, int, bool>? _relocateGear;

    private Func<uint, uint, uint, bool>? _combineGearList;

    private Func<uint, uint, bool>? _discardGear;

    private Func<uint, uint, uint, bool>? _handGear;

    private Func<uint, bool, bool>? _liftGear;

    private Func<uint, bool>? _recognizeGear;

    private Func<uint, IReadOnlyList<uint>, bool>? _salvageGearList;

    private Func<uint, uint, int, bool>? _vendGear;

    private Func<uint, bool>? _dismissGhost;

    private Func<SelectionVerb, bool>? _pickAct;

    private KineticEngine? _missileKinetics;

    private IReadOnlyList<ProjectileTraceSample> _missileDiagSpecimens =
        Array.Empty<ProjectileTraceSample>();

    private long _missileDiagSpecimensExpireAt;

    private CurrentGameEngineBridge? _sessDirectives;

    private IDisposable? _communicationSubscription;

    private readonly List<ChatLine> _commsMsgs = [];

    private ulong _extensionCommsSeries;

    private long _satchelWrapUpRev;

    private InventoryReceipt _previousSatchelWrapUp;

    private readonly Dictionary<(uint Target, uint Spell), FollowedEnchantment>
        _followedEnchantments = [];

    private long _followedCastingWrapUpRev;

    private bool _destroyed;
    private static readonly string[] AttrLabels =
        ["Strength", "Endurance", "Quickness", "Coordination", "Focus", "Self"];

    public AppAutopilotSurface()
        : this(signals: null)
    {
    }

    internal AppAutopilotSurface(
        IWorldPulse? signals,
        OwnExtensionCounterpartRegistry? peers = null,
        IReadOnlyList<string>? counterpartTags = null)
    {
        _extensionDirectives = new PluginSlashRegistry((verb, problem) =>
            Console.WriteLine(
                $"[SlashCommand:{verb}] {problem.GetBaseException().Message}"));
        _signals = signals;
        _peers = peers ?? new OwnExtensionCounterpartRegistry(Path.Combine(
            MacAC.Host.UserStateLayout.Locate().Data,
            "plugin-peers"));
        _counterpartTags = [.. (counterpartTags ?? Array.Empty<string>())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)];
        _signals?.Tick += OnCounterpartBeat;
    }

    private readonly record struct FollowedEnchantment(
        uint Family,
        int Quality,
        bool IsUntargeted,
        DateTimeOffset ExpiresAt);
}
