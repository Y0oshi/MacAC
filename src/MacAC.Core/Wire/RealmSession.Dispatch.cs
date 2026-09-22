using System.Buffers.Binary;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Routes one decoded game message by opcode to the matching event or handshake step.</summary>
public sealed partial class RealmSession
{
    private const uint DddInterrogationOpcode = 0xF7E5u;
    private const uint DddProblemOpcode = 0xF7E2u;
    private const uint DddFinishOpcode = 0xF7EAu;
    private const uint AdminEnvironsOpcode = 0xEA60u;
    private const uint AvatarWarpOpcode = 0xF751u;

    private void Relay(uint opcode, ReadOnlySpan<byte> corpus, ReadOnlyMemory<byte> msg)
    {
        switch (opcode)
        {
            case ToonRoster.Opcode:
                if (Guarded(ToonRoster.Parse, corpus, out ToonRoster.ParsedUnit lineup))
                {
                    Characters = lineup;
                    CharacterListReceived?.Invoke(lineup);
                }
                break;

            case RealmName.Opcode:
                if (Guarded(RealmName.Parse, corpus, out RealmName.Parsed realm))
                {
                    SrvDetails = realm;
                    ServerNameReceived?.Invoke(realm);
                }
                break;

            case CharacterErase.Opcode when CharacterErase.IsAcknowledgement(corpus):
                CharacterDeleteAcknowledged?.Invoke();
                break;

            case GenesisVerdict.ResponseOpcode:
                OnGenesisReply(corpus);
                break;

            case CharacterFault.Opcode:
                if (Guarded(CharacterFault.Parse, corpus, out CharacterFault.ParsedDef flaw) && flaw.AsCode != CharacterFault.WireCode.NumErrors)
                {
                    _previousToonPickProblem = flaw;
                    CharacterErrorReceived?.Invoke(flaw);
                }
                break;

            case DddInterrogationOpcode:
                _blobInterrogationReceived = true;
                TransmitPlayMsg(DddInterrogationReply.Build(BlobVersions), GameQueueGroup.DatabaseQueue);
                break;

            case DddOpen.Opcode:
                try
                {
                    if (DddOpen.Parse(corpus).RequiresRefresh)
                        AssignConnectionHeadway(new(LinkPhase.Unsupported, new UnknownDataUpdateException().Message));
                }
                catch (InvalidDataException problem)
                {
                    AssignConnectionHeadway(new(LinkPhase.Failed, problem.Message));
                }
                break;

            case DddProblemOpcode:
                AssignConnectionHeadway(new(LinkPhase.Unsupported, new UnknownDataUpdateException().Message));
                break;

            case DddFinishOpcode:
                if (corpus.Length is not 4)
                {
                    AssignConnectionHeadway(new(LinkPhase.Failed, "Invalid game-data completion response."));
                }
                else if (ConnectionHeadway.Phase is not (LinkPhase.Unsupported or LinkPhase.Failed))
                {
                    if (!_blobVerifyDone && _blobInterrogationReceived)
                        TransmitPlayMsg(BitConverter.GetBytes(DddFinishOpcode), GameQueueGroup.DatabaseQueue);
                    _blobVerifyDone = true;
                }
                break;

            case ObjectCreation.Opcode:
                if (ObjectCreation.TryParse(corpus) is { } built)
                    EntitySpawned?.Invoke(ToActorSummon(built));
                break;
            case ObjectDeletion.Opcode:
                if (ObjectDeletion.TryParse(corpus) is { } deleted)
                    EntityDeleted?.Invoke(deleted);
                break;
            case PickupNotice.Opcode:
                if (PickupNotice.TryParse(corpus) is { } picked)
                    EntityPickedUp?.Invoke(picked);
                break;
            case AncestorSignal.Opcode:
                if (AncestorSignal.TryParse(corpus) is { } parented)
                    ParentUpdated?.Invoke(parented);
                break;
            case MotionUpdate.Opcode:
                if (MotionUpdate.TryParse(corpus) is { } locomotion)
                {
                    MotionUpdated?.Invoke(new MoverMotionUpdate(
                        locomotion.Guid, locomotion.MotionState, locomotion.InstanceSequence, locomotion.MovementSequence, locomotion.ServerControlSequence, locomotion.IsAutonomous));
                }
                break;
            case RefreshLocus.Opcode:
                if (RefreshLocus.TryParse(corpus) is { } placed)
                {
                    PositionUpdated?.Invoke(new MoverPositionUpdate(
                        placed.Guid, placed.Position, placed.Velocity, placed.PlacementId, placed.IsGrounded,
                        placed.InstanceSequence, placed.PositionSequence, placed.TeleportSequence, placed.ForcePositionSequence));
                }
                break;
            case VelocityUpdate.Opcode:
                if (VelocityUpdate.TryParse(corpus) is { } vel)
                    VectorUpdated?.Invoke(vel);
                break;
            case GroupPhase.Opcode:
                PrintSetPhaseOnce(corpus);
                if (GroupPhase.TryParse(corpus) is { } phase)
                    StateUpdated?.Invoke(phase);
                break;
            case ObjDescNotice.Opcode:
                OnLooks(corpus);
                break;
            case AvatarWarpOpcode:
                if (corpus.Length >= 6)
                    TeleportStarted?.Invoke(BinaryPrimitives.ReadUInt16LittleEndian(corpus.Slice(4, 2)));
                break;

            case SpeechLine.OwnOpcode:
            case SpeechLine.RangedOpcode:
                if (SpeechLine.TryParse(corpus) is { } speech)
                    SpeechHeard?.Invoke(speech);
                break;
            case EmoteLine.Opcode:
                if (EmoteLine.TryParse(corpus) is { } emote)
                    EmoteHeard?.Invoke(emote);
                break;
            case WireSoulEmote.Opcode:
                if (WireSoulEmote.TryParse(corpus) is { } soul)
                    SoulEmoteHeard?.Invoke(soul);
                break;
            case ServerLine.Opcode:
                if (ServerLine.TryParse(corpus) is { } stroke)
                    ServerMessageReceived?.Invoke(stroke);
                break;
            case PlayerDeath.Opcode:
                if (PlayerDeath.TryParse(corpus) is { } death)
                    PlayerKilledReceived?.Invoke(death);
                break;
            case TurbineComms.Opcode:
                if (TurbineComms.TryParse(corpus) is { } turbine)
                    TurbineChatReceived?.Invoke(turbine);
                break;

            case OwnVitalUpdate.WholeOpcode:
                if (OwnVitalUpdate.TryDecodeWhole(corpus) is { } vital)
                    VitalUpdated?.Invoke(vital);
                break;
            case OwnVitalUpdate.LatestOpcode:
                if (OwnVitalUpdate.TryDecodeLatest(corpus) is { } latest)
                    VitalCurrentUpdated?.Invoke(latest);
                break;
            case OwnAttributeUpdate.Opcode:
                if (OwnAttributeUpdate.TryParse(corpus) is { } attr)
                    AttributeUpdated?.Invoke(attr);
                break;
            case OwnSkillUpdate.Opcode:
                if (OwnSkillUpdate.TryParse(corpus) is { } aptitude)
                    SkillUpdated?.Invoke(aptitude);
                break;
            case SharedIntUpdate.Opcode:
                if (SharedIntUpdate.TryParse(corpus) is { } objectInt)
                    ObjectIntPropertyUpdated?.Invoke(new ObjectIntNotice(objectInt.Guid, objectInt.Property, objectInt.Value));
                break;
            case OwnIntUpdate.Opcode:
                if (OwnIntUpdate.TryParse(corpus) is { } avatarInt)
                    PlayerIntPropertyUpdated?.Invoke(new PlayerIntNotice(avatarInt.Property, avatarInt.Value));
                break;
            case OwnInt64Update.Opcode:
                if (OwnInt64Update.TryParse(corpus) is { } avatarInt64)
                    PlayerInt64PropertyUpdated?.Invoke(new PlayerInt64Notice(avatarInt64.Property, avatarInt64.Value));
                break;
            case StackSizeSet.Opcode:
                if (StackSizeSet.TryParse(corpus) is { } pile)
                    StackSizeUpdated?.Invoke(new StackSizeNotice(pile.Guid, pile.StackSize, pile.Value));
                break;
            case InventoryDrop.Opcode:
                if (InventoryDrop.TryParse(corpus) is { } dropped)
                    InventoryObjectRemoved?.Invoke(dropped.Guid);
                break;

            case GameEventParcel.Opcode:
                if (GameEventParcel.TryDecodeBorrowed(msg) is { } parcel)
                    PlaySignals.Relay(parcel);
                break;
            case AdminEnvironsOpcode:
                if (corpus.Length >= 8)
                    EnvironChanged?.Invoke(BinaryPrimitives.ReadUInt32LittleEndian(corpus.Slice(4, 4)));
                break;
            case PlayKineticsProgram.Opcode:
                if (PlayKineticsProgram.TryParse(corpus) is { } program)
                    PlayPhysicsScriptReceived?.Invoke(program);
                break;
            case PlayKineticsProgramKind.Opcode:
                if (PlayKineticsProgramKind.TryParse(corpus) is { } programKind)
                    PlayPhysicsScriptTypeReceived?.Invoke(programKind);
                break;
            case SfxSignal.Opcode:
                if (SfxSignal.TryParse(corpus) is { } sfx)
                    SoundEventReceived?.Invoke(sfx);
                break;

            default:
                // MACAC_DUMP_OPCODES=1: one line per genuinely unhandled opcode, first sighting only.
                if (PrintOpcodesTurnedOn && _unhandledObserved.Add(opcode))
                    Console.WriteLine($"opcodes: unhandled 0x{opcode:X4} (body.len={corpus.Length})");
                break;
        }
    }

    private delegate T BodyParser<T>(ReadOnlySpan<byte> body);

    // Management parsers throw on malformed input; a bad one must not poison the rest of the UIQueue,
    // so it is swallowed here
    private static bool Guarded<T>(BodyParser<T> decode, ReadOnlySpan<byte> corpus, out T decoded)
    {
        try
        {
            decoded = decode(corpus);
            return true;
        }
        catch
        {
            decoded = default!;
            return false;
        }
    }

    private void OnGenesisReply(ReadOnlySpan<byte> corpus)
    {
        var awaited = _genesisAwaiting;
        if (awaited == GenesisAwaiting.None)
        {
            if (!_warnedStrayGenesisReply)
            {
                _warnedStrayGenesisReply = true;
                Console.Error.WriteLine(
                    "[session] unexpected CharacterGenerationVerificationResponse "
                    + "(0xF643) with no outstanding create/restore request - dropped");
            }
            return;
        }
        _genesisAwaiting = GenesisAwaiting.None;

        if (awaited == GenesisAwaiting.Restore)
        {
            if (Guarded(CharacterRevive.Parse, corpus, out CharacterRevive.Parsed revived))
                CharacterRestoreReceived?.Invoke(revived);
        }
        else if (Guarded(GenesisVerdict.Parse, corpus, out GenesisVerdict.Parsed verdict))
        {
            CharacterCreateResponseReceived?.Invoke(verdict);
        }
    }

    private void OnLooks(ReadOnlySpan<byte> corpus)
    {
        if (ObjDescNotice.TryParse(corpus) is not { } decoded)
        {
            if (PrintLooksTurnedOn)
                Console.WriteLine($"appearance: 0xF625 PARSE FAILED body.len={corpus.Length}");
            return;
        }

        if (PrintLooksTurnedOn)
        {
            ObjectCreation.SchemeBlob data = decoded.ModelData;
            Console.WriteLine($"appearance: 0xF625 guid=0x{decoded.Guid:X8} basePal=0x{(data.BasePaletteId ?? 0):X8} subPals={data.SubPalettes.Count} texChanges={data.TextureChanges.Count} animParts={data.AnimPartChanges.Count}");
            foreach (var swap in data.SubPalettes)
                Console.WriteLine($"  SP id=0x{swap.SubPaletteId:X8} offset={swap.Offset} length={swap.Length}");
            foreach (var tc in data.TextureChanges)
                Console.WriteLine($"  TC part={tc.PartIndex:D2} oldTex=0x{tc.OldTexture:X8} -> newTex=0x{tc.NewTexture:X8}");
            foreach (var apc in data.AnimPartChanges)
                Console.WriteLine($"  APC part={apc.PartIndex:D2} -> gfx=0x{apc.NewModelId:X8}");
        }
        AppearanceUpdated?.Invoke(decoded);
    }

    private void PrintSetPhaseOnce(ReadOnlySpan<byte> corpus)
    {
        if (!MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeBuildingEnabled || _setPhaseHexDumped)
            return;
        _setPhaseHexDumped = true;
        int shown = Math.Min(corpus.Length, 32);
        string hex = string.Join(" ", corpus.Slice(0, shown).ToArray().Select(b => b.ToString("X2")));
        Console.WriteLine($"[setstate-hex] body.len={corpus.Length} first-{shown}-bytes: {hex}");
    }
}
