using MacAC.Cockpit.Panels.Settings;
using MacAC.Cockpit.Settings;
using MacAC.Wire.Messages;

namespace MacAC.Client.Preferences;

internal sealed partial class EnginePreferencesDriver
{
    internal bool IsGameplayReadout => _sessReadout?.IsGameplay ?? false;

    internal IEngineDisplayWindowTarget? ReadoutPaneMark => _sessReadout;

    public ReadoutPrefs Readout { get; private set; }

    public ReadoutPrefs ReadoutPreview => Readout;

    public void AttachCoreMarks(IEnginePreferencesTargets marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        if (_coreMarks is not null)
            throw new InvalidOperationException("Runtime settings targets are by now bound");
        _coreMarks = marks;
    }

    public IDisposable AttachCoreMarksPossessed(IEnginePreferencesTargets marks)
    {
        AttachCoreMarks(marks);
        return new EngineTargetWiring(this, marks);
    }

    public void AssignWidgetBolted(bool bolted)
    {
        if (_previousImposedWidgetBolted == bolted)
            return;

        _coreMarks?.ImposeWidgetLock(bolted);
        _previousImposedWidgetBolted = bolted;
    }

    public void AssignEngagedToon(string toonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonLabel);
        EngagedToonTag = toonLabel;
    }

    public void ImposeStartup(IEnginePreferencesStartupTarget mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (_startupImposed)
            throw new InvalidOperationException("Runtime settings startup was by now applied");

        if (!_startupReadoutImposed)
        {
            var outcome = mark.ImposeReadout(Startup.Display);
            var imposed = SettleReadoutOutcome(Startup.Display, outcome);
            if (!ReferenceEquals(imposed, Startup.Display))
            {
                _depot.PersistReadout(imposed);
                Readout = imposed;
                _trace(
                    $"settings: startup fullscreen request reconciled to "
                    + $"native state {imposed.Fullscreen}");
            }
            _startupReadoutImposed = true;
        }
        if (!_startupSoundImposed)
        {
            mark.EnactSound(Startup.Audio);
            _startupSoundImposed = true;
        }
        _startupImposed = true;

        QualityKnobs baseFidelity = QualityKnobs.From(Startup.Display.Quality);
        _trace(Startup.Quality.Equals(baseFidelity)
            ? $"[QUALITY] Preset {Startup.Display.Quality} -> {Startup.Quality}"
            : $"[QUALITY] Preset {Startup.Display.Quality} overridden by env vars: {Startup.Quality}");
    }

    public void UnwireCoreMarks() => _coreMarks = null;

    public void ReqWidgetBolted(bool bolted)
    {
        var marks = _coreMarks;
        if (marks is null || _toonKnobVal is null)
            return;

        marks.AssignSingleToonKnob(
            (uint)CharacterOptionId.LockUI,
            bolted);

        // OnlineSessionDirectiveRouter applies SimToonOptionsLedger's local write synchronously before the
        // autosave send.
        if (_toonKnobVal((uint)CharacterOptionId.LockUI) != bolted)
            return;

        AssignWidgetBolted(bolted);
    }

    public EnginePreferencesCapture Startup { get; }

    public SettingsVault? ArrangementStore => _depot.ArrangementVault;

    public SoundPrefs Audio { get; private set; }

    public SoundPrefs SoundPreview => Audio;

    public CommsPrefs Chat { get; private set; }

    public ToonPrefs Character { get; private set; }

    public QualityKnobs SettledFidelity { get; private set; }

    public bool HasDraftPreview => false;

    public void FlipCycleRate()
    {
        Readout = Readout with { ShowFps = !Readout.ShowFps };

        try
        {
            _depot.PersistReadout(Readout);
        }
        catch (Exception exc)
        {
            _trace($"settings: framerate display save failed: {exc.Message}");
        }
    }

    public CameraTurnSettings FetchCamTurning() => _depot.PullCamTurning();

    public void PullToonCtx(string toonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonLabel);
        EngagedToonTag = toonLabel;
        Character = _depot.LoadCharacter(toonLabel);
        _trace($"settings: loaded character[{toonLabel}] preferences");
    }

    public void StoreCamTurning(CameraTurnSettings camTurning)
    {
        ArgumentNullException.ThrowIfNull(camTurning);
        try
        {
            _depot.PersistCamTurning(camTurning);
        }
        catch (Exception exc)
        {
            _trace($"settings: camera-turning save failed: {exc.Message}");
        }
    }

    public void StoreReadout(ReadoutPrefs readout)
    {
        try
        {
            _depot.PersistReadout(readout);
            _trace($"settings: display saved to {_depot.Location}");
            EngineDisplayApplyResult outcome = _coreMarks is null
                ? new EngineDisplayApplyResult(readout.Fullscreen)
                : _coreMarks.ApplyDisplayWindowState(readout);
            var imposed = SettleReadoutOutcome(readout, outcome);
            if (!ReferenceEquals(imposed, readout))
            {
                _depot.PersistReadout(imposed);
                _trace(
                    $"settings: fullscreen request reconciled to native state "
                    + $"{imposed.Fullscreen}");
            }
            Readout = imposed;
            ReapplyQualityPreset(imposed.Quality);
        }
        catch (Exception exc)
        {
            _trace($"settings: display save failed: {exc.Message}");
            return;
        }

        try
        {
            DisplayChanged?.Invoke(Readout);
        }
        catch (Exception exc)
        {
            _trace($"settings: display observer failed: {exc.Message}");
        }
    }

    public void StoreSound(SoundPrefs sound)
    {
        try
        {
            _depot.PersistSound(sound);
            Audio = sound;
            _coreMarks?.ImposeSound(sound);
            _trace($"settings: audio saved to {_depot.Location}");
        }
        catch (Exception exc)
        {
            _trace($"settings: audio save failed: {exc.Message}");
        }
    }

    public void StoreComms(CommsPrefs comms)
    {
        var earlier = Chat;
        try
        {
            _depot.PersistComms(comms);
            Chat = comms;
            _trace($"settings: chat saved to {_depot.Location}");
        }
        catch (Exception exc)
        {
            _trace($"settings: chat save failed: {exc.Message}");
            return;
        }

        BroadcastHearKnobEdit(
            earlier.HearGeneralChat, comms.HearGeneralChat,
            (uint)CharacterOptionId.ListenToGeneralChat);
        BroadcastHearKnobEdit(
            earlier.HearTradeChat, comms.HearTradeChat,
            (uint)CharacterOptionId.ListenToTradeChat);
        BroadcastHearKnobEdit(
            earlier.HearLFGChat, comms.HearLFGChat,
            (uint)CharacterOptionId.ListenToLFGChat);
        BroadcastHearKnobEdit(
            earlier.HearRoleplayChat, comms.HearRoleplayChat,
            (uint)CharacterOptionId.ListenToRoleplayChat);
        BroadcastHearKnobEdit(
            earlier.HearSocietyChat, comms.HearSocietyChat,
            (uint)CharacterOptionId.ListenToSocietyChat);

        _coreMarks?.AssignCommsDensity(comms.DefaultOpacity, comms.ActiveOpacity);
    }

    public void ReinstateDefaultToonCtx() => Character = _defaultToon;

    public void RestartEngagedToonTag() => EngagedToonTag = DefaultToonTag;

    public void ReapplyQualityPreset(QualityTier preset)
    {
        var settled = _locateFidelity(preset);
        _trace($"[QUALITY] ReapplyQualityPreset: {preset} -> {settled}");

        if (settled.MsaaSamples != SettledFidelity.MsaaSamples)
        {
            _trace(
                $"[QUALITY] MSAA samples change ({SettledFidelity.MsaaSamples} -> " +
                $"{settled.MsaaSamples}) requires a restart - skipped for this session.");
        }

        SettledFidelity = settled;
        _coreMarks?.ImposeFidelity(settled);
    }

    public void SynchronizeCommsFromSrvKnobs(uint options2)
    {
        CommsPrefs Reseed(CommsPrefs latest) => latest with
        {
            HearGeneralChat = (options2
                & (uint)PlayerDescReader.ToonOptions2.HearGeneralChat) != 0u,
            HearTradeChat = (options2
                & (uint)PlayerDescReader.ToonOptions2.HearTradeChat) != 0u,
            HearLFGChat = (options2
                & (uint)PlayerDescReader.ToonOptions2.HearLFGChat) != 0u,
            HearRoleplayChat = (options2
                & (uint)PlayerDescReader.ToonOptions2.HearRoleplayChat) != 0u,
            HearSocietyChat = (options2
                & (uint)PlayerDescReader.ToonOptions2.HearSocietyChat) != 0u,
        };

        var synced = Reseed(Chat);
        if (synced == Chat)
            return;

        Chat = synced;
        try
        {
            _depot.PersistComms(synced);
            _trace($"settings: chat synced from server options2=0x{options2:X8}");
        }
        catch (Exception exc)
        {
            _trace($"settings: chat sync save failed: {exc.Message}");
        }
    }

    public void AlertSrvKnobsSeeded() => SrvKnobsSeeded?.Invoke();

    internal void AttachReadoutPane(IEngineDisplayWindowTarget mark, bool fixedAutomationViewRect = false,
        bool straightToonLaunch = false)
    {
        if (_sessReadout is not null)
            throw new InvalidOperationException("The display window is by now bound");
        _sessReadout = new SessionReadoutPaneMark(mark, fixedAutomationViewRect, straightToonLaunch);
    }

    internal void AssignGameplayReadout(bool engaged) => _sessReadout?.AssignGameplay(engaged);

    internal void FinishStraightLaunch() => _sessReadout?.FinishStraightLaunch();

    internal static QualityKnobs ImposeSceneryPaintGap(
        QualityKnobs fidelity,
        int sceneryPaintGap)
    {
        if (sceneryPaintGap is >= 3 and <= 25)
        {
            fidelity = fidelity with
            {
                NearRadius = Math.Min(
                    fidelity.NearRadius,
                    sceneryPaintGap),
                FarRadius = sceneryPaintGap,
            };
        }

        return fidelity;
    }

    private void LoosenCoreMarks(IEnginePreferencesTargets anticipated)
    {
        if (ReferenceEquals(_coreMarks, anticipated))
            _coreMarks = null;
    }

    private static ReadoutPrefs SettleReadoutOutcome(
        ReadoutPrefs asked,
        EngineDisplayApplyResult outcome)
    {
        return asked.Fullscreen == outcome.Fullscreen
            ? asked
            : asked with { Fullscreen = outcome.Fullscreen };
    }

    public Action? SrvKnobsSeeded { get; set; }

    private void BroadcastHearKnobEdit(bool earlier, bool latest, uint knobIdent)
    {
        if (earlier == latest)
            return;
        _coreMarks?.AssignSingleToonKnob(knobIdent, latest);
    }

    private static QualityKnobs LocateFidelity(
        QualityTier preset,
        int sceneryPaintGap)
    {
        var fidelity = ImposeSceneryPaintGap(
            QualityKnobs.From(preset),
            sceneryPaintGap);
        return QualityKnobs.WithEnvironSubstitutions(fidelity);
    }
}
