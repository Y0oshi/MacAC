using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public sealed class MoverFacts
{
    public MoverState State;
    public float StepUpHeight = 0.01f;  // KineticConstants.DefaultStepHeight
    public float StepDownHeight = 0.04f;
    public bool Ethereal;
    public bool StepDown = true;
    public float Scale = 1.0f;

    public KineticStateFlags MoverPhysicsState;

    public uint TargetId;

    public uint SelfEntityId;

    public bool MoverHasGravity;

    public bool Contact => (State & MoverState.Contact) != 0;
    public bool OnWalkable => (State & MoverState.OnWalkable) != 0;
    public bool IsViewer => (State & MoverState.IsViewer) != 0;
    public bool IsPlayer => (State & MoverState.IsPlayer) != 0;
    public bool EdgeSlide => (State & MoverState.EdgeSlide) != 0;
    public bool PathClipped => (State & MoverState.PathClipped) != 0;
    public bool ReleaseSpin => (State & MoverState.FreeRotate) != 0;
    public bool CanBypassRelocateRestrictions => (State & MoverState.CanBypassMoveRestrictions) != 0;

    public bool MissileIgnore(
        uint markActorIdent,
        uint markKineticsPhase,
        ActorImpactFlagSet markFlagSet)
    {
        KineticStateFlags markPhase = (KineticStateFlags)markKineticsPhase;
        if ((markPhase & KineticStateFlags.Missile) != 0)
            return true;
        if ((MoverPhysicsState & KineticStateFlags.Missile) == 0)
            return false;
        if (markActorIdent == TargetId)
            return false;

        bool hasWeenie = (markFlagSet & ActorImpactFlagSet.HasWeenie) != 0;
        if ((markPhase & KineticStateFlags.Ethereal) != 0 && hasWeenie)
            return true;

        return TargetId is not 0
            && hasWeenie
            && (markFlagSet & ActorImpactFlagSet.IsCreature) != 0;
    }

    public ShiftVerdict VerifyListingRestrictions(
        uint chamberRestrictionObjRef,
        ClientThingChart? objects)
    {
        if (!IsPlayer) return ShiftVerdict.OK;               // NPCs/props bypass entirely
        if (CanBypassRelocateRestrictions) return ShiftVerdict.OK;
        if (chamberRestrictionObjRef is 0) return ShiftVerdict.OK;

        var restrictionObject = objects?.Get(chamberRestrictionObjRef);
        if (restrictionObject is null) return ShiftVerdict.Collided;

        uint carrierIdent = SelfEntityId;
        uint houseHolderIdent = restrictionObject.HouseHolderIdent ?? 0;
        if (houseHolderIdent is 0 || houseHolderIdent == carrierIdent) return ShiftVerdict.OK;

        var restrictions = restrictionObject.Restrictions;
        if (restrictions is null) return ShiftVerdict.OK;

        uint carrierMonarchIdent = objects?.Get(carrierIdent)?.MonarchIdent ?? 0;
        return restrictions.IsAllowedIn(carrierIdent, carrierMonarchIdent)
            ? ShiftVerdict.OK
            : ShiftVerdict.Collided;
    }

    public float FetchPassableZ()
        => OnWalkable ? KineticConstants.FloorZ : KineticConstants.LandingZ;

    public bool VelocityKilled;

    public void StopVelocity() => VelocityKilled = true;

    public void RewindForReuse()
    {
        State = MoverState.None;
        StepUpHeight = KineticConstants.DefaultHopHeight;
        StepDownHeight = 0.04f;
        Ethereal = false;
        StepDown = true;
        Scale = 1.0f;
        MoverPhysicsState = KineticStateFlags.None;
        TargetId = 0;
        SelfEntityId = 0;
        MoverHasGravity = false;
        VelocityKilled = false;
    }

    internal void DuplicateFrom(MoverFacts src)
    {
        ArgumentNullException.ThrowIfNull(src);
        State = src.State;
        StepUpHeight = src.StepUpHeight;
        StepDownHeight = src.StepDownHeight;
        Ethereal = src.Ethereal;
        StepDown = src.StepDown;
        Scale = src.Scale;
        MoverPhysicsState = src.MoverPhysicsState;
        TargetId = src.TargetId;
        SelfEntityId = src.SelfEntityId;
        MoverHasGravity = src.MoverHasGravity;
        VelocityKilled = src.VelocityKilled;
    }
}
