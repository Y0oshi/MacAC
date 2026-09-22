using MacAC.Client.Shell;
using MacAC.Client.Telemetry;
using MacAC.Cockpit.Input;
using MacAC.Sim;

namespace MacAC.Client.Controls;

internal interface IRetainedGameplayWindowDirectives
{
    void FlipSatchel();

    void FlipFloatingCommsPane(int paneIdent);

    void FlipKnobsBoard();

    void FlipGameplayKnobsSheet();

    void FocusCommsListing();

    void LogOutToon();
}

internal sealed class RetainedGameplayWindowDirectives(CanonWidgetEngine? core)
    : IRetainedGameplayWindowDirectives
{
    private readonly CanonWidgetEngine? _runtime = core;

    public void FlipSatchel() =>
        _runtime?.FlipPane(PaneLabels.Inventory);

    public void FlipFloatingCommsPane(int paneIdent) =>
        _runtime?.SwitchFloatingCommsPane(paneIdent);

    public void FlipKnobsBoard() =>
        _runtime?.FlipPane(PaneLabels.Options);

    public void FlipGameplayKnobsSheet() =>
        _runtime?.SwitchGameplayKnobsSheet();

    public void FocusCommsListing() => _runtime?.FocusCommsEntry();

    public void LogOutToon() => _runtime?.RecordOutToon();
}

internal interface IAvatarModeGameplayDirectives
{
    void SwitchFlyOrPursue();

    void FlipAvatarManner();
}

internal sealed class AvatarModeGameplayDirectives(AvatarModeDriver controller)
    : IAvatarModeGameplayDirectives
{
    private readonly AvatarModeDriver _driver = controller
        ?? throw new ArgumentNullException(nameof(controller));

    public void SwitchFlyOrPursue() => _driver.FlipFlyOrPursue();

    public void FlipAvatarManner() => _driver.Toggle();
}

internal interface IGearTargetModeDirectives
{
    bool IsAnyMarkMannerEngaged { get; }

    void AbortMarkManner();
}

internal sealed class GearTargetModeDirectives(GearDealingDriver items)
    : IGearTargetModeDirectives
{
    private readonly GearDealingDriver _gearList = items
        ?? throw new ArgumentNullException(nameof(items));

    public bool IsAnyMarkMannerEngaged => _gearList.IsAnyObjectiveMannerEngaged;

    public void AbortMarkManner() => _gearList.AbortObjectiveManner();
}

internal interface IGameplayInputDirectiveTarget
{
    bool Handle(FeedAct act);
}

internal sealed class GameplayInputDirectiveDriver(
    IRetainedGameplayWindowDirectives retained,
    IEngineTelemetryDirectives diagnostics,
    IAvatarModeGameplayDirectives playerMode,
    IGearTargetModeDirectives targetMode,
    ISimCoreLens runtimeView,
    ISimFightingDirectives combat,
    Action? flipSoundMute = null) : IGameplayInputDirectiveTarget
{
    private readonly IRetainedGameplayWindowDirectives _kept = retained ?? throw new ArgumentNullException(nameof(retained));
    private readonly IEngineTelemetryDirectives _telemetry = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly IAvatarModeGameplayDirectives _avatarManner = playerMode ?? throw new ArgumentNullException(nameof(playerMode));
    private readonly IGearTargetModeDirectives _markManner = targetMode ?? throw new ArgumentNullException(nameof(targetMode));
    private readonly ISimCoreLens _coreLens = runtimeView
            ?? throw new ArgumentNullException(nameof(runtimeView));
    private readonly ISimFightingDirectives _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
    private readonly Action? _flipSoundMute = flipSoundMute;

    public bool Handle(FeedAct act)
    {
        if (_telemetry.Handle(act))
            return true;

        switch (act)
        {
            case FeedAct.ToggleInventoryPanel:
                _kept.FlipSatchel();
                return true;
            case FeedAct.ToggleFloatingChatWindow1:
                _kept.FlipFloatingCommsPane(1);
                return true;
            case FeedAct.ToggleFloatingChatWindow2:
                _kept.FlipFloatingCommsPane(2);
                return true;
            case FeedAct.ToggleFloatingChatWindow3:
                _kept.FlipFloatingCommsPane(3);
                return true;
            case FeedAct.ToggleFloatingChatWindow4:
                _kept.FlipFloatingCommsPane(4);
                return true;
            case FeedAct.EngineToggleAudioMute:
                _flipSoundMute?.Invoke();
                return true;
            case FeedAct.EngineToggleDebugPanel:
                return true;
            case FeedAct.EngineToggleFlyMode:
                _avatarManner.SwitchFlyOrPursue();
                return true;
            case FeedAct.EngineTogglePlayerMode:
                _avatarManner.FlipAvatarManner();
                return true;
            case FeedAct.ToggleChatEntry:
            case FeedAct.EnterChatMode:
                _kept.FocusCommsListing();
                return true;
            case FeedAct.ToggleOptionsPanel:
                _kept.FlipKnobsBoard();
                return true;
            case FeedAct.CombatToggleCombat:
                _fighting.Execute(
                    _coreLens.Generation,
                    SimFightingDirective.ToggleMode);
                return true;
            case FeedAct.LOGOUT:
                _kept.LogOutToon();
                return true;
            case FeedAct.EscapeKey:
                ProcessEscape();
                return true;
            default:
                return false;
        }
    }

    private void ProcessEscape()
    {
        if (_markManner.IsAnyMarkMannerEngaged)
            _markManner.AbortMarkManner();
        else
            _kept.FlipGameplayKnobsSheet();
    }
}
