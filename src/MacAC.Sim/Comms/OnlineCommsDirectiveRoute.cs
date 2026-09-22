using MacAC.Mechanics.Comms;
using MacAC.Wire.Messages;
using MacAC.Sim.Play;
using MacAC.Sim.Presence;

namespace MacAC.Sim.Comms;

public sealed record OnlineCommsDirectiveWiring(
    Action<ExecuteClientDirectiveCmd> ExecuteClientCommand,
    SimCommsLedger Communication,
    ChatTranscript Chat,
    TurbineChatPhase TurbineChat,
    SimToonLedger CharacterState,
    Func<uint> PlayerGuid,
    Action<string> SendTalk,
    Action<string, string> SendTell,
    Action<uint, string> SendChannel,
    Action<uint, uint, uint, uint, string, uint> SendTurbineChat,
    Action<string>? Log = null,
    Func<string, CanonCommsPose?>? ResolvePose = null,
    Action<uint>? ExecuteMotion = null,
    Action<string>? SendSoulEmote = null);

public sealed class OnlineCommsDirectiveRoute : IOnlineSessionDirectiveRouting, IDirectiveBus
{
    private const int Idle = 0;
    private const int Active = 1;
    private const int Retired = 2;

    private static readonly Dictionary<CommsChannelKind, ChannelKindLite> TurbineHalls = new()
    {
        [CommsChannelKind.Allegiance] = ChannelKindLite.Allegiance,
        [CommsChannelKind.General] = ChannelKindLite.General,
        [CommsChannelKind.Trade] = ChannelKindLite.Trade,
        [CommsChannelKind.Lfg] = ChannelKindLite.Lfg,
        [CommsChannelKind.Roleplay] = ChannelKindLite.Roleplay,
        [CommsChannelKind.Society] = ChannelKindLite.Society,
        [CommsChannelKind.Olthoi] = ChannelKindLite.Olthoi,
    };

    private readonly object _latch = new();
    private OnlineDirectiveBus? _bus;
    private int _phase;

    public OnlineCommsDirectiveRoute(OnlineCommsDirectiveWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(mappings.ExecuteClientCommand);
        ArgumentNullException.ThrowIfNull(mappings.Communication);
        ArgumentNullException.ThrowIfNull(mappings.Chat);
        ArgumentNullException.ThrowIfNull(mappings.TurbineChat);
        ArgumentNullException.ThrowIfNull(mappings.CharacterState);
        ArgumentNullException.ThrowIfNull(mappings.PlayerGuid);
        ArgumentNullException.ThrowIfNull(mappings.SendTalk);
        ArgumentNullException.ThrowIfNull(mappings.SendTell);
        ArgumentNullException.ThrowIfNull(mappings.SendChannel);
        ArgumentNullException.ThrowIfNull(mappings.SendTurbineChat);

        OnlineDirectiveBus bus = new OnlineDirectiveBus();
        bus.Register<ExecuteClientDirectiveCmd>(c => WhenEngaged(() => mappings.ExecuteClientCommand(c)));
        bus.Register<SendServerDirectiveCmd>(c =>
        {
            if (!string.IsNullOrEmpty(c.Text))
                WhenEngaged(() => mappings.SendTalk(c.Text));
        });
        bus.Register<SendCommsCmd>(c => CourseComms(mappings, c));
        bus.Register<TransmitCrudeLaneCmd>(c => WhenEngaged(() => mappings.SendChannel(c.ChannelId, c.Text)));
        _bus = bus;
    }

    public bool IsActive
    {
        get { lock (_latch) return _phase == Active; }
    }

    public void Arm()
    {
        lock (_latch)
        {
            if (_phase == Retired)
                throw new ObjectDisposedException(nameof(OnlineCommsDirectiveRoute));
            _phase = Active;
        }
    }

    public void Dispose()
    {
        OnlineDirectiveBus? bus;
        lock (_latch)
        {
            _phase = Retired;
            bus = _bus;
            _bus = null;
        }
        bus?.Clear();
    }

    public void Publish<T>(T directive) where T : notnull
    {
        if (!TryPublish(directive))
            Console.WriteLine($"[OnlineCommsDirectiveRoute] not supported command type {typeof(T).FullName}; dropping");
    }

    public bool TryPublish<T>(T directive) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(directive);
        Type kind = typeof(T);
        if (kind != typeof(ExecuteClientDirectiveCmd) && kind != typeof(SendServerDirectiveCmd) && kind != typeof(SendCommsCmd) && kind != typeof(TransmitCrudeLaneCmd))
            return false;

        lock (_latch)
        {
            if (_phase == Active)
                _bus?.Publish(directive);
        }
        return true;
    }

    private bool WhenEngaged(Action transmit)
    {
        lock (_latch)
        {
            if (_phase != Active)
                return false;
            transmit();
            return true;
        }
    }

    private void CourseComms(OnlineCommsDirectiveWiring wiring, SendCommsCmd directive)
    {
        string phrase = directive.Text;
        if (string.IsNullOrEmpty(phrase))
            return;

        switch (directive.Channel)
        {
            case CommsChannelKind.Say:
                Speak(wiring, phrase);
                return;
            case CommsChannelKind.Tell:
                if (!string.IsNullOrEmpty(directive.TargetName))
                    WhenEngaged(() => wiring.SendTell(directive.TargetName, phrase));
                return;
        }

        // Without Turbine chat the allegiance channel falls back to the legacy broadcast id.
        if (directive.Channel == CommsChannelKind.Allegiance && !wiring.TurbineChat.Enabled)
        {
            TransmitLegacy(wiring, CommsChannelKind.AllegianceBroadcast, phrase);
            return;
        }

        if (TurbineHalls.TryGetValue(directive.Channel, out ChannelKindLite hall))
            TransmitTurbine(wiring, hall, phrase);
        else
            TransmitLegacy(wiring, directive.Channel, phrase);
    }

    private void Speak(OnlineCommsDirectiveWiring wiring, string phrase)
    {
        string spoken = CanonPublicCommsReader.DistillPostures(phrase, wiring.ResolvePose, posture =>
        {
            wiring.ExecuteMotion?.Invoke(posture.MotionCommand);
            if (!string.IsNullOrEmpty(posture.OthersText))
                wiring.SendSoulEmote?.Invoke(posture.OthersText);
            if (!string.IsNullOrEmpty(posture.SelfText))
                wiring.Chat.OnSoulEmote("You", posture.SelfText, 0u);
        });
        if (!string.IsNullOrEmpty(spoken))
            WhenEngaged(() => wiring.SendTalk(spoken));
    }

    private void TransmitTurbine(OnlineCommsDirectiveWiring wiring, ChannelKindLite hall, string phrase)
    {
        var turbine = wiring.TurbineChat;
        var latch = TurbineCommsMembershipTurnstile.Evaluate(hall, turbine, wiring.CharacterState.Options, wiring.CharacterState.IsOlthoiAvatar);
        if (latch.Status != TurbineCommsTurnstileStatus.Allowed)
        {
            if (TurbineCommsMembershipTurnstile.LocateRefusalPhrase(latch) is (string refusal, CanonLogTextType sort))
                wiring.Communication.AddText(refusal, sort);
            return;
        }

        uint cookie = turbine.UpcomingCtxIdent();
        uint sender = wiring.PlayerGuid();
        wiring.Log?.Invoke($"chat: outbound TurbineComms {latch.DisplayName} room=0x{latch.RoomId:X8} chatType={latch.ChatType} cookie=0x{cookie:X} sender=0x{sender:X8} len={phrase.Length}");
        WhenEngaged(() => wiring.SendTurbineChat(latch.RoomId, latch.ChatType, (uint)TurbineComms.RelayKind.SendToRoomById, sender, phrase, cookie));
    }

    private void TransmitLegacy(OnlineCommsDirectiveWiring wiring, CommsChannelKind lane, string phrase)
    {
        if (ChannelPicker.Resolve(lane) is not { } legacy)
        {
            wiring.Log?.Invoke($"chat: SendCommsCmd kind={lane} dropped (no legacy id)");
            return;
        }

        wiring.Log?.Invoke($"chat: outbound legacy ChatChannel {legacy.DisplayName} id=0x{legacy.ChannelId:X8} len={phrase.Length}");
        if (!WhenEngaged(() => wiring.SendChannel(legacy.ChannelId, phrase)))
            return;

        // Channels the server echoes back need no local line.
        if (new ChannelFacts.Classic(legacy.ChannelId, legacy.DisplayName).IsSelfEchoLane())
            return;
        wiring.Chat.OnSelfSent(ChatFlavor.Channel, phrase, markOrLane: legacy.DisplayName, tracePhraseKind: LegacyChannelType.Resolve(legacy.ChannelId, ownTransmit: true));
    }
}

/// <summary>The stable bus the UI talks to; the live route behind it comes and goes with the session.</summary>
public sealed class OnlineCommsDirectiveSurface(Func<string, bool>? tryHndExtensionDirective = null) : IPluginDirectiveBus
{
    private readonly object _latch = new();
    private readonly Func<string, bool>? _extensionDirectives = tryHndExtensionDirective;
    private OnlineCommsDirectiveRoute? _course;

    public IOnlineSessionDirectiveRouting Attach(OnlineCommsDirectiveRoute course)
    {
        ArgumentNullException.ThrowIfNull(course);
        lock (_latch)
        {
            if (_course is not null)
                throw new InvalidOperationException("A live chat command route is by now attached");
            _course = course;
            return new Lease(this, course);
        }
    }

    public void Publish<T>(T directive) where T : notnull
    {
        OnlineCommsDirectiveRoute? course;
        lock (_latch)
            course = _course;
        course?.Publish(directive);
    }

    public bool TryHndExtensionDirective(string directiveStroke) => _extensionDirectives?.Invoke(directiveStroke) == true;

    private void Release(OnlineCommsDirectiveRoute anticipated)
    {
        anticipated.Dispose();
        lock (_latch)
        {
            if (ReferenceEquals(_course, anticipated))
                _course = null;
        }
    }

    private sealed class Lease(OnlineCommsDirectiveSurface holder, OnlineCommsDirectiveRoute course) : IOnlineSessionDirectiveRouting
    {
        private readonly object _latch = new();
        private OnlineCommsDirectiveSurface? _holder = holder;

        public void Arm() => course.Arm();

        public void Dispose()
        {
            lock (_latch)
            {
                if (_holder is null)
                    return;
                _holder.Release(course);
                _holder = null;
            }
        }
    }
}
