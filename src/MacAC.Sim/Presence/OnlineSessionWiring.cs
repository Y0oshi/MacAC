using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Fellows;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

/// <summary>Where entity-facing wire events go.</summary>
public sealed record OnlineActorSessionSink(
    Action<RealmSession.MoverSpawn> Spawned,
    Action<ObjectDeletion.Parsed> Deleted,
    Action<PickupNotice.Parsed> PickedUp,
    Action<RealmSession.MoverMotionUpdate> MotionUpdated,
    Action<RealmSession.MoverPositionUpdate> PositionUpdated,
    Action<VelocityUpdate.Parsed> VectorUpdated,
    Action<GroupPhase.Parsed> StateUpdated,
    Action<AncestorSignal.Parsed> ParentUpdated,
    Action<uint> TeleportStarted,
    Action<ObjDescNotice.Parsed> AppearanceUpdated,
    Action<PlayKineticsProgram> PlayPhysicsScript,
    Action<PlayKineticsProgramKind> PlayPhysicsScriptType,
    Action<SfxSignal> SoundEvent);

public sealed record OnlineAmbienceSessionSink(Action<uint> EnvironChanged, Action<double> ServerTimeUpdated);

public sealed record OnlineStashSessionWiring(
    ClientThingChart Objects,
    Func<uint> PlayerGuid,
    Action<IReadOnlyList<HotbarSlot>>? OnShortcuts,
    Action<uint>? OnUseDone,
    ItemManaGauge? ItemMana,
    OpenContainerState? ExternalContainers,
    Action<AppraisalReader.WireParsed>? OnAppraisal = null,
    MerchantPhase? Vendor = null);

public sealed record OnlineToonSessionWiring(
    FightingPhase Combat,
    SimToonLedger Character,
    Func<uint, IReadOnlyDictionary<uint, uint>, uint>? ResolveSkillFormulaBonus,
    Action<int, int>? OnSkillsUpdated,
    Action<PlaySignals.ToonAckRequest>? OnConfirmationRequest,
    Action<PlaySignals.ToonAckFinished>? OnConfirmationDone,
    Func<double>? ClientTime,
    Action? OnMovementStatsUpdated = null,
    Action<uint, uint>? OnCharacterOptionsChanged = null);

public sealed record OnlineSocialSessionWiring(
    ChatTranscript Chat,
    TurbineChatPhase TurbineChat,
    FriendsLedger? Friends,
    SquelchLedger? Squelch,
    Action<string, CanonLogTextType>? AddText = null,
    SimFellowsLedger? Fellowship = null,
    SimAllegianceLedger? Allegiance = null,
    SimBarterLedger? Trade = null,
    SimDwellingLedger? House = null,
    SimContractLedger? Contracts = null,
    Func<uint>? PlayerGuid = null);
