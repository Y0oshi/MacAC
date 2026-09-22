using System.Numerics;

namespace MacAC.Mechanics.Effects;

public sealed partial class MoteSys
{

    private void Advance(MoteSpout emitter)
    {
        for (int idx = 0; idx < emitter.Particles.Length; ++idx)
        {
            ref Mote particle = ref emitter.Particles[idx];
            if (!particle.Alive)
                continue;

            particle.Age = AgeOf(emitter, particle);
            if (particle.Lifespan <= 0f || particle.Age >= particle.Lifespan)
            {
                particle.Alive = false;
                emitter.ActiveCount--;
                continue;
            }

            particle.Position = Place(emitter, particle);
            float life = Math.Clamp(particle.Age / particle.Lifespan, 0f, 1f);
            particle.Size = Lerp(particle.BeginSize, particle.FinishSize, life);
            particle.Rotation = Lerp(emitter.Desc.BeginSpin, emitter.Desc.FinishSpin, life);
            particle.ColorArgb = Argb(Lerp(particle.StartAlpha, particle.EndAlpha, life), emitter.Desc.BeginTintArgb, emitter.Desc.FinishTintArgb, life);
        }

        if (emitter.Finished || _clock < emitter.StartedAt + emitter.Desc.BeginDelay)
            return;

        while (WantsToEmit(emitter))
        {
            if (!Birth(emitter, evictWhenWhole: false))
                break;
        }

        // A rate-driven emitter (no birthrate) accumulates fractional births
        if (emitter.Desc.Birthrate <= 0f && emitter.Desc.EmitRate > 0f)
        {
            emitter.EmittedAccumulator += (_clock - emitter.PreviousEmitMoment) * emitter.Desc.EmitRate;
            emitter.PreviousEmitMoment = _clock;
            while (emitter.EmittedAccumulator >= 1f)
            {
                emitter.EmittedAccumulator -= 1f;
                if (!Birth(emitter, evictWhenWhole: false))
                    break;
            }
        }
    }

    private void ProgressDegraded(MoteSpout emitter, float dt)
    {
        if (emitter.Desc.TotalParticles is 0 && emitter.Desc.SumInterval == 0f)
        {
            emitter.FrozenMoment += dt;
            if (emitter.Desc.Birthrate <= 0f && emitter.Desc.EmitRate > 0f)
            {
                emitter.PreviousEmitMoment = _clock;
                emitter.EmittedAccumulator = 0f;
            }
            return;
        }

        for (int idx = 0; idx < emitter.Particles.Length; ++idx)
        {
            ref Mote particle = ref emitter.Particles[idx];
            if (!particle.Alive)
                continue;
            particle.Age = _clock - particle.SpawnedAt;
            if (particle.Lifespan <= 0f || particle.Age >= particle.Lifespan)
            {
                particle.Alive = false;
                emitter.ActiveCount--;
            }
        }

        if (!emitter.Finished && WantsToEmit(emitter))
        {
            emitter.ActiveCount++;
            emitter.SumEmitted++;
            emitter.PreviousEmitMoment = _clock;
            emitter.PreviousEmitShift = emitter.MooringSpot;
        }
    }

    private bool WantsToEmit(MoteSpout emitter)
    {
        EmitterSpec spec = emitter.Desc;
        if (spec.TotalParticles > 0 && emitter.SumEmitted >= spec.TotalParticles)
            return false;
        if (emitter.ActiveCount >= spec.MaxParticles || spec.Birthrate <= 0f)
            return false;
        return spec.SpoutSort switch
        {
            EmitterSort.BirthratePerSec => _clock - emitter.PreviousEmitMoment > spec.Birthrate,
            EmitterSort.BirthratePerMeter => Vector3.DistanceSquared(emitter.MooringSpot, emitter.PreviousEmitShift) > spec.Birthrate * spec.Birthrate,
            _ => false,
        };
    }

    // Spawns one particle; random draws happen in a fixed order so seeded runs replay
    private bool Birth(MoteSpout emitter, bool evictWhenWhole)
    {
        int socket = ReleaseSocket(emitter);
        if (socket < 0 && evictWhenWhole)
            socket = OldestSocket(emitter);
        if (socket < 0)
            return false;

        EmitterSpec spec = emitter.Desc;
        ref Mote particle = ref emitter.Particles[socket];
        bool evicting = particle.Alive;
        particle = default;
        particle.Alive = true;
        particle.SpawnedAt = _clock;
        particle.FrozenMomentAtSummon = emitter.FrozenMoment;
        particle.Lifespan = RollLifespan(spec);
        particle.EmissionOrigin = emitter.MooringSpot;
        particle.SummonSpin = emitter.MooringRot;

        Vector3 shift = RollShift(spec);
        Vector3 a = RollVector(spec.A, spec.MinA, spec.MaxA);
        Vector3 b = RollVector(spec.B, spec.MinB, spec.MaxB);
        Vector3 c = RollVector(spec.C, spec.MinC, spec.MaxC);

        if (a == Vector3.Zero && spec.StartingVel != Vector3.Zero)
        {
            a = spec.StartingVel;
            if (spec.VelJitter > 0f)
                a += new Vector3(RollCentered(spec.VelJitter), RollCentered(spec.VelJitter), RollCentered(spec.VelJitter));
        }
        if (b == Vector3.Zero && spec.Gravity != Vector3.Zero)
            b = spec.Gravity;

        Seed(emitter, ref particle, shift, a, b, c);

        particle.Velocity = particle.A;
        particle.BeginSize = RollScaling(spec.BeginDims, spec.ScaleRand);
        particle.FinishSize = RollScaling(spec.FinishDims, spec.ScaleRand);
        particle.StartAlpha = RollAlpha(spec.BeginAlpha, spec.TransRand);
        particle.EndAlpha = RollAlpha(spec.FinishAlpha, spec.TransRand);
        particle.Size = particle.BeginSize;
        particle.ColorArgb = Argb(particle.StartAlpha, spec.BeginTintArgb, spec.FinishTintArgb, 0f);
        particle.Position = Place(emitter, particle);

        emitter.SumEmitted++;
        if (!evicting)
            emitter.ActiveCount++;
        emitter.PreviousEmitMoment = _clock;
        emitter.PreviousEmitShift = emitter.MooringSpot;
        return true;
    }

    // Which of the A/B/C vectors live in the spawn frame depends on the particle type
    private void Seed(MoteSpout emitter, ref Mote particle, Vector3 shift, Vector3 a, Vector3 b, Vector3 c)
    {
        particle.Offset = Spin(emitter, shift);
        particle.A = a;
        particle.B = b;
        particle.C = c;

        switch (emitter.Desc.Type)
        {
            case MoteKind.LocalVelocity:
            case MoteKind.ParabolicLVGA:
            case MoteKind.Swarm:
                particle.A = Spin(emitter, a);
                break;
            case MoteKind.ParabolicLVLA:
                particle.A = Spin(emitter, a);
                particle.B = Spin(emitter, b);
                break;
            case MoteKind.ParabolicLVGAGR:
                particle.A = Spin(emitter, a);
                particle.C = c;
                break;
            case MoteKind.ParabolicLVLALR:
                particle.A = Spin(emitter, a);
                particle.B = Spin(emitter, b);
                particle.C = Spin(emitter, c);
                break;
            case MoteKind.Explode:
                particle.A = a;
                particle.B = b;
                particle.C = RollExplodeDir(c);
                break;
            case MoteKind.Implode:
                particle.A = a;
                particle.B = b;
                particle.Offset = new Vector3(particle.Offset.X * c.X, particle.Offset.Y * c.Y, particle.Offset.Z * c.Z);
                particle.C = particle.Offset;
                break;
            case MoteKind.ParabolicGVGAGR:
                particle.C = c;
                break;
        }
    }

    private float AgeOf(MoteSpout emitter, in Mote particle)
    {
        return _clock - particle.SpawnedAt - (emitter.FrozenMoment - particle.FrozenMomentAtSummon);
    }

    private static Vector3 Place(MoteSpout emitter, Mote particle)
    {
        float t = particle.Age;
        Vector3 origin = (emitter.Desc.Flags & EmitterBits.AttachLocal) != 0 ? emitter.MooringSpot : particle.EmissionOrigin;
        Vector3 at = origin + particle.Offset;
        Vector3 a = particle.A;
        Vector3 b = particle.B;
        Vector3 c = particle.C;

        return emitter.Desc.Type switch
        {
            MoteKind.Still => at,
            MoteKind.LocalVelocity or MoteKind.GlobalVelocity => at + t * a,
            MoteKind.ParabolicLVGA or MoteKind.ParabolicLVLA or MoteKind.ParabolicGVGA
                or MoteKind.ParabolicLVGAGR or MoteKind.ParabolicLVLALR or MoteKind.ParabolicGVGAGR =>
                at + t * a + 0.5f * t * t * b,
            MoteKind.Swarm =>
                at + t * a + new Vector3(MathF.Cos(t * b.X) * c.X, MathF.Sin(t * b.Y) * c.Y, MathF.Cos(t * b.Z) * c.Z),
            MoteKind.Explode =>
                at + new Vector3((t * b.X + c.X * a.X) * t, (t * b.Y + c.Y * a.X) * t, (t * b.Z + c.Z * a.X + a.Z) * t),
            MoteKind.Implode => at + MathF.Cos(a.X * t) * c + t * t * b,
            _ => at + t * a,
        };
    }

    private static Vector3 Spin(MoteSpout emitter, Vector3 v)
    {
        return emitter.MooringRot == Quaternion.Identity ? v : Vector3.Transform(v, emitter.MooringRot);
    }

    private static int ReleaseSocket(MoteSpout emitter)
    {
        for (int idx = 0; idx < emitter.Particles.Length; ++idx)
        {
            if (!emitter.Particles[idx].Alive)
                return idx;
        }
        return -1;
    }

    private static int OldestSocket(MoteSpout emitter)
    {
        int socket = -1;
        float finest = -1f;
        for (int idx = 0; idx < emitter.Particles.Length; ++idx)
        {
            ref Mote particle = ref emitter.Particles[idx];
            float spent = particle.Lifespan > 0f ? particle.Age / particle.Lifespan : 1f;
            if (spent > finest)
            {
                finest = spent;
                socket = idx;
            }
        }
        return socket;
    }

    private float RollLifespan(EmitterSpec spec)
    {
        float middle = spec.Lifespan > 0f ? spec.Lifespan : (spec.LifespanLower + spec.LifespanUpper) * 0.5f;
        float spread = spec.LifespanRand > 0f ? spec.LifespanRand : MathF.Abs(spec.LifespanUpper - spec.LifespanLower) * 0.5f;
        float life = middle + RollCentered(spread);
        if (life <= 0f && spec.LifespanUpper > 0f)
            life = Lerp(spec.LifespanLower, spec.LifespanUpper, Roll());
        return MathF.Max(0f, life);
    }

    private Vector3 RollShift(EmitterSpec spec)
    {
        float lower = MathF.Min(spec.MinOffset, spec.MaxOffset);
        float upper = MathF.Max(spec.MinOffset, spec.MaxOffset);
        if (upper <= 0f)
            return Vector3.Zero;

        Vector3 axis = StandardizeOrZero(spec.OffsetDir);
        var v = new Vector3(RollCentered(1f), RollCentered(1f), RollCentered(1f));
        if (axis != Vector3.Zero)
            v -= axis * Vector3.Dot(v, axis);

        v = v.LengthSquared() < 1e-8f
            ? (axis != Vector3.Zero ? Perpendicular(axis) : Vector3.UnitX)
            : Vector3.Normalize(v);
        return v * Lerp(lower, upper, Roll());
    }

    private Vector3 RollVector(Vector3 dir, float lower, float upper)
    {
        if (dir == Vector3.Zero)
            return Vector3.Zero;
        if (upper < lower)
            (lower, upper) = (upper, lower);
        return dir * Lerp(lower, upper, Roll());
    }

    private Vector3 RollExplodeDir(Vector3 c)
    {
        float yaw = Lerp(-MathF.PI, MathF.PI, Roll());
        float pitch = Lerp(-MathF.PI, MathF.PI, Roll());
        float cosPitch = MathF.Cos(pitch);
        Vector3 direction = new Vector3(MathF.Cos(yaw) * c.X * cosPitch, MathF.Sin(yaw) * c.Y * cosPitch, MathF.Sin(pitch) * c.Z);
        float len = direction.Length();
        return len < 1e-8f ? Vector3.Zero : direction / len;
    }

    private float RollScaling(float val, float spread) => Math.Clamp(val + RollCentered(spread), 0.1f, 10f);

    private float RollAlpha(float val, float spread) => Math.Clamp(val + RollCentered(spread), 0f, 1f);

    private float RollCentered(float halfWidth) => (Roll() - 0.5f) * 2f * halfWidth;

    private float Roll() => (float)_rng.NextDouble();

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static Vector3 StandardizeOrZero(Vector3 v) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.Zero;

    private static Vector3 Perpendicular(Vector3 v)
    {
        return Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
    }

    private static uint Argb(float alpha, uint beginArgb, uint finishArgb, float t)
    {
        byte Lane(int shift) =>
            (byte)Math.Clamp(((beginArgb >> shift) & 0xFF) + (((finishArgb >> shift) & 0xFF) - (float)((beginArgb >> shift) & 0xFF)) * t, 0f, 255f);
        byte r = Lane(16);
        byte g = Lane(8);
        byte b = Lane(0);
        byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}
