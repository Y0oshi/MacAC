using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public interface IAnimReader
{
    MotionClip? PullAnim(uint ident);
}

/// <summary>One part's pose for a sampled frame.</summary>
public readonly struct PieceTransform(Vector3 origin, Quaternion facing)
{
    public readonly Vector3 Origin = origin;
    public readonly Quaternion Facing = facing;
}

public sealed class AnimSequencer
{
    private const float BackwardsFactor = 0.65f;

    private static readonly AnimationDoneCue DoneSentinel = new() { Direction = CueDirection.Both };

    private readonly RigSpec _rig;
    private readonly AnimTrack _track;
    private readonly LocomotionPhase _phase;
    private readonly MotionTableKeeper _keeper;
    private readonly List<Cue> _taps = [];
    private readonly PieceTransform[] _postureTemp;
    private readonly PoseWindow _postureLens;
    private bool _initialized;

    public AnimSequencer(RigSpec rig, MotionBook locomotionChart, IAnimReader fetcher)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(locomotionChart);
        ArgumentNullException.ThrowIfNull(fetcher);

        _rig = rig;
        _postureTemp = new PieceTransform[rig.PartIds.Count];
        _postureLens = new PoseWindow(_postureTemp);
        _track = new AnimTrack(fetcher) { TapObjRef = new HookCollector(this) };
        _phase = new LocomotionPhase();
        _keeper = new MotionTableKeeper(new MotionTableRecord(locomotionChart), _phase, _track, new DoneRelay(this));

        BootstrapRigDefaultAnim((uint)rig.DefaultClipId);
    }

    public uint LatestStyling => _phase.Style;

    public uint CurrentMotion => _phase.Substate;

    public float CurrentSpeedMod => _phase.SubstateMod;

    public Vector3 LatestVel => _track.Velocity;

    public Vector3 LatestOmega => _track.Omega;

    public int FifoTally => _track.Count;

    public bool HasLatestJoint => _track.CurrAnim is not null;

    internal AnimTrack Core => _track;

    public MotionTableKeeper Manager => _keeper;

    public Action<uint, bool>? LocomotionDoneMark { get; set; }

    public IReadOnlyList<Cue> QueuedTaps => _taps;

    public (int AnimRefHash, bool IsLooping, double Framerate, int StartFrame, int EndFrame, double FramePosition, int QueueCount) LatestJointDiag
    {
        get
        {
            var joint = _track.CurrAnim;
            if (joint is null)
                return (0, false, 0.0, 0, 0, 0.0, _track.Count);
            bool looping = ReferenceEquals(_track.CurrAnim, _track.LeadCyclic);
            return (Persona(joint.Anim), looping, joint.Framerate, joint.LoFrame, joint.HighFrame, _track.FrameNumber, _track.Count);
        }
    }

    public int LeadCyclicAnimRefDigest => Persona(_track.LeadCyclic?.Anim);

    public void BootstrapPhase() => SecureInitialized();

    public void SetCycle(uint styling, uint locomotion, float paceMod = 1f)
    {
        SecureInitialized();

        (uint mirrored, float pace) = Mirror(locomotion, paceMod);
        if (styling is not 0 && styling != _phase.Style)
            _keeper.PerformMovement(MotionTableGait.Interpreted(styling, 1f));
        PerformTravel(MotionTableGait.Interpreted(mirrored, pace));
    }

    public void PlayAct(uint locomotionDirective, float paceMod = 1f)
    {
        SecureInitialized();
        _keeper.PerformMovement(MotionTableGait.Interpreted(locomotionDirective, paceMod));
    }

    public uint PerformTravel(MotionTableGait travel)
    {
        SecureInitialized();
        return _keeper.PerformMovement(travel);
    }

    public void DropAllConnectAnims() => _track.DeleteAllConnectAnims();

    public bool BootstrapRigDefaultAnim(uint animIdent)
    {
        if (animIdent is 0)
            return false;

        _track.WipeAnims();
        _taps.Clear();
        _track.AffixAnim(new ClipRef
        {
            ClipId = (uint)animIdent,
            LowFrame = 0,
            HighFrame = -1,
            Framerate = 30f,
        });
        return _track.CurrAnim is not null;
    }

    public void Reset()
    {
        _keeper.ServiceQuitRealm();
        _track.Clear();
        _taps.Clear();
        _phase.Style = 0;
        _phase.Substate = 0;
        _phase.SubstateMod = 1f;
        _phase.WipeModifiers();
        _phase.WipeActs();
        _initialized = false;
    }

    public IReadOnlyList<PieceTransform> Advance(float dt) => Advance(dt, trunkLocomotionCycle: null);

    public IReadOnlyList<PieceTransform> Advance(float dt, Pose? trunkLocomotionCycle)
    {
        if (_track.CurrAnim is null && trunkLocomotionCycle is null)
            return PersonaPosture(_rig.PartIds.Count);
        if (dt <= 0f)
            return ProbeLatestPosture();

        _track.Update(dt, trunkLocomotionCycle);
        return ProbeLatestPosture();
    }

    public IReadOnlyList<PieceTransform> ProbeLatestPosture()
    {
        return _track.CurrAnim is null ? PersonaPosture(_rig.PartIds.Count) : BlendedPosture();
    }

    public IReadOnlyList<Cue> AbsorbQueuedTaps()
    {
        if (_taps.Count is 0)
            return [];
        var fired = _taps.ToArray();
        _taps.Clear();
        return fired;
    }

    public static Quaternion SlerpCanonClient(Quaternion q1, Quaternion q2, float t)
    {
        const float SlerpEpsilon = 1e-4f;

        float dot = q1.W * q2.W + q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z;
        Quaternion q2s = q2;
        if (dot < 0f)
        {
            dot = -dot;
            q2s = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
        }

        float w1 = 1f - t;
        float w2 = t;
        if (1f - dot > SlerpEpsilon)
        {
            float omega = MathF.Acos(dot);
            float invSin = 1f / MathF.Sin(omega);
            float c1 = MathF.Sin((1f - t) * omega) * invSin;
            float c2 = MathF.Sin(t * omega) * invSin;
            if (c1 >= 0f && c1 <= 1f && c2 >= 0f && c2 <= 1f)
            {
                w1 = c1;
                w2 = c2;
            }
        }

        return new Quaternion(
            w1 * q1.X + w2 * q2s.X,
            w1 * q1.Y + w2 * q2s.Y,
            w1 * q1.Z + w2 * q2s.Z,
            w1 * q1.W + w2 * q2s.W);
    }

    private static int Persona(MotionClip? anim) => anim is null ? 0 : RuntimeHelpers.GetHashCode(anim);

    private void SecureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;
        _keeper.BootstrapCondition();
    }

    private static (uint Motion, float Speed) Mirror(uint locomotion, float paceMod)
    {
        uint upper = locomotion & 0xFFFF0000u;
        return (locomotion & 0xFFFFu) switch
        {
            0x000E => (upper | 0x000Du, -paceMod),                    // TurnLeft → TurnRight
            0x0010 => (upper | 0x000Fu, -paceMod),                    // SideStepLeft → SideStepRight
            0x0006 => (upper | 0x0005u, -paceMod * BackwardsFactor),  // WalkBackward → WalkForward
            _ => (locomotion, paceMod),
        };
    }

    private IReadOnlyList<PieceTransform> BlendedPosture()
    {
        int pieceTally = _rig.PartIds.Count;
        var joint = _track.CurrAnim;
        if (joint?.Anim is null)
            return PersonaPosture(pieceTally);

        int cycleTally = joint.Anim.Frames.Count;
        int lo = Math.Max(Math.Min(joint.LoFrame, joint.HighFrame), 0);
        int hi = Math.Min(Math.Max(joint.LoFrame, joint.HighFrame), cycleTally - 1);

        int here = Math.Clamp((int)Math.Floor(_track.FrameNumber), lo, hi);
        int upcoming;
        if (joint.Framerate >= 0f)
        {
            upcoming = here + 1;
            if (upcoming > hi || upcoming >= cycleTally)
                upcoming = here;
        }
        else
        {
            upcoming = here - 1;
            if (upcoming < lo)
                upcoming = here;
        }

        float t = (float)Math.Clamp(_track.FrameNumber - Math.Floor(_track.FrameNumber), 0.0, 1.0);

        List<Pose> fromPieces = joint.Anim.Frames[here].Poses;
        List<Pose> toPieces = joint.Anim.Frames[upcoming].Poses;
        int authored = Math.Min(pieceTally, fromPieces.Count);
        for (int idx = 0; idx < authored; ++idx)
        {
            Pose frame = fromPieces[idx];
            Pose b = idx < toPieces.Count ? toPieces[idx] : frame;
            _postureTemp[idx] = new PieceTransform(
                Vector3.Lerp(frame.Origin, b.Origin, t),
                SlerpCanonClient(frame.Orientation, b.Orientation, t));
        }

        _postureLens.AssignTally(authored);
        return _postureLens;
    }

    private IReadOnlyList<PieceTransform> PersonaPosture(int pieceTally)
    {
        // Same reusable buffer as BlendedPose - overwritten in place, never reallocated.
        for (int idx = 0; idx < pieceTally; ++idx)
            _postureTemp[idx] = new PieceTransform(Vector3.Zero, Quaternion.Identity);
        _postureLens.AssignTally(pieceTally);
        return _postureLens;
    }

    private sealed class DoneRelay(AnimSequencer holder) : IMotionDoneTap
    {
        public void MotionDone(uint locomotion, bool success) => holder.LocomotionDoneMark?.Invoke(locomotion, success);
    }

    private sealed class HookCollector(AnimSequencer holder) : IAnimTapFifo
    {
        public void AppendAnimTap(Cue tap) => holder._taps.Add(tap);

        public void AppendAnimDoneTap() => holder._taps.Add(DoneSentinel);
    }

    // A resizable read-only view over the pose scratch buffer
    private sealed class PoseWindow(PieceTransform[] gearList) : IReadOnlyList<PieceTransform>
    {
        public int Count { get; private set; }

        public PieceTransform this[int index] =>
            (uint)index < (uint)Count ? gearList[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public void AssignTally(int tally) => Count = tally;

        public IEnumerator<PieceTransform> GetEnumerator()
        {
            for (int idx = 0; idx < Count; ++idx)
                yield return gearList[idx];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
