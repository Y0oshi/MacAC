using System.Collections.Immutable;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Actors;

internal readonly record struct HeldAnchorCreate(
    ulong AdmissionId,
    RealmSession.MoverSpawn Spawn,
    bool IsLocalPlayer)
{
    internal bool IsValid
    {
        get
        {
            return AdmissionId is not 0UL
        && Spawn.Guid is not 0u;
        }
    }
}

internal readonly record struct HeldGrantedAnchorRelation(
    ulong AdmissionId,
    uint ChildGuid,
    SimActorKey ChildKey,
    AncestorSignal.Parsed? Standalone,
    CreateAnchorUpdate? Envelope,
    GrantedKineticsTimestamps AcceptedTimestamps)
{
    internal bool IsValid
    {
        get
        {
            return AdmissionId is not 0UL
        && ChildGuid is not 0u
        && ChildKey.LocalEntityId is not 0u
        && (Standalone.HasValue ^ Envelope.HasValue);
        }
    }

    internal ushort? AncestorInstSeries => Standalone?.ParentInstanceSequence;
}

internal enum HeldRerunBucketKind : byte
{
    Creates,
    AcceptedRelations,
}

internal readonly record struct HeldRerunWindowTicket(
    ulong Id,
    uint ParentGuid,
    HeldRerunBucketKind Kind)
{
    internal bool IsValid => Id is not 0UL;
}

public readonly record struct AnchorAttachmentRelation(
    uint ParentGuid,
    uint ChildGuid,
    uint ParentLocation,
    uint PlacementId,
    ushort ParentInstanceSequence,
    ushort ChildPositionSequence,
    AnchorAttachmentWaitHolder WaitOwner = AnchorAttachmentWaitHolder.Unknown);

public enum AnchorAttachmentWaitHolder
{
    Unknown,
    Parent,
    Child,
}

public enum AnchorMirrorCandidateKind
{
    Staged,
    Recovery,
}
