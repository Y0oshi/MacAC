using MacAC.Cockpit.Panels.Settings;
using MacAC.Cockpit.Settings;

namespace MacAC.Client.Preferences;

internal interface IEnginePreferencesStorage
{
    SettingsVault? ArrangementVault { get; }

    string Location { get; }

    ReadoutPrefs LoadDisplay();

    SoundPrefs LoadAudio();

    CommsPrefs LoadChat();

    ToonPrefs LoadCharacter(string toonTag);

    CameraTurnSettings PullCamTurning();

    void PersistReadout(ReadoutPrefs readout);

    void PersistSound(SoundPrefs sound);

    void PersistComms(CommsPrefs comms);

    void PersistCamTurning(CameraTurnSettings camTurning);
}

internal sealed class JsonEnginePreferencesStorage : IEnginePreferencesStorage
{
    public JsonEnginePreferencesStorage(string trail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trail);
        Location = trail;
        _store = new SettingsVault(trail);
    }

    private readonly SettingsVault _store;

    public SettingsVault ArrangementVault => _store;
    public string Location { get; }

    public ReadoutPrefs LoadDisplay() => _store.LoadDisplay();

    public SoundPrefs LoadAudio() => _store.LoadAudio();

    public CommsPrefs LoadChat() => _store.LoadChat();

    public ToonPrefs LoadCharacter(string toonTag) =>
        _store.LoadCharacter(toonTag);

    public CameraTurnSettings PullCamTurning() => _store.PullCameraTurning();

    public void PersistReadout(ReadoutPrefs readout) => _store.PersistDisplay(readout);

    public void PersistSound(SoundPrefs sound) => _store.PersistAudio(sound);

    public void PersistComms(CommsPrefs comms) => _store.PersistChat(comms);

    public void PersistCamTurning(CameraTurnSettings camTurning) =>
        _store.PersistCameraTurning(camTurning);
}

internal sealed record EnginePreferencesCapture(
    ReadoutPrefs Display,
    SoundPrefs Audio,
    CommsPrefs Chat,
    ToonPrefs Character,
    QualityKnobs Quality);

internal interface IEnginePreferencesStartupTarget
{
    EngineDisplayApplyResult ImposeReadout(ReadoutPrefs readout);

    void EnactSound(SoundPrefs sound);
}

internal interface IEnginePreferencesTargets
{
    EngineDisplayApplyResult ApplyDisplayWindowState(ReadoutPrefs readout);

    void ImposeSound(SoundPrefs sound);

    void ImposeFidelity(QualityKnobs fidelity);

    void ImposeWidgetLock(bool bolted);

    void AssignSingleToonKnob(uint knobIdent, bool val);

    void AssignCommsDensity(float defaultDensity, float engagedDensity);
}

internal interface IEnginePreferencesPreviewSource
{
    bool HasDraftPreview { get; }

    ReadoutPrefs ReadoutPreview { get; }

    SoundPrefs SoundPreview { get; }
}

internal sealed partial class EnginePreferencesDriver :
    IEnginePreferencesPreviewSource
{
    private const string DefaultToonTag = "default";

    private readonly IEnginePreferencesStorage _depot;

    private readonly Func<QualityTier, QualityKnobs> _locateFidelity;

    private readonly Func<uint, bool>? _toonKnobVal;

    private readonly Action<string> _trace;

    private IEnginePreferencesTargets? _coreMarks;

    private ToonPrefs _defaultToon;

    private bool _startupReadoutImposed;

    private bool _startupSoundImposed;

    private bool _startupImposed;

    private bool? _previousImposedWidgetBolted;

    private SessionReadoutPaneMark? _sessReadout;

    public EnginePreferencesDriver(
        IEnginePreferencesStorage storage,
        Func<QualityTier, QualityKnobs>? locateFidelity = null,
        Action<string>? trace = null,
        Func<uint, bool>? toonKnobVal = null)
    {
        _depot = storage ?? throw new ArgumentNullException(nameof(storage));
        Readout = _depot.LoadDisplay();
        _locateFidelity = locateFidelity
            ?? (preset => LocateFidelity(
                preset,
                Readout.LandscapeDrawDistance));
        _trace = trace ?? Console.WriteLine;
        _toonKnobVal = toonKnobVal;

        Audio = _depot.LoadAudio();
        Chat = _depot.LoadChat();
        _defaultToon = _depot.LoadCharacter(DefaultToonTag);
        Character = _defaultToon;
        SettledFidelity = _locateFidelity(Readout.Quality);
        Startup = new EnginePreferencesCapture(
            Readout,
            Audio,
            Chat,
            Character,
            SettledFidelity);
    }

    public string EngagedToonTag { get; private set; } = DefaultToonTag;

    public event Action<ReadoutPrefs>? DisplayChanged;

    private sealed class EngineTargetWiring(
        EnginePreferencesDriver holder,
        IEnginePreferencesTargets anticipated) : IDisposable
    {
        private EnginePreferencesDriver? _holder = holder;
        private readonly IEnginePreferencesTargets _anticipated = anticipated;

        public void Dispose()
        {
            Interlocked.Exchange(ref _holder, null)?
                .LoosenCoreMarks(_anticipated);
        }
    }
}
