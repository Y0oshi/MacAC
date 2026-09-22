using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal readonly record struct SimPeerHullAssemblyStub(
    uint MotionTableId,
    bool MotionTableGatePassed,
    bool MovementBranch,
    bool LastMoveWasAutonomous,
    bool PlacementFrameStaged,
    float Friction,
    bool FrictionApplied,
    float Elasticity,
    float TranslucencyOriginal,
    bool TranslucencyApplied,
    bool VelocityApplied,
    bool OmegaApplied);

internal static class SimPeerHullBlueprint
{
    private const float DefaultDscFriction = 0.95f;
    private const float DefaultDscElasticity = 0.05f;
    private const float UpperElasticity = 0.1f;

    internal static KineticBody Construct(
        SimActorRecord capture,
        KineticSpawnData? blurb,
        in SimSetPositionDirective readiedDirective,
        out SimPeerHullAssemblyStub receipt)
    {
        ArgumentNullException.ThrowIfNull(capture);
        KineticBody hull = new KineticBody();

        uint locomotionChartIdent = blurb?.MotionTableId ?? 0u;
        const bool locomotionChartLatchPassed = true;

        bool hasTravel = blurb?.Movement is { } travel && !travel.RawData.IsEmpty;
        bool autonomous = false;
        bool cycleLined = false;
        if (hasTravel)
        {
            autonomous = blurb!.Value.Movement!.Value.IsAutonomous ?? false;
            hull.PreviousRelocateWasAutonomous = autonomous;
        }
        else
        {
            hull.Orientation = readiedDirective.Physics.Orientation;
            hull.JunctureDormantChamberCycle(readiedDirective.Physics.CellId, readiedDirective.Physics.Position, readiedDirective.Physics.CellLocalPosition);
            cycleLined = true;
        }

        hull.State = capture.FinalKineticsCondition;

        float friction = blurb?.Friction ?? DefaultDscFriction;
        bool frictionImposed = friction is >= 0.0f and <= 1.0f;
        if (frictionImposed)
            hull.Friction = friction;

        float elasticity = blurb?.Elasticity ?? DefaultDscElasticity;
        hull.Elasticity = !(elasticity >= 0f) ? 0f : Math.Min(elasticity, UpperElasticity);

        float seeThrough = blurb?.Translucency ?? 0f;

        bool velImposed = blurb?.Velocity is { } vel && Finite(vel);
        if (velImposed)
            hull.set_velocity(blurb!.Value.Velocity!.Value);

        bool omegaImposed = blurb?.AngularVelocity is { } omega && Finite(omega);
        if (omegaImposed)
            hull.Omega = blurb!.Value.AngularVelocity!.Value;

        receipt = new SimPeerHullAssemblyStub(
            locomotionChartIdent,
            locomotionChartLatchPassed,
            hasTravel,
            autonomous,
            cycleLined,
            hull.Friction,
            frictionImposed,
            hull.Elasticity,
            seeThrough,
            seeThrough != 0.0f,
            velImposed,
            omegaImposed);
        return hull;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
