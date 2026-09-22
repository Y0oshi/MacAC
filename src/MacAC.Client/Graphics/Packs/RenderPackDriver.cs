using MacAC.Client.Extensions;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal sealed record RenderPackRegistryEntry(
    RenderPackCard Descriptor,
    IRenderPackFiles Assets,
    long RegistrationId,
    bool IsCompatible,
    string? IncompatibilityReason,
    IReadOnlyDictionary<string, string?> PresetIncompatibilityReasons);

internal sealed class RasterizeBundleRegistry
{
    private readonly Dictionary<string, RenderPackRegistryEntry> _listings;

    private RasterizeBundleRegistry(Dictionary<string, RenderPackRegistryEntry> listings)
    {
        _listings = listings;
    }

    internal IReadOnlyList<RenderPackRegistryEntry> Listings
    {
        get
        {
            return _listings.Values
            .OrderBy(static val => val.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static val => val.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        }
    }

    internal bool TryGet(string ident, out RenderPackRegistryEntry listing) =>
        _listings.TryGetValue(ident, out listing!);

    internal static RasterizeBundleRegistry Build(
        IEnumerable<BufferedRasterizeBundleEnrollment> registrations,
        RasterizeBundleHubCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(capabilities);
        var listings = new Dictionary<string, RenderPackRegistryEntry>(
            StringComparer.OrdinalIgnoreCase);

        foreach (BufferedRasterizeBundleEnrollment enrollment in registrations)
        {
            var descriptor = enrollment.Descriptor;
            var validation =
                RenderPackValidator.VetDescriptor(descriptor, capabilities);
            if (listings.ContainsKey(descriptor.Id))
                continue;

            IReadOnlyDictionary<string, string?> presetCauses =
                descriptor.QualityPresets is null
                    ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    : descriptor.QualityPresets
                        .Where(static preset => preset is not null
                            && !string.IsNullOrWhiteSpace(preset.Id))
                        .GroupBy(static preset => preset.Id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            static cluster => cluster.Key,
                            cluster =>
                            {
                                var outcome =
                                    RenderPackValidator.VetPresetCompatibility(
                                        descriptor,
                                        cluster.First(),
                                        capabilities);
                                return outcome.Success ? null : outcome.Reason;
                            },
                            StringComparer.OrdinalIgnoreCase);
            listings.Add(
                descriptor.Id,
                new RenderPackRegistryEntry(
                    descriptor,
                    enrollment.Assets,
                    enrollment.RegistrationId,
                    validation.Success,
                    validation.Reason,
                    presetCauses));
        }

        return new RasterizeBundleRegistry(listings);
    }
}

internal interface IRenderPackEngine : IDisposable
{
    RenderPackCard Descriptor { get; }

    QualityLadderStep Preset { get; }
}

internal interface IDefaultRealmPathRenderPackEngine : IRenderPackEngine
{
}

internal interface IRenderPackEngineMint
{
    IRenderPackEngine Build(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions);
}

internal enum RenderPackActivationPhase
{
    Retail,
    CandidatePending,
    Active,
    FailedToRetail,
}

internal readonly record struct RenderPackActivationCapture(
    RenderPackActivationPhase State,
    RenderPackPick Selection,
    string? ActivePackDisplayName,
    string? Reason,
    long ActivationGeneration);

internal readonly record struct RasterizeBundleActivationReach(
    int Width,
    int Height,
    int SampleCount)
{
    internal void Validate()
    {
        if (Width <= 0 || Height <= 0 || SampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Activation extent has to be positive");
    }
}

internal sealed partial class RenderPackDriver :
    IDisposable,
    IRenderPackTelemetryCaptureSource
{
    private readonly Func<RasterizeBundleRegistry> _registry;

    private readonly IRenderPackEngineMint _maker;

    private readonly IRenderPackReceiverPipelineMarshal? _recipientPipes;

    private readonly IRenderPackPreparationRota _prepScheduler;

    private readonly RenderPackRegistrySource? _registrySrc;

    private readonly Dictionary<RenderPackPick, long> _failedSelections = [];

    private readonly RasterizeBundlePerformancePane _performance = new();

    private RenderPackPick? _queued;

    private AtmosphericAutoQualityDriver? _autoFidelity;

    private AtmosphericFidelityTier? _queuedAutoFidelity;

    private string? _queuedAutoBackupCause;

    private QueuedPrep? _prep;
    private long _engagedEnrollmentIdent;

    private IRenderPackEngine? _observedPerformanceCore;

    private long _observedAssetGen = -1;
    private bool _destroyed;

    private int _registryAltered;

    internal RenderPackDriver(
        Func<RasterizeBundleRegistry> catalog,
        IRenderPackEngineMint factory,
        IRenderPackReceiverPipelineMarshal? recipientPipes = null,
        IRenderPackPreparationRota? prepScheduler = null,
        RenderPackRegistrySource? registrySrc = null)
    {
        _registry = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _maker = factory ?? throw new ArgumentNullException(nameof(factory));
        _recipientPipes = recipientPipes;
        _prepScheduler = prepScheduler
            ?? ThreadPoolRenderPackPreparationRota.Instance;
        _registrySrc = registrySrc;
        registrySrc?.Changed += OnRegistryAltered;
    }

    private enum AutomaticBulletinManner : byte
    {
        Disable,
        Initialise,
        Preserve,
    }

    private readonly record struct AutomaticBulletin(
        AutomaticBulletinManner Mode,
        AtmosphericQualityAllowance[]? Budgets,
        AtmosphericFidelityTier Initial,
        AtmosphericFidelityTier Maximum)
    {
        internal static AutomaticBulletin Disabled { get; } = new(
            AutomaticBulletinManner.Disable,
            Budgets: null,
            AtmosphericFidelityTier.Low,
            AtmosphericFidelityTier.Low);

        internal static AutomaticBulletin Preserve { get; } = new(
            AutomaticBulletinManner.Preserve,
            Budgets: null,
            AtmosphericFidelityTier.Low,
            AtmosphericFidelityTier.Low);
    }

    private sealed record ActivationScheme(
        RenderPackRegistryEntry Entry,
        RenderPackPick Selection,
        QualityLadderStep Preset,
        RasterizeBundleActivationReach Extent,
        bool RequirePerformanceSource,
        AutomaticBulletin AutomaticPublication);

    private sealed class QueuedPrep(ActivationScheme plan) : IDisposable
    {
        private PreparationVerdict? _verdict;

        internal ActivationScheme Plan { get; } = plan;

        internal Task Work { get; set; } = Task.CompletedTask;

        internal PreparationVerdict? Verdict
        {
            set => _verdict = value;
        }

        internal PreparationVerdict GrabVerdict()
        {
            if (!Work.IsCompleted)
                throw new InvalidOperationException("Render-pack preparation isn't complete");
            PreparationVerdict verdict = _verdict
                ?? throw new InvalidOperationException(
                    "Render-pack preparation completed without an outcome");
            _verdict = null;
            return verdict;
        }

        public void Dispose()
        {
            _verdict?.Dispose();
            _verdict = null;
        }
    }

    private sealed class PreparationVerdict(
        IRenderPackEngine? core,
        IRasterizeBundleRecipientPipeContender? recipientContender,
        string? missCause) : IDisposable
    {
        private IRenderPackEngine? _runtime = core;
        private IRasterizeBundleRecipientPipeContender? _recipientContender = recipientContender;

        internal string? MissCause { get; } = missCause;

        public void Dispose() => _ = TeardownAssetList();

        internal static PreparationVerdict Ready(
            IRenderPackEngine core,
            IRasterizeBundleRecipientPipeContender? recipientContender) =>
            new(core, recipientContender, null);

        internal static PreparationVerdict Failed(string cause) =>
            new(null, null, cause);

        internal IRenderPackEngine GrabCore()
        {
            IRenderPackEngine core = _runtime
                ?? throw new InvalidOperationException(
                    "The prepared render-pack outcome has no runtime");
            _runtime = null;
            return core;
        }

        internal IRasterizeBundleRecipientPipeContender? GrabRecipientContender()
        {
            var contender = _recipientContender;
            _recipientContender = null;
            return contender;
        }

        internal string? TeardownAssetList()
        {
            string? recipientMiss = TryTeardownRecipientContender(_recipientContender);
            _recipientContender = null;
            string? coreMiss = TryTeardown(_runtime);
            _runtime = null;
            return FuseSunsetMisses(recipientMiss, coreMiss);
        }
    }
}
