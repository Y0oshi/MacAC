using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fellows;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    public static IDisposable WireAll(
        GameEventRouter router,
        ClientThingChart gearList,
        FightingPhase fighting,
        Grimoire grimoire,
        ChatTranscript comms,
        SelfState? ownAvatar = null,
        TurbineChatPhase? turbineComms = null,
        Action<int /*runSkill*/, int /*jumpSkill*/>? onAptitudesUpdated = null,
        Func<uint /*skillId*/, IReadOnlyDictionary<uint, uint> /*attrCurrents*/, uint /*formulaBonus*/>? locateAptitudeEquationBonus = null,
        Action<IReadOnlyList<HotbarSlot>>? onShortcuts = null,
        Func<uint>? avatarOid = null,
        Action<uint /*weenieError*/>? onUseDone = null,
        Action<AppraisalReader.WireParsed>? onAppraisal = null,
        ItemManaGauge? gearMana = null,
        Action<PlaySignals.ToonAckRequest>? onAckReq = null,
        Action<PlaySignals.ToonAckFinished>? onAckDone = null,
        FriendsLedger? friends = null,
        SquelchLedger? squelch = null,
        Action<IReadOnlyList<(uint Id, uint Amount)>>? onWantedModules = null,
        Action<uint /*options1*/, uint /*options2*/, bool /*trailerTruncated*/>? onToonKnobs = null,
        Func<double>? clientMoment = null,
        OpenContainerState? externalVessels = null,
        MerchantPhase? merchant = null,
        Action<string, CanonLogTextType>? onInterfacePhrase = null,
        Func<bool>? accepting = null,
        Action<PlaySignals.FellowshipWholeRefresh>? onFellowshipWholeRefresh = null,
        Action<PlaySignals.FellowshipRefreshFellow>? onFellowshipRefreshFellow = null,
        Action<uint /*quitterGuid*/>? onFellowshipQuit = null,
        Action<uint /*dismissedGuid*/>? onFellowshipDismiss = null,
        Action? onFellowshipDisband = null,
        Action<CommandReplies.AllegianceRefresh>? onAllegianceRefresh = null,
        Action<uint /*weenieError*/>? onAllegianceRefreshDone = null,
        Action<uint /*weenieError*/>? onAllegianceRefreshAborted = null,
        Action<PlaySignals.AllegianceSigninNotice>? onAllegianceSigninNotification = null,
        Action<PlaySignals.EnrollBarter>? onBarterEnroll = null,
        Action<uint /*endReason*/>? onBarterShut = null,
        Action<PlaySignals.AppendToBarter>? onBarterAppend = null,
        Action<PlaySignals.DropFromBarter>? onBarterDrop = null,
        Action<uint /*whoAccepted*/>? onBarterAdmit = null,
        Action<uint /*whoDeclined*/>? onBarterDecline = null,
        Action<uint /*whoReset*/>? onBarterRestart = null,
        Action<PlaySignals.BarterMiss>? onBarterMiss = null,
        Action? onBarterWipeAcceptance = null,
        Action<PlaySignals.HouseBlob>? onHouseBlob = null,
        Action<uint /*weenieError*/>? onHouseCondition = null,
        Action<uint /*rentTime*/>? onHouseRefreshRentMoment = null,
        Action<IReadOnlyList<PlaySignals.HouseDue>>? onHouseRefreshRentPayment = null,
        Action<IReadOnlyDictionary<uint, QuestTracker>>? onContractChart = null,
        Action<QuestTrackerUpdate>? onContractRefresh = null,
        Action<uint /*displayTitleId*/, IReadOnlyList<uint> /*titleIds*/>? onToonBannerChart = null,
        Action<uint /*titleId*/, bool /*setAsDisplay*/>? onRefreshBanner = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(gearList);
        ArgumentNullException.ThrowIfNull(fighting);
        ArgumentNullException.ThrowIfNull(grimoire);
        ArgumentNullException.ThrowIfNull(comms);

        Hookup hookup = new Hookup(router, accepting);
        using BuildScope assemble = new BuildScope(hookup);
        Binder binder = new Binder(hookup)
        {
            Items = gearList,
            Combat = fighting,
            Spellbook = grimoire,
            Chat = comms,
            LocalPlayer = ownAvatar,
            TurbineChat = turbineComms,
            OnAptitudesUpdated = onAptitudesUpdated,
            LocateAptitudeEquationBonus = locateAptitudeEquationBonus,
            OnShortcuts = onShortcuts,
            PlayerGuid = avatarOid,
            OnUseDone = onUseDone,
            OnAppraisal = onAppraisal,
            ItemMana = gearMana,
            OnConfirmationRequest = onAckReq,
            OnAckDone = onAckDone,
            Friends = friends,
            Squelch = squelch,
            OnWantedModules = onWantedModules,
            OnToonKnobs = onToonKnobs,
            ClientMoment = clientMoment ?? (static () => 0d),
            ExternalContainers = externalVessels,
            Vendor = merchant,
            OnInterfaceText = onInterfacePhrase,
            OnFellowshipWholeRefresh = onFellowshipWholeRefresh,
            OnFellowshipRefreshFellow = onFellowshipRefreshFellow,
            OnFellowshipQuit = onFellowshipQuit,
            OnFellowshipDismiss = onFellowshipDismiss,
            OnFellowshipDisband = onFellowshipDisband,
            OnAllegianceRefresh = onAllegianceRefresh,
            OnAllegianceRefreshDone = onAllegianceRefreshDone,
            OnAllegianceRefreshAborted = onAllegianceRefreshAborted,
            OnAllegianceSigninNotification = onAllegianceSigninNotification,
            OnBarterEnroll = onBarterEnroll,
            OnBarterShut = onBarterShut,
            OnBarterAppend = onBarterAppend,
            OnBarterDrop = onBarterDrop,
            OnBarterAdmit = onBarterAdmit,
            OnBarterDecline = onBarterDecline,
            OnBarterRestart = onBarterRestart,
            OnBarterMiss = onBarterMiss,
            OnBarterWipeAcceptance = onBarterWipeAcceptance,
            OnHouseBlob = onHouseBlob,
            OnHouseCondition = onHouseCondition,
            OnHouseRefreshRentMoment = onHouseRefreshRentMoment,
            OnHouseRefreshRentPayment = onHouseRefreshRentPayment,
            OnContractChart = onContractChart,
            OnContractRefresh = onContractRefresh,
            OnToonBannerChart = onToonBannerChart,
            OnRefreshBanner = onRefreshBanner,
        };

        binder.AttachComms();
        binder.AttachSocial();
        binder.AttachBarter();
        binder.AttachHousing();
        binder.AttachToon();
        binder.AttachProblems();
        binder.AttachFighting();
        binder.AttachArcana();
        binder.AttachSatchel();
        binder.AttachAvatarBlurb();
        return assemble.Complete();
    }

    // Parses one event payload; null (or false) means "malformed, ignore"
    private delegate T PayloadParser<out T>(ReadOnlySpan<byte> payload);

    // Registers handlers through the hookup, wrapping the parse-then-act pattern once
    private sealed partial class Binder(Hookup hookup)
    {
        public required ClientThingChart Items { get; init; }
        public required FightingPhase Combat { get; init; }
        public required Grimoire Spellbook { get; init; }
        public required ChatTranscript Chat { get; init; }
        public required Func<double> ClientMoment { get; init; }
        public SelfState? LocalPlayer { get; init; }
        public TurbineChatPhase? TurbineChat { get; init; }
        public Action<int, int>? OnAptitudesUpdated { get; init; }
        public Func<uint, IReadOnlyDictionary<uint, uint>, uint>? LocateAptitudeEquationBonus { get; init; }
        public Action<IReadOnlyList<HotbarSlot>>? OnShortcuts { get; init; }
        public Func<uint>? PlayerGuid { get; init; }
        public Action<uint>? OnUseDone { get; init; }
        public Action<AppraisalReader.WireParsed>? OnAppraisal { get; init; }
        public ItemManaGauge? ItemMana { get; init; }
        public Action<PlaySignals.ToonAckRequest>? OnConfirmationRequest { get; init; }
        public Action<PlaySignals.ToonAckFinished>? OnAckDone { get; init; }
        public FriendsLedger? Friends { get; init; }
        public SquelchLedger? Squelch { get; init; }
        public Action<IReadOnlyList<(uint Id, uint Amount)>>? OnWantedModules { get; init; }
        public Action<uint, uint, bool>? OnToonKnobs { get; init; }
        public OpenContainerState? ExternalContainers { get; init; }
        public MerchantPhase? Vendor { get; init; }
        public Action<string, CanonLogTextType>? OnInterfaceText { get; init; }
        public Action<PlaySignals.FellowshipWholeRefresh>? OnFellowshipWholeRefresh { get; init; }
        public Action<PlaySignals.FellowshipRefreshFellow>? OnFellowshipRefreshFellow { get; init; }
        public Action<uint>? OnFellowshipQuit { get; init; }
        public Action<uint>? OnFellowshipDismiss { get; init; }
        public Action? OnFellowshipDisband { get; init; }
        public Action<CommandReplies.AllegianceRefresh>? OnAllegianceRefresh { get; init; }
        public Action<uint>? OnAllegianceRefreshDone { get; init; }
        public Action<uint>? OnAllegianceRefreshAborted { get; init; }
        public Action<PlaySignals.AllegianceSigninNotice>? OnAllegianceSigninNotification { get; init; }
        public Action<PlaySignals.EnrollBarter>? OnBarterEnroll { get; init; }
        public Action<uint>? OnBarterShut { get; init; }
        public Action<PlaySignals.AppendToBarter>? OnBarterAppend { get; init; }
        public Action<PlaySignals.DropFromBarter>? OnBarterDrop { get; init; }
        public Action<uint>? OnBarterAdmit { get; init; }
        public Action<uint>? OnBarterDecline { get; init; }
        public Action<uint>? OnBarterRestart { get; init; }
        public Action<PlaySignals.BarterMiss>? OnBarterMiss { get; init; }
        public Action? OnBarterWipeAcceptance { get; init; }
        public Action<PlaySignals.HouseBlob>? OnHouseBlob { get; init; }
        public Action<uint>? OnHouseCondition { get; init; }
        public Action<uint>? OnHouseRefreshRentMoment { get; init; }
        public Action<IReadOnlyList<PlaySignals.HouseDue>>? OnHouseRefreshRentPayment { get; init; }
        public Action<IReadOnlyDictionary<uint, QuestTracker>>? OnContractChart { get; init; }
        public Action<QuestTrackerUpdate>? OnContractRefresh { get; init; }
        public Action<uint, IReadOnlyList<uint>>? OnToonBannerChart { get; init; }
        public Action<uint, bool>? OnRefreshBanner { get; init; }

        // Raw handler on the envelope
        private void Raw(GameEventKind sort, GameEventRouter.EventHandler handler) => hookup.Register(sort, handler);

        // Value-typed payload: act only when the parser yields something
        private void On<T>(GameEventKind sort, PayloadParser<T?> decode, Action<T> act) where T : struct
        {
            Raw(sort, parcel =>
            {
                if (decode(parcel.Payload.Span) is { } decoded)
                    act(decoded);
            });
        }

        // Reference-typed payload (strings, lists): act only when the parser yields something
        private void OnRef<T>(GameEventKind sort, PayloadParser<T?> decode, Action<T> act) where T : class
        {
            Raw(sort, parcel =>
            {
                if (decode(parcel.Payload.Span) is { } decoded)
                    act(decoded);
            });
        }

        // Payload-free event whose parser only validates the shape
        private void OnBit(GameEventKind sort, PayloadParser<bool> decode, Action act)
        {
            Raw(sort, parcel =>
            {
                if (decode(parcel.Payload.Span))
                    act();
            });
        }

        // Interface text goes to the caller's sink when one was given, else to the transcript as a system
        // line
        private void Say(string phrase, CanonLogTextType kind)
        {
            if (OnInterfaceText is { } drain)
                drain(phrase, kind);
            else
                Chat.OnSysMsg(phrase, commsKind: (uint)kind);
        }

        private void SayPlain(string phrase) => Chat.OnSysMsg(phrase, commsKind: 0u);
    }

    // Owns every registration made through it and gates each handler on accepting
    private sealed class Hookup(GameEventRouter router, Func<bool>? accepting) : IDisposable
    {
        private readonly SubscriptionLedger _register = new();

        public void Register(GameEventKind sort, GameEventRouter.EventHandler handler)
        {
            GameEventRouter.EventHandler gated = accepting is null
                ? handler
                : envelope =>
                {
                    if (accepting())
                        handler(envelope);
                };
            _register.Add(router.ListPossessed(sort, gated));
        }

        public void Dispose() => _register.Dispose();
    }

    // Releases the hookup unless construction reached Complete
    private sealed class BuildScope(Hookup hookup) : IDisposable
    {
        private bool _done;

        public IDisposable Complete()
        {
            _done = true;
            return hookup;
        }

        public void Dispose()
        {
            if (!_done)
                hookup.Dispose();
        }
    }
}
