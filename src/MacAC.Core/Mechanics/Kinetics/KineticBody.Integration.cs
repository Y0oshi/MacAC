using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>The retail integrator: acceleration, friction, and the fixed-quantum update loop.</summary>
public sealed partial class KineticBody
{
    private const float SlowSledPaceSq = 1.5625f;      // 1.25²
    private const float FastSledPaceSq = 6.25f;        // 2.5²
    private const float PlanarTerrainCosine = 0.98480775f; // cos 10°

    public void calc_acceleration()
    {
        bool grounded = InContact && OnWalkable && (State & KineticStateFlags.Sledding) == 0;
        if (grounded)
        {
            Acceleration = Vector3.Zero;
            Omega = Vector3.Zero;
            return;
        }
        Acceleration = HasGravity ? new Vector3(0f, 0f, Gravity) : Vector3.Zero;
    }

    public void set_velocity(Vector3 newVel)
    {
        Velocity = newVel;
        if (Velocity.LengthSquared() > UpperVelSquared)
            Velocity = Vector3.Normalize(Velocity) * MaxVelocity;

        // Set Active flag (bit 7 of TransientState, offset +0xAC)
        TransientState |= TransientPhaseFlagSet.Active;
    }

    public void set_local_velocity(Vector3 ownVel, bool autonomous = false)
    {
        PreviousRelocateWasAutonomous = autonomous;
        set_velocity(Vector3.Transform(ownVel, Orientation));
    }

    public void set_on_walkable(bool isOnPassable)
    {
        if (isOnPassable)
            TransientState |= TransientPhaseFlagSet.OnWalkable;
        else
            TransientState &= ~TransientPhaseFlagSet.OnWalkable;
        calc_acceleration();
    }

    public void calc_friction(float dt, float velMag2)
    {
        if (!OnWalkable)
            return;

        float intoTerrain = Vector3.Dot(Velocity, GroundNormal);
        if (intoTerrain >= 0.25f)
            return;

        Velocity -= intoTerrain * GroundNormal;

        float friction = Friction;
        if ((State & KineticStateFlags.Sledding) != 0)
        {
            if (velMag2 < SlowSledPaceSq)
                friction = 1.0f;
            else if (velMag2 >= FastSledPaceSq && GroundNormal.Z > PlanarTerrainCosine)
                friction = 0.2f;
        }

        Velocity *= MathF.Pow(1.0f - friction, dt);
    }

    public void RefreshKineticsInternal(float dt)
    {
        float velMag2 = Velocity.LengthSquared();

        if (velMag2 <= 0f)
        {
            if (OnWalkable)
                TransientState &= ~TransientPhaseFlagSet.Active;
        }
        else
        {
            if (velMag2 > UpperVelSquared)
            {
                Velocity = Vector3.Normalize(Velocity) * MaxVelocity;
                velMag2 = UpperVelSquared;
            }

            calc_friction(dt, velMag2);

            if (velMag2 - SmallVelSquared < 0.0002f)
                Velocity = Vector3.Zero;

            Position += Velocity * dt + Acceleration * (0.5f * dt * dt);
        }

        Velocity += Acceleration * dt;

        Vector3 spin = Omega * dt;
        float spinLengthSq = spin.LengthSquared();
        if (spinLengthSq >= KineticConstants.EpsilonSq)
        {
            float angle = MathF.Sqrt(spinLengthSq);
            Quaternion pivot = Quaternion.CreateFromAxisAngle(spin / angle, angle);
            Orientation = Quaternion.Normalize(Quaternion.Multiply(pivot, Orientation));
        }
    }

    public void update_object(double latestMoment)
    {
        double owed = latestMoment - PreviousRefreshMoment;
        if (owed <= KineticConstants.EPSILON || owed > HugeQuantum)
        {
            PreviousRefreshMoment = latestMoment;
            return;
        }

        double simulated = PreviousRefreshMoment;
        while (owed > MaxQuantum)
        {
            simulated += MaxQuantum;
            update_objectLoop(MaxQuantum);
            owed -= MaxQuantum;
        }

        if (owed > LowerQuantum)
        {
            simulated += owed;
            update_objectLoop((float)owed);
        }

        PreviousRefreshMoment = simulated;
    }

    private void update_objectLoop(float dt)
    {
        calc_acceleration();
        RefreshKineticsInternal(dt);
    }
}
