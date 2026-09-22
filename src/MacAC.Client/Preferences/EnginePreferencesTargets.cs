using System.Globalization;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Shell;
using MacAC.Client.Sound;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Cockpit.Settings;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace MacAC.Client.Preferences;

internal interface IEngineDisplayWindowTarget
{
    EngineDisplayApplyResult Apply(ReadoutPrefs readout);
}

internal readonly record struct EngineDisplayApplyResult(bool Fullscreen);

internal interface IEngineQualityApplicationTarget
{
    void AssignAlphaToCoverage(bool turnedOn);

    void ApplyAnisotropic(int tier);

    void BroadcastRasterizeSpan(int nearbyRadius, int farawayRadius);

    void ReconfigurePagingRadii(int nearbyRadius, int farawayRadius);

    void AssignWrapUpAllowance(int upperCompletionsPerCycle);
}

internal interface IEngineWidgetLockTarget
{
    void Apply(bool bolted);
}

internal interface IEngineCommsOpacityTarget
{
    void Apply(float defaultDensity, float engagedDensity);
}

internal interface IWindowedDimsCanvas
{
    Vector2D<int> Size { get; set; }

    bool IsMaximized { get; }

    void Restore();
}

internal sealed class SilkPaneDimsCanvas(IWindow window) : IWindowedDimsCanvas
{
    private readonly IWindow _window = window
        ?? throw new ArgumentNullException(nameof(window));

    public Vector2D<int> Size
    {
        get => _window.Size;
        set => _window.Size = value;
    }

    public bool IsMaximized => _window.WindowState == WindowState.Maximized;

    public void Restore() => _window.WindowState = WindowState.Normal;
}

internal sealed class SilkEngineDisplayWindowTarget : IEngineDisplayWindowTarget
{
    private readonly IWindowedDimsCanvas _window;
    private readonly IReadoutMannerSwitcher _mannerSwitcher;
    private readonly Func<string, bool> _isOfferedManner;

    public SilkEngineDisplayWindowTarget(IWindow pane)
        : this(
            new SilkPaneDimsCanvas(pane),
            new GlfwReadoutMannerSwitcher(pane),
            spec => (Graphics.DisplayModeRegistry.Resolutions
                     ?? ReadoutPrefs.OnHandResolutions).Contains(spec))
    {
    }

    internal SilkEngineDisplayWindowTarget(
        IWindowedDimsCanvas window,
        IReadoutMannerSwitcher modeSwitcher,
        Func<string, bool> isOfferedMode)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _mannerSwitcher = modeSwitcher
            ?? throw new ArgumentNullException(nameof(modeSwitcher));
        _isOfferedManner = isOfferedMode
            ?? throw new ArgumentNullException(nameof(isOfferedMode));
    }

    public EngineDisplayApplyResult Apply(ReadoutPrefs readout)
    {
        ArgumentNullException.ThrowIfNull(readout);
        bool haveResolution =
            TryDecodeResolution(readout.Resolution, out int width, out int height);

        if (readout.Fullscreen)
        {
            if (!haveResolution)
            {
                Console.WriteLine(
                    $"display: fullscreen refused - unparseable resolution '{readout.Resolution}'");
                return LatestOutcome();
            }
            if (_mannerSwitcher.LatestFullscreenManner is (int curW, int curH)
                && curW == width && curH == height)
                return LatestOutcome();
            if (!_isOfferedManner.Invoke($"{width}x{height}"))
            {
                Console.WriteLine(
                    $"display: fullscreen {width}x{height} refused - not an offered mode");
                return LatestOutcome();
            }
            if (!_mannerSwitcher.TryJoinFullscreen(width, height, out string? problem))
                Console.WriteLine(
                    $"display: fullscreen {width}x{height} failed ({problem}) - window state unchanged");
            return LatestOutcome();
        }

        if (_mannerSwitcher.IsFullscreen)
        {
            if (!haveResolution)
            {
                width = _window.Size.X;
                height = _window.Size.Y;
            }
            if (!_mannerSwitcher.TryDepartFullscreen(width, height, out string? problem))
                Console.WriteLine(
                    $"display: leaving fullscreen failed ({problem})");
            return LatestOutcome();
        }

        if (haveResolution && (_window.Size.X != width || _window.Size.Y != height))
        {
            if (_window.IsMaximized)
                _window.Restore();
            Console.WriteLine(
                $"display: resolution pick {width}x{height} " +
                $"(window was {_window.Size.X}x{_window.Size.Y})");
            _window.Size = new Vector2D<int>(width, height);
        }
        return LatestOutcome();
    }

    internal static bool TryDecodeResolution(
        string spec,
        out int width,
        out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(spec))
            return false;
        string[] pieces = spec.Split('x', 2);
        return pieces.Length is 2
            && int.TryParse(
                pieces[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(
                pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
            && width > 0
            && height > 0;
    }

    private EngineDisplayApplyResult LatestOutcome() =>
        new(_mannerSwitcher.IsFullscreen);
}

internal sealed class EnginePreferencesStartupTargets(
    IEngineDisplayWindowTarget displayWindow,
    DisplayFramePacingDriver pacing,
    CameraDriver cameras,
    OpenAlSoundEngine? sound) : IEnginePreferencesStartupTarget
{
    private readonly IEngineDisplayWindowTarget _readoutPane = displayWindow
            ?? throw new ArgumentNullException(nameof(displayWindow));
    private readonly DisplayFramePacingDriver _pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));
    private readonly CameraDriver _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
    private readonly OpenAlSoundEngine? _sound = sound;

    public EngineDisplayApplyResult ImposeReadout(ReadoutPrefs readout)
    {
        ArgumentNullException.ThrowIfNull(readout);
        _pacing.RenewEngagedObserve();
        _pacing.ImposePreference(readout.VSync);
        var outcome = _readoutPane.Apply(readout);
        ImposeFieldOfLens(_cameras, readout.FieldOfView);
        return outcome;
    }

    public void EnactSound(SoundPrefs sound) => ImposeSound(_sound, sound);

    internal static void ImposeFieldOfLens(
        CameraDriver cameras,
        float deg) => cameras.AssignPlayFov(deg * (MathF.PI / 180f));

    internal static void ImposeSound(
        OpenAlSoundEngine? engine,
        SoundPrefs sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (engine is not { IsAvailable: true })
            return;
        engine.MasterVolume = sound.Master;
        ImposeSoundRest(sound, engine);
    }

    private static void ImposeSoundRest(SoundPrefs sound, OpenAlSoundEngine engine)
    {
        (float sfx, float ambient) = CalculateNetBucketVolumes(sound);
        engine.SfxVolume = sfx;
        engine.AmbientVolume = ambient;
    }

    internal static (float Sfx, float Ambient) CalculateNetBucketVolumes(SoundPrefs sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        float sfx = sound.SfxEnabled ? sound.Sfx : 0f;
        float ambient = sound.AmbientEnabled ? sound.Ambient : 0f;
        return (sfx, ambient);
    }
}

internal sealed class EngineQualityApplicationTarget(
    RealmPaintRouter? router,
    LandTileset? landTileset,
    PagingDriver streaming,
    RealmRenderRangeLedger renderRange)
        : IEngineQualityApplicationTarget
{
    private readonly RealmPaintRouter? _router = router;
    private readonly LandTileset? _landTileset = landTileset;
    private readonly PagingDriver _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly RealmRenderRangeLedger _rasterizeSpan = renderRange ?? throw new ArgumentNullException(nameof(renderRange));

    public void AssignAlphaToCoverage(bool turnedOn) => _router?.AlphaToCoverage = turnedOn;

    public void ApplyAnisotropic(int tier) => _landTileset?.AssignAnisotropic(tier);

    public void BroadcastRasterizeSpan(int nearbyRadius, int farawayRadius)
    {
        _rasterizeSpan.NearbyRadius = nearbyRadius;
        _rasterizeSpan.FarawayRadius = farawayRadius;
    }

    public void ReconfigurePagingRadii(int nearbyRadius, int farawayRadius) =>
        _paging.ReconfigureRadii(nearbyRadius, farawayRadius);

    public void AssignWrapUpAllowance(int upperCompletionsPerCycle) =>
        _paging.UpperCompletionsPerCycle = upperCompletionsPerCycle;
}

internal sealed class EngineWidgetLockTarget(WidgetTrunk root) : IEngineWidgetLockTarget
{
    private readonly WidgetTrunk _trunk = root ?? throw new ArgumentNullException(nameof(root));

    public void Apply(bool bolted) => _trunk.WidgetBolted = bolted;
}

internal sealed class NullEngineWidgetLockTarget : IEngineWidgetLockTarget
{
    public static NullEngineWidgetLockTarget Instance { get; } = new();

    private NullEngineWidgetLockTarget()
    {
    }

    public void Apply(bool bolted)
    {
    }
}

internal sealed class EngineCommsOpacityTarget(CanonWindowOpacityDriver controller)
    : IEngineCommsOpacityTarget
{
    private readonly CanonWindowOpacityDriver _driver =
        controller ?? throw new ArgumentNullException(nameof(controller));

    public void Apply(float defaultDensity, float engagedDensity) =>
        _driver.AssignOpacity(defaultDensity, engagedDensity);
}

internal sealed class NullEngineCommsOpacityTarget : IEngineCommsOpacityTarget
{
    public static NullEngineCommsOpacityTarget Instance { get; } = new();

    private NullEngineCommsOpacityTarget()
    {
    }

    public void Apply(float defaultDensity, float engagedDensity)
    {
    }
}

internal sealed class EnginePreferencesTargets : IEnginePreferencesTargets
{
    private readonly IEngineDisplayWindowTarget _readoutPane;
    private readonly IEngineQualityApplicationTarget _fidelity;
    private readonly IEngineWidgetLockTarget _widgetMutex;
    private readonly IEngineCommsOpacityTarget _commsDensity;
    private readonly IDirectiveBus _commands;
    private readonly Action<string> _trace;
    private readonly OpenAlSoundEngine? _sound;
    private readonly CameraDriver? _cameras;

    public EnginePreferencesTargets(
        IEngineDisplayWindowTarget readoutPane,
        RealmPaintRouter? router,
        LandTileset? landTileset,
        PagingDriver paging,
        RealmRenderRangeLedger rasterizeSpan,
        WidgetTrunk? widgetTrunk,
        IDirectiveBus directives,
        CanonWindowOpacityDriver? commsDensity = null,
        Action<string>? trace = null,
        OpenAlSoundEngine? sound = null,
        CameraDriver? cameras = null)
        : this(
            readoutPane,
            new EngineQualityApplicationTarget(
                router,
                landTileset,
                paging,
                rasterizeSpan),
            widgetTrunk is null
                ? NullEngineWidgetLockTarget.Instance
                : new EngineWidgetLockTarget(widgetTrunk),
            directives,
            trace,
            commsDensity is null
                ? NullEngineCommsOpacityTarget.Instance
                : new EngineCommsOpacityTarget(commsDensity),
            sound,
            cameras)
    {
    }

    internal EnginePreferencesTargets(
        IEngineDisplayWindowTarget displayWindow,
        IEngineQualityApplicationTarget quality,
        IEngineWidgetLockTarget uiLock,
        IDirectiveBus commands,
        Action<string>? trace = null,
        IEngineCommsOpacityTarget? commsDensity = null,
        OpenAlSoundEngine? sound = null,
        CameraDriver? cameras = null)
    {
        _readoutPane = displayWindow
            ?? throw new ArgumentNullException(nameof(displayWindow));
        _fidelity = quality ?? throw new ArgumentNullException(nameof(quality));
        _widgetMutex = uiLock ?? throw new ArgumentNullException(nameof(uiLock));
        _commsDensity = commsDensity ?? NullEngineCommsOpacityTarget.Instance;
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _trace = trace ?? Console.WriteLine;
        _sound = sound;
        _cameras = cameras;
    }

    public EngineDisplayApplyResult ApplyDisplayWindowState(ReadoutPrefs readout)
    {
        var outcome = _readoutPane.Apply(readout);
        if (_cameras is not null)
            EnginePreferencesStartupTargets.ImposeFieldOfLens(_cameras, readout.FieldOfView);
        return outcome;
    }

    public void ImposeSound(SoundPrefs sound) =>
        EnginePreferencesStartupTargets.ImposeSound(_sound, sound);

    public void ImposeFidelity(QualityKnobs fidelity)
    {
        _fidelity.AssignAlphaToCoverage(fidelity.AlphaToCoverage);
        _fidelity.ApplyAnisotropic(fidelity.AnisotropicLevel);
        _fidelity.BroadcastRasterizeSpan(fidelity.NearRadius, fidelity.FarRadius);
        _fidelity.ReconfigurePagingRadii(fidelity.NearRadius, fidelity.FarRadius);
        _fidelity.AssignWrapUpAllowance(fidelity.MaxCompletionsPerFrame);
        _trace(
            $"[QUALITY] Streaming reconciled: nearRadius={fidelity.NearRadius}, " +
            $"farRadius={fidelity.FarRadius}, " +
            $"maxCompletions={fidelity.MaxCompletionsPerFrame}");
    }

    public void ImposeWidgetLock(bool bolted) => _widgetMutex.Apply(bolted);

    public void AssignSingleToonKnob(uint knobIdent, bool val) =>
        _commands.Publish(new SetSingleToonKnobEngineCmd(knobIdent, val));

    public void AssignCommsDensity(float defaultDensity, float engagedDensity) =>
        _commsDensity.Apply(defaultDensity, engagedDensity);
}
