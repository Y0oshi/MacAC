using MacAC.Client.Shell;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Presence;

namespace MacAC.Client.Link;

internal sealed record OnlineSessionDirectiveWiring(
    ClientDirectiveDriver.Mappings ClientCommands,
    ChatTranscript Chat,
    TurbineChatPhase TurbineChat,
    Func<uint> PlayerGuid,
    Action<string> SendTalk,
    Action<string, string> SendTell,
    Action<uint, string> SendChannel,
    Action<uint, uint, uint, uint, string, uint> SendTurbineChat,
    Action<HotbarSlot> AddShortcut,
    Action<uint> RemoveShortcut,
    Action<uint, int, int> AddFavorite,
    Action<uint, int> RemoveFavorite,
    Action<uint> SetSpellbookFilter,
    Action<uint> ForgetSpell,
    Action<uint, uint> SetDesiredComponent,
    Action ClearDesiredComponents,
    Action<uint, ulong> RaiseAttribute,
    Action<uint, ulong> RaiseVital,
    Action<uint, ulong> RaiseSkill,
    Action<uint, uint> TrainSkill,
    Action<string> AddFriend,
    Action<uint> RemoveFriend,
    Action ClearFriends,
    Action RequestLegacyFriends,
    Action<uint> OpenTradeNegotiations,
    Action CloseTradeNegotiations,
    Action<uint> AddToTrade,
    Action<uint, bool, bool> AcceptTrade,
    Action DeclineTrade,
    Action ResetTrade,
    Action<bool, uint, string, uint> ModifyCharacterSquelch,
    Action<bool, string> ModifyAccountSquelch,
    Action<bool, uint> ModifyGlobalSquelch,
    SimCommsLedger Communication,
    SimToonLedger CharacterState,
    Action<uint, bool> SendSingleCharacterOption,
    Action SaveCharacterOptions,
    Action<uint> SendSetTitle,
    Action<string, bool> SendFellowshipCreate,
    Action<uint> SendFellowshipRecruit,
    Action<uint> SendFellowshipDismiss,
    Action<bool> SendFellowshipQuit,
    Action<uint> SendFellowshipAssignNewLeader,
    Action<bool> SendFellowshipChangeOpenness,
    Action<bool> SendFellowshipUpdateRequest,
    Action<uint> SendAllegianceSwear,
    Action<uint> SendAllegianceBreak,
    Action<uint> SendAllegianceKick,
    Action<string> SendAllegianceInfoRequest,
    Action<bool> SendAllegianceUpdateRequest,
    Action<string>? Log = null,
    Func<string, CanonCommsPose?>? ResolvePose = null,
    Action<uint>? ExecuteMotion = null,
    Action<string>? SendSoulEmote = null);

internal readonly record struct AddShortcutEngineCmd(HotbarSlot Entry);
internal readonly record struct RemoveShortcutEngineCmd(uint Index);
internal readonly record struct AddFavoriteEngineCmd(
    uint SpellId,
    int Position,
    int TabIndex);
internal readonly record struct RemoveFavoriteEngineCmd(
    uint SpellId,
    int TabIndex);
internal readonly record struct SetArcanabookFilterEngineCmd(uint Filters);
internal readonly record struct ForgetArcanaEngineCmd(uint SpellId);
internal readonly record struct SetDesiredComponentEngineCmd(
    uint ComponentId,
    uint Amount);
internal readonly record struct ClearDesiredComponentsEngineCmd;
internal readonly record struct RaiseAttributeEngineCmd(uint StatId, ulong Cost);
internal readonly record struct RaiseVitalEngineCmd(uint StatId, ulong Cost);
internal readonly record struct RaiseSkillEngineCmd(uint StatId, ulong Cost);
internal readonly record struct TrainSkillEngineCmd(uint StatId, uint Cost);
internal readonly record struct SetSingleToonKnobEngineCmd(
    uint OptionId,
    bool Value);
internal readonly record struct SaveToonKnobsEngineCmd;
internal readonly record struct SetTitleEngineCmd(uint TitleId);
internal readonly record struct AddBuddyEngineCmd(string Name);
internal readonly record struct RemoveBuddyEngineCmd(uint CharacterId);

internal readonly record struct OpenBarterNegotiationsEngineCmd(uint PartnerGuid);
internal readonly record struct CloseBarterNegotiationsEngineCmd;
internal readonly record struct AddToBarterEngineCmd(uint ItemGuid);
internal readonly record struct AcceptBarterEngineCmd(
    uint PartnerGuid,
    bool SelfAccepted,
    bool PartnerAccepted);
internal readonly record struct DeclineBarterEngineCmd;
internal readonly record struct ResetBarterEngineCmd;
internal readonly record struct ClearBuddysEngineCmd;
internal readonly record struct AskLegacyBuddysEngineCmd;
internal readonly record struct ModifyToonMuteEngineCmd(
    bool Add,
    uint CharacterId,
    string Name,
    uint MessageType);
internal readonly record struct ModifyAccountMuteEngineCmd(
    bool Add,
    string Name);
internal readonly record struct ModifyGlobalMuteEngineCmd(
    bool Add,
    uint MessageType);

internal readonly record struct FellowsCreateEngineCmd(
    string FellowshipName,
    bool ShareXp);
internal readonly record struct FellowsRecruitEngineCmd(uint TargetGuid);
internal readonly record struct FellowsDismissEngineCmd(uint TargetGuid);
internal readonly record struct FellowsQuitEngineCmd(bool Disband);
internal readonly record struct FellowsAssignNewLeaderEngineCmd(uint NewLeaderGuid);
internal readonly record struct FellowsChangeOpennessEngineCmd(bool IsOpen);
internal readonly record struct FellowsPulseAskEngineCmd(bool PanelOpen);
internal readonly record struct AllegianceSwearEngineCmd(uint PatronGuid);
internal readonly record struct AllegianceBreakEngineCmd(uint TargetGuid);
internal readonly record struct AllegianceKickEngineCmd(uint VassalGuid);
internal readonly record struct AllegianceInfoAskEngineCmd(string PlayerName);
internal readonly record struct AllegiancePulseAskEngineCmd(bool On);

internal sealed partial class OnlineSessionDirectiveRouter : IOnlineSessionDirectiveRouting
{
    private readonly object _latch = new();

    private OnlineDirectiveBus? _commands;

    private OnlineCommsDirectiveRoute? _commsDirectives;

    private ClientDirectiveDriver.Mappings? _clientDirectives;

    private int _phase;

    public OnlineSessionDirectiveRouter(OnlineSessionDirectiveWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(mappings.ClientCommands);
        ArgumentNullException.ThrowIfNull(mappings.Chat);
        ArgumentNullException.ThrowIfNull(mappings.TurbineChat);
        ArgumentNullException.ThrowIfNull(mappings.PlayerGuid);
        ArgumentNullException.ThrowIfNull(mappings.SendTalk);
        ArgumentNullException.ThrowIfNull(mappings.SendTell);
        ArgumentNullException.ThrowIfNull(mappings.SendChannel);
        ArgumentNullException.ThrowIfNull(mappings.SendTurbineChat);
        ArgumentNullException.ThrowIfNull(mappings.Communication);
        ArgumentNullException.ThrowIfNull(mappings.CharacterState);

        _clientDirectives = mappings.ClientCommands;
        OnlineDirectiveBus directives = new OnlineDirectiveBus();
        ClientDirectiveDriver clientDirectives = new ClientDirectiveDriver(
            AssembleGuardedClientDirectives(mappings.ClientCommands));
        _commsDirectives = new OnlineCommsDirectiveRoute(new OnlineCommsDirectiveWiring(
            clientDirectives.Execute,
            mappings.Communication,
            mappings.Chat,
            mappings.TurbineChat,
            mappings.CharacterState,
            mappings.PlayerGuid,
            mappings.SendTalk,
            mappings.SendTell,
            mappings.SendChannel,
            mappings.SendTurbineChat,
            mappings.Log,
            mappings.ResolvePose,
            mappings.ExecuteMotion,
            mappings.SendSoulEmote));
        directives.Register<AddShortcutEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.AddShortcut(directive.Entry)));
        directives.Register<RemoveShortcutEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.RemoveShortcut(directive.Index)));
        directives.Register<AddFavoriteEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.AddFavorite(
                directive.SpellId,
                directive.Position,
                directive.TabIndex)));
        directives.Register<RemoveFavoriteEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.RemoveFavorite(
                directive.SpellId,
                directive.TabIndex)));
        directives.Register<SetArcanabookFilterEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SetSpellbookFilter(directive.Filters)));
        directives.Register<ForgetArcanaEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.ForgetSpell(directive.SpellId)));
        directives.Register<SetDesiredComponentEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.SetDesiredComponent(
                directive.ComponentId,
                directive.Amount)));
        directives.Register<ClearDesiredComponentsEngineCmd>(
            _ => TransmitIfEngaged(mappings.ClearDesiredComponents));
        directives.Register<RaiseAttributeEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.RaiseAttribute(directive.StatId, directive.Cost)));
        directives.Register<RaiseVitalEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.RaiseVital(directive.StatId, directive.Cost)));
        directives.Register<RaiseSkillEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.RaiseSkill(directive.StatId, directive.Cost)));
        directives.Register<TrainSkillEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.TrainSkill(directive.StatId, directive.Cost)));
        directives.Register<SetSingleToonKnobEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendSingleCharacterOption(
                    directive.OptionId,
                    directive.Value)));
        directives.Register<SaveToonKnobsEngineCmd>(
            _ => TransmitIfEngaged(mappings.SaveCharacterOptions));
        directives.Register<SetTitleEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.SendSetTitle(directive.TitleId)));
        directives.Register<AddBuddyEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.AddFriend(directive.Name)));
        directives.Register<OpenBarterNegotiationsEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.OpenTradeNegotiations(directive.PartnerGuid)));
        directives.Register<CloseBarterNegotiationsEngineCmd>(
            _ => TransmitIfEngaged(mappings.CloseTradeNegotiations));
        directives.Register<AddToBarterEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.AddToTrade(directive.ItemGuid)));
        directives.Register<AcceptBarterEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.AcceptTrade(
                directive.PartnerGuid,
                directive.SelfAccepted,
                directive.PartnerAccepted)));
        directives.Register<DeclineBarterEngineCmd>(
            _ => TransmitIfEngaged(mappings.DeclineTrade));
        directives.Register<ResetBarterEngineCmd>(
            _ => TransmitIfEngaged(mappings.ResetTrade));
        directives.Register<RemoveBuddyEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.RemoveFriend(directive.CharacterId)));
        directives.Register<ClearBuddysEngineCmd>(
            _ => TransmitIfEngaged(mappings.ClearFriends));
        directives.Register<AskLegacyBuddysEngineCmd>(
            _ => TransmitIfEngaged(mappings.RequestLegacyFriends));
        directives.Register<ModifyToonMuteEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.ModifyCharacterSquelch(
                directive.Add,
                directive.CharacterId,
                directive.Name,
                directive.MessageType)));
        directives.Register<ModifyAccountMuteEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.ModifyAccountSquelch(
                directive.Add,
                directive.Name)));
        directives.Register<ModifyGlobalMuteEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.ModifyGlobalSquelch(
                directive.Add,
                directive.MessageType)));
        directives.Register<FellowsCreateEngineCmd>(
            directive => TransmitIfEngaged(() => mappings.SendFellowshipCreate(
                directive.FellowshipName,
                directive.ShareXp)));
        directives.Register<FellowsRecruitEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipRecruit(directive.TargetGuid)));
        directives.Register<FellowsDismissEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipDismiss(directive.TargetGuid)));
        directives.Register<FellowsQuitEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipQuit(directive.Disband)));
        directives.Register<FellowsAssignNewLeaderEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipAssignNewLeader(directive.NewLeaderGuid)));
        directives.Register<FellowsChangeOpennessEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipChangeOpenness(directive.IsOpen)));
        directives.Register<FellowsPulseAskEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendFellowshipUpdateRequest(directive.PanelOpen)));
        directives.Register<AllegianceSwearEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendAllegianceSwear(directive.PatronGuid)));
        directives.Register<AllegianceBreakEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendAllegianceBreak(directive.TargetGuid)));
        directives.Register<AllegianceKickEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendAllegianceKick(directive.VassalGuid)));
        directives.Register<AllegianceInfoAskEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendAllegianceInfoRequest(directive.PlayerName)));
        directives.Register<AllegiancePulseAskEngineCmd>(
            directive => TransmitIfEngaged(() =>
                mappings.SendAllegianceUpdateRequest(directive.On)));
        _commands = directives;
    }

    public void Arm()
    {
        lock (_latch)
        {
            if (_phase is 2)
                throw new ObjectDisposedException(nameof(OnlineSessionDirectiveRouter));
            _commsDirectives?.Arm();
            _phase = 1;
        }
    }

    public bool IsEngaged
    {
        get
        {
            lock (_latch)
                return _phase is 1;
        }
    }

    public void Publish<T>(T directive) where T : notnull
    {
        lock (_latch)
        {
            if (_phase is 1 && _commsDirectives?.TryPublish(directive) != true)
                _commands?.Publish(directive);
        }
    }

    public void Dispose()
    {
        OnlineDirectiveBus? directives;
        OnlineCommsDirectiveRoute? commsDirectives;
        lock (_latch)
        {
            _phase = 2;
            directives = _commands;
            _commands = null;
            commsDirectives = _commsDirectives;
            _commsDirectives = null;
            _clientDirectives = null;
        }

        commsDirectives?.Dispose();
        directives?.Clear();
    }

    private TResult ScanClient<TResult>(
        Func<ClientDirectiveDriver.Mappings, TResult> scan,
        TResult backup)
    {
        lock (_latch)
        {
            var mappings = _clientDirectives;
            return _phase is 1 && mappings is not null
                ? scan(mappings)
                : backup;
        }
    }

    private bool CallClient(Action<ClientDirectiveDriver.Mappings> invoke)
    {
        lock (_latch)
        {
            var mappings = _clientDirectives;
            if (_phase is not 1 || mappings is null)
                return false;
            invoke(mappings);
            return true;
        }
    }

    private bool TransmitIfEngaged(Action transmit)
    {
        lock (_latch)
        {
            if (_phase is not 1)
                return false;
            transmit();
            return true;
        }
    }
}
