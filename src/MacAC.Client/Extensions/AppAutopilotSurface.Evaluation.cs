using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim;
using MacAC.Sim.Actors;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{

    public CastGate EvaluateGate(uint arcanumIdent)
    {
        SimArcanaCastLedger? casting;
        Grimoire? grimoire;
        lock (_latch)
        {
            casting = _casting;
            grimoire = _grimoire;
        }
        if (casting is null || grimoire is null || !IsAvailable)
            return CastGate.Unavailable;
        if (!grimoire.Knows(arcanumIdent))
            return CastGate.NotKnown;
        return IsCasting
            ? CastGate.Busy
            : casting.EvaluateCastingLatch(arcanumIdent) switch
            {
                ArcanaCastTurnstile.Unknown => CastGate.NotKnown,
                ArcanaCastTurnstile.NoTargetNeeded => CastGate.Ready,
                ArcanaCastTurnstile.TargetCompatible => CastGate.Ready,
                ArcanaCastTurnstile.NoTargetSelected => CastGate.NoTargetSelected,
                ArcanaCastTurnstile.TargetIncompatible => CastGate.TargetIncompatible,
                _ => CastGate.Refused,
            };
    }

    public CastGate EvaluateGate(uint arcanumIdent, uint markObjectIdent)
    {
        return !PickExplicitMark(markObjectIdent) ? CastGate.Refused : EvaluateGate(arcanumIdent);
    }

    ProjectilePathVerdict IProjectileControls.EvaluatePath(
        uint markObjectIdent,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float missileRadius,
        float hopGap,
        int ceilingImpactChecks)
    {
        return EvaluateMissileTrailReq(
            markObjectIdent,
            sort,
            markHeight,
            missileRadius,
            hopGap,
            ceilingImpactChecks,
            grabTelemetry: false);
    }

    ProjectilePathVerdict IProjectileControls.EvaluatePathWithDiagnostics(
        uint markObjectIdent,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float missileRadius,
        float hopGap,
        int ceilingImpactChecks)
    {
        return EvaluateMissileTrailReq(
            markObjectIdent,
            sort,
            markHeight,
            missileRadius,
            hopGap,
            ceilingImpactChecks,
            grabTelemetry: true);
    }

    private ProjectilePathVerdict EvaluateMissileTrailReq(
        uint markObjectIdent,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float missileRadius,
        float hopGap,
        int ceilingImpactChecks,
        bool grabTelemetry)
    {
        SimCore? core;
        KineticEngine? kinetics;
        lock (_latch)
        {
            core = _runtime;
            kinetics = _missileKinetics;
        }
        if (core is null || kinetics is null || !IsAvailable)
            return new(ProjectilePathOutcome.Unavailable);
        if (markObjectIdent is 0u
            || !float.IsFinite(missileRadius)
            || missileRadius <= 0f
            || !float.IsFinite(hopGap)
            || hopGap <= 0f
            || ceilingImpactChecks <= 0)

            return new(ProjectilePathOutcome.InvalidTarget);

        uint ownIdent = core.AvatarIdentity.ServerGuid;
        if (!core.EntityObjects.Entities.TryFetchEngaged(
                ownIdent,
                out SimActorRecord own)
            || !core.EntityObjects.Entities.TryFetchEngaged(
                markObjectIdent,
                out SimActorRecord mark)
            || own.KineticBody is not { } ownCorpus
            || mark.KineticBody is not { } markCorpus
            || ownCorpus.CellPosition.ObjCellId is 0u)

            return new(ProjectilePathOutcome.InvalidTarget);

        try
        {
            return EvaluateMissileTrail(
                kinetics,
                ownIdent,
                ownCorpus,
                markObjectIdent,
                markCorpus,
                sort,
                markHeight,
                missileRadius,
                hopGap,
                ceilingImpactChecks,
                grabTelemetry);
        }
        catch (Exception problem)
        {
            return new(
                ProjectilePathOutcome.Error,
                Notice: problem.GetBaseException().Message);
        }
    }

    private static ProjectilePathVerdict EvaluateMissileTrail(
        KineticEngine kinetics,
        uint ownObjectIdent,
        KineticBody own,
        uint markObjectIdent,
        KineticBody mark,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float radius,
        float hopGap,
        int ceilingChecks,
        bool grabTelemetry)
    {
        var baseDiff = mark.Position - own.Position;
        var horizontal = new System.Numerics.Vector2(baseDiff.X, baseDiff.Y);
        float horizontalGap = horizontal.Length();
        if (!float.IsFinite(horizontalGap)
            || horizontalGap <= KineticConstants.EPSILON)

            return new(ProjectilePathOutcome.InvalidTarget);

        var dir = horizontal / horizontalGap;
        float srcAhead = sort switch
        {
            ProjectilePathKind.Arc => 0.44f,
            ProjectilePathKind.Missile => 0.61f,
            _ => 0.66f,
        };
        float srcHeight = sort == ProjectilePathKind.Arc ? 1.8f : 1.2f;
        float markHeightMeters = markHeight switch
        {
            StrikeHeight.Low => 0.3f,
            StrikeHeight.High => 1.5f,
            _ => 0.9f,
        };
        var latest = own.Position + new System.Numerics.Vector3(
            dir.X * srcAhead,
            dir.Y * srcAhead,
            srcHeight);
        System.Numerics.Vector3 dest = mark.Position + new System.Numerics.Vector3(
            0f,
            0f,
            markHeightMeters);
        var diff = dest - latest;
        horizontal = new System.Numerics.Vector2(diff.X, diff.Y);
        horizontalGap = horizontal.Length();
        if (horizontalGap <= KineticConstants.EPSILON)
            return new(ProjectilePathOutcome.Clear);
        dir = horizontal / horizontalGap;

        float pace = sort switch
        {
            ProjectilePathKind.Arc => 37.5185f,
            ProjectilePathKind.Missile => 46f,
            _ => 100f,
        };
        float sumMoment = horizontalGap / pace;
        float verticalPace = sort == ProjectilePathKind.Straight
            ? diff.Z / sumMoment
            : (diff.Z + 4.9f * sumMoment * sumMoment) / sumMoment;
        System.Numerics.Vector3 vel = new System.Numerics.Vector3(
            dir.X * pace,
            dir.Y * pace,
            verticalPace);
        float passed = 0f;
        uint chamberIdent = own.CellPosition.ObjCellId;
        KineticBody sensorCorpus = new KineticBody
        {
            State = KineticStateFlags.Missile
                | KineticStateFlags.Inelastic
                | KineticStateFlags.ReportCollisions,
        };
        List<ProjectileTraceSample>? diagSpecimens = grabTelemetry
            ? new List<ProjectileTraceSample>(
                Math.Min(ceilingChecks, 512))
            : null;

        for (int verify = 1; verify <= ceilingChecks; ++verify)
        {
            float leftover = MathF.Max(0f, sumMoment - passed);
            if (leftover <= KineticConstants.EPSILON)
            {
                return WithMissileDiagSpecimens(
                    new(ProjectilePathOutcome.Clear, verify - 1),
                    diagSpecimens);
            }
            float velMagnitude = vel.Length();
            if (!float.IsFinite(velMagnitude)
                || velMagnitude <= KineticConstants.EPSILON)
            {
                return WithMissileDiagSpecimens(
                    new(
                        ProjectilePathOutcome.Error,
                        verify - 1,
                        Notice: "The projectile trajectory became invalid."),
                    diagSpecimens);
            }
            float quantum = MathF.Min(leftover, hopGap / velMagnitude);
            var upcoming = latest + vel * quantum;
            if (quantum >= leftover - KineticConstants.EPSILON)
                upcoming = dest;

            var settled = kinetics.ResolveWithTransition(
                latest,
                upcoming,
                chamberIdent,
                radius,
                orbHeight: 0f,
                hopUpHeight: 0f,
                hopDownHeight: 0f,
                isOnTerrain: false,
                corpus: sensorCorpus,
                carrierFlagSet: MoverState.PathClipped,
                movingActorIdent: ownObjectIdent,
                ownOrbOrigin: System.Numerics.Vector3.Zero,
                designatedMarkIdent: markObjectIdent);
            float askedGap = System.Numerics.Vector3.Distance(latest, upcoming);
            float deliveredGap = System.Numerics.Vector3.Distance(
                latest,
                settled.Position);
            bool stopped = !settled.Ok
                || settled.CollidedWithEnvironment
                || settled.PreviousCollidedObjectIdent is not 0u
                || settled.CollisionNormalValid
                || deliveredGap + 0.01f < askedGap;
            bool markStrike = settled.PreviousCollidedObjectIdent == markObjectIdent;
            diagSpecimens?.Add(new ProjectileTraceSample(
                settled.Position,
                markStrike || !stopped,
                radius));
            if (markStrike)
            {
                return WithMissileDiagSpecimens(
                    new(
                        ProjectilePathOutcome.Clear,
                        verify,
                        markObjectIdent),
                    diagSpecimens);
            }
            if (stopped)
            {
                return WithMissileDiagSpecimens(
                    new(
                        ProjectilePathOutcome.Blocked,
                        verify,
                        settled.PreviousCollidedObjectIdent),
                    diagSpecimens);
            }

            latest = settled.Position;
            chamberIdent = settled.CellId;
            passed += quantum;
            if (sort != ProjectilePathKind.Straight)
                vel.Z -= 9.8f * quantum;
        }

        return WithMissileDiagSpecimens(
            new(
                ProjectilePathOutcome.BudgetExceeded,
                ceilingChecks,
                Notice: "The projectile collision-check budget was exhausted."),
            diagSpecimens);
    }
}
