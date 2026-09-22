using MacAC.Mechanics.Comms;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionEventRouter : IOnlineSessionEventRouting
{
    private const int Fresh = 0;
    private const int Attaching = 1;
    private const int Attached = 2;
    private const int Retired = 3;

    private readonly OnlineSessionSubscriptionSet _subscriptions = new();
    private readonly Action<int>? _checkpoint;
    private readonly RealmSession _session;
    private readonly OnlineActorSessionSink _actors;
    private readonly OnlineAmbienceSessionSink _surroundings;
    private readonly OnlineStashSessionWiring _satchel;
    private readonly OnlineToonSessionWiring _toon;
    private readonly OnlineSocialSessionWiring _social;
    private int _hop;
    private int _accepting;
    private int _stage;

    public OnlineSessionEventRouter(
        RealmSession sess,
        OnlineActorSessionSink actors,
        OnlineAmbienceSessionSink surroundings,
        OnlineStashSessionWiring satchel,
        OnlineToonSessionWiring toon,
        OnlineSocialSessionWiring social,
        Action<int>? constructionCheckpoint = null)
    {
        ArgumentNullException.ThrowIfNull(sess);
        DemandDone(actors, surroundings, satchel, toon, social);
        _session = sess;
        _actors = actors;
        _surroundings = surroundings;
        _satchel = satchel;
        _toon = toon;
        _social = social;
        _checkpoint = constructionCheckpoint;
    }

    public bool Accepting => IsAccepting();

    public void Attach()
    {
        if (Interlocked.CompareExchange(ref _stage, Attaching, Fresh) != Fresh)
            throw new InvalidOperationException("Live-session event routing can only attach once");

        Interlocked.Exchange(ref _accepting, 1);
        try
        {
            WireCharts();
            WireActors();
            WireAmbience();
            WirePlaySignals();
            WireFidelityRecompute();
            WireComms();
            WireVitals();

            if (Interlocked.CompareExchange(ref _stage, Attached, Attaching) != Attaching)
                throw new ObjectDisposedException(nameof(OnlineSessionEventRouter));
        }
        catch
        {
            Interlocked.Exchange(ref _accepting, 0);
            Interlocked.Exchange(ref _stage, Retired);
            throw;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _accepting, 0);
        Interlocked.Exchange(ref _stage, Retired);
        _subscriptions.Dispose();
    }

    private bool IsAccepting() => Volatile.Read(ref _accepting) is not 0;

    private void Checkpoint() => _checkpoint?.Invoke(++_hop);

    private void On<T>(Action<Action<T>> fasten, Action<Action<T>> unfasten, Action<T> drain)
    {
        void Guarded(T val)
        {
            if (Volatile.Read(ref _accepting) is not 0)
                drain(val);
        }
        fasten(Guarded);
        _subscriptions.Add(() => unfasten(Guarded));
        Checkpoint();
    }

    private void On(Action<Action> fasten, Action<Action> unfasten, Action drain)
    {
        void Guarded()
        {
            if (Volatile.Read(ref _accepting) is not 0)
                drain();
        }
        fasten(Guarded);
        _subscriptions.Add(() => unfasten(Guarded));
        Checkpoint();
    }

    private void Own(IDisposable subscription)
    {
        _subscriptions.Add(subscription);
        Checkpoint();
    }

    private void WireCharts()
    {
        Own(ObjectTableBindings.Wire(_session, _satchel.Objects, _satchel.PlayerGuid, _toon.Character.LocalPlayer, IsAccepting));
        Own(CombatStateBindings.Wire(_session, _toon.Combat, IsAccepting));
    }

    private void WireActors()
    {
        var session = _session;
        var sink = _actors;
        On(h => session.EntitySpawned += h, h => session.EntitySpawned -= h, sink.Spawned);
        On(h => session.EntityDeleted += h, h => session.EntityDeleted -= h, sink.Deleted);
        On(h => session.EntityPickedUp += h, h => session.EntityPickedUp -= h, sink.PickedUp);
        On(h => session.MotionUpdated += h, h => session.MotionUpdated -= h, sink.MotionUpdated);
        On(h => session.PositionUpdated += h, h => session.PositionUpdated -= h, sink.PositionUpdated);
        On(h => session.VectorUpdated += h, h => session.VectorUpdated -= h, sink.VectorUpdated);
        On(h => session.StateUpdated += h, h => session.StateUpdated -= h, sink.StateUpdated);
        On(h => session.ParentUpdated += h, h => session.ParentUpdated -= h, sink.ParentUpdated);
        On(h => session.TeleportStarted += h, h => session.TeleportStarted -= h, sink.TeleportStarted);
        On(h => session.AppearanceUpdated += h, h => session.AppearanceUpdated -= h, sink.AppearanceUpdated);
        On(h => session.PlayPhysicsScriptReceived += h, h => session.PlayPhysicsScriptReceived -= h, sink.PlayPhysicsScript);
        On(h => session.PlayPhysicsScriptTypeReceived += h, h => session.PlayPhysicsScriptTypeReceived -= h, sink.PlayPhysicsScriptType);
        On(h => session.SoundEventReceived += h, h => session.SoundEventReceived -= h, sink.SoundEvent);
    }

    private void WireAmbience()
    {
        var session = _session;
        On(h => session.EnvironChanged += h, h => session.EnvironChanged -= h, _surroundings.EnvironChanged);
        On(h => session.ServerTimeUpdated += h, h => session.ServerTimeUpdated -= h, _surroundings.ServerTimeUpdated);
    }

    private void WireFidelityRecompute()
    {
        var objects = _satchel.Objects;
        var toon = _toon.Character;
        On<ClientThing>(h => objects.ObjectAdded += h, h => objects.ObjectAdded -= h, _ => RecomputeAvatarQualities(_satchel, _toon));
        On<ClientThing>(h => objects.ObjectUpdated += h, h => objects.ObjectUpdated -= h, _ => RecomputeAvatarQualities(_satchel, _toon));
        On<ClientThing>(h => objects.ObjectRemoved += h, h => objects.ObjectRemoved -= h, _ => RecomputeAvatarQualities(_satchel, _toon));
        On<ObjectRelocation>(h => objects.ObjectMoved += h, h => objects.ObjectMoved -= h, _ => RecomputeAvatarQualities(_satchel, _toon));
        On<uint>(h => objects.ContainerContentsReplaced += h, h => objects.ContainerContentsReplaced -= h, _ => RecomputeAvatarQualities(_satchel, _toon));
        On(h => objects.Cleared += h, h => objects.Cleared -= h, () => RecomputeAvatarQualities(_satchel, _toon));
        On<SelfState.StatKind>(h => toon.LocalPlayer.AttributeChanged += h, h => toon.LocalPlayer.AttributeChanged -= h, sort =>
        {
            if (sort == SelfState.StatKind.Strength)
                RecomputeBurden(_satchel, _toon);
        });
        On(h => toon.Spellbook.EnchantmentsChanged += h, h => toon.Spellbook.EnchantmentsChanged -= h, () => RecomputeBurden(_satchel, _toon));
        On<SelfState.VitalSort>(h => toon.LocalPlayer.Changed += h, h => toon.LocalPlayer.Changed -= h, sort => RecomputeStamina(sort, _toon));
        Own(new CombatLineTranslator(_toon.Combat, _social.Chat, IsAccepting));
    }

    private void WireComms()
    {
        var session = _session;
        var comms = _social.Chat;
        On<SpeechLine.Parsed>(h => session.SpeechHeard += h, h => session.SpeechHeard -= h,
            speech => comms.OnOwnSpeech(speech.SenderName, speech.Text, speech.SenderGuid, speech.IsRanged, speech.ChatType));
        On<ServerLine.Parsed>(h => session.ServerMessageReceived += h, h => session.ServerMessageReceived -= h, msg =>
        {
            if (_social.AddText is { } appendPhrase)
                appendPhrase(msg.Message, (CanonLogTextType)msg.ChatType);
            else
                comms.OnSysMsg(msg.Message, msg.ChatType);
        });
        On<EmoteLine.Parsed>(h => session.EmoteHeard += h, h => session.EmoteHeard -= h, emote => comms.OnEmote(emote.SenderName, emote.Text, emote.SenderGuid));
        On<WireSoulEmote.Parsed>(h => session.SoulEmoteHeard += h, h => session.SoulEmoteHeard -= h, emote => comms.OnSoulEmote(emote.SenderName, emote.Text, emote.SenderGuid));
        On<PlayerDeath.Parsed>(h => session.PlayerKilledReceived += h, h => session.PlayerKilledReceived -= h,
            killed => comms.OnAvatarKilled(killed.DeathMessage, killed.VictimGuid, killed.KillerGuid, _social.PlayerGuid?.Invoke() ?? 0u));
        On<TurbineComms.Parsed>(h => session.TurbineChatReceived += h, h => session.TurbineChatReceived -= h, decoded => CourseTurbineComms(comms, decoded));
    }

    private void WireVitals()
    {
        var session = _session;
        var toon = _toon;
        SelfState self = toon.Character.LocalPlayer;
        On<OwnVitalUpdate.DecodedWhole>(h => session.VitalUpdated += h, h => session.VitalUpdated -= h,
            vital => self.OnVitalRefresh(vital.VitalId, vital.Ranks, vital.Start, vital.Xp, vital.Current));
        On<OwnVitalUpdate.DecodedLatest>(h => session.VitalCurrentUpdated += h, h => session.VitalCurrentUpdated -= h,
            vital => self.OnVitalLatest(vital.VitalId, vital.Current));
        self.AptitudeEquationBonusLocator = toon.ResolveSkillFormulaBonus;
        On<OwnAttributeUpdate.Parsed>(h => session.AttributeUpdated += h, h => session.AttributeUpdated -= h, attr =>
        {
            self.OnAttrRefresh(attr.AttributeId, attr.Ranks, attr.Start, attr.Xp);
            PushTravelAptitudeSums(toon);
        });
        On<OwnSkillUpdate.Parsed>(h => session.SkillUpdated += h, h => session.SkillUpdated -= h, aptitude =>
        {
            self.OnAptitudeWireRefresh(aptitude.SkillId, aptitude.Ranks, aptitude.AdvancementClass, aptitude.Xp, aptitude.Init, aptitude.Resistance, aptitude.LastUsed);
            // Run=24 / Jump=22 are the only movement inputs
            if (aptitude.SkillId is 22u or 24u)
                PushTravelAptitudeSums(toon);
        });
    }

    private static void DemandDone(
        OnlineActorSessionSink actors,
        OnlineAmbienceSessionSink surroundings,
        OnlineStashSessionWiring satchel,
        OnlineToonSessionWiring toon,
        OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(surroundings);
        ArgumentNullException.ThrowIfNull(satchel);
        ArgumentNullException.ThrowIfNull(toon);
        DemandDoneRest(actors, surroundings, satchel, toon, social);
    }

    private static void DemandDoneRest(OnlineActorSessionSink actors, OnlineAmbienceSessionSink surroundings, OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(social);
        ArgumentNullException.ThrowIfNull(actors.Spawned);
        ArgumentNullException.ThrowIfNull(actors.Deleted);
        DemandDoneTail(actors, surroundings, satchel, toon, social);
    }

    private static void DemandDoneTail(OnlineActorSessionSink actors, OnlineAmbienceSessionSink surroundings, OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(actors.PickedUp);
        ArgumentNullException.ThrowIfNull(actors.MotionUpdated);
        ArgumentNullException.ThrowIfNull(actors.PositionUpdated);
        ArgumentNullException.ThrowIfNull(actors.VectorUpdated);
        DemandDoneCoda(actors, surroundings, satchel, toon, social);
    }

    private static void DemandDoneCoda(OnlineActorSessionSink actors, OnlineAmbienceSessionSink surroundings, OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(actors.StateUpdated);
        ArgumentNullException.ThrowIfNull(actors.ParentUpdated);
        DemandDoneCoda2(actors, surroundings, satchel, toon, social);
    }

    private static void DemandDoneCoda2(OnlineActorSessionSink actors, OnlineAmbienceSessionSink surroundings, OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(actors.TeleportStarted);
        ArgumentNullException.ThrowIfNull(actors.AppearanceUpdated);
        ArgumentNullException.ThrowIfNull(actors.PlayPhysicsScript);
        ArgumentNullException.ThrowIfNull(actors.PlayPhysicsScriptType);
        DemandDoneCoda3(actors, surroundings, satchel, toon, social);
    }

    private static void DemandDoneCoda3(OnlineActorSessionSink actors, OnlineAmbienceSessionSink surroundings, OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(actors.SoundEvent);
        ArgumentNullException.ThrowIfNull(surroundings.EnvironChanged);
        ArgumentNullException.ThrowIfNull(surroundings.ServerTimeUpdated);
        DemandDoneCoda4(satchel, toon, social);
    }

    private static void DemandDoneCoda4(OnlineStashSessionWiring satchel, OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(satchel.Objects);
        ArgumentNullException.ThrowIfNull(satchel.PlayerGuid);
        ArgumentNullException.ThrowIfNull(toon.Combat);
        FinishThrowIfNull5(toon, social);
    }

    private static void FinishThrowIfNull5(OnlineToonSessionWiring toon, OnlineSocialSessionWiring social)
    {
        ArgumentNullException.ThrowIfNull(toon.Character);
        ArgumentNullException.ThrowIfNull(social.Chat);
        ArgumentNullException.ThrowIfNull(social.TurbineChat);
    }
}
