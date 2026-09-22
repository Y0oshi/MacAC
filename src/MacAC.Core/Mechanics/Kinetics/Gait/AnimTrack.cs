using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics.Gait;

public interface IAnimTapFifo
{
    /// <summary>Queue a matched AnimFrame hook (already direction-filtered by <c>execute_hooks</c>).</summary>
    void AppendAnimTap(Cue tap);

    void AppendAnimDoneTap();
}

public sealed class AnimTrack(IAnimReader fetcher)
{
    private readonly LinkedList<AnimSeriesJoint> _joints = new();   // anim_list (DLList)
    private LinkedListNode<AnimSeriesJoint>? _leadCyclic;          // first_cyclic
    private LinkedListNode<AnimSeriesJoint>? _playing;
    private readonly IAnimReader _fetcher = fetcher;

    public double FrameNumber;

    public Vector3 Velocity;

    public Vector3 Omega;

    /// <summary>Static pose used when no animation node is active (<c>placement_frame</c>, §16).</summary>
    public MotionFrame? StanceCycle { get; private set; }

    public uint StanceCycleIdent { get; private set; }

    /// <summary>Host hook queue (<c>hook_obj</c>); null = hooks dropped (objects without a physics host).</summary>
    public IAnimTapFifo? TapObjRef;

    public AnimSeriesJoint? CurrAnim => _playing?.Value;
    public AnimSeriesJoint? LeadCyclic => _leadCyclic?.Value;
    public int Count => _joints.Count;

    public bool HasAnims() => _joints.Count > 0;

    public void AssignCurrAnimForTest(int ordinal)
    {
        var joint = _joints.First;
        for (int idx = 0; idx < ordinal && joint is not null; ++idx)
            joint = joint.Next;
        _playing = joint;
    }

    internal LinkedListNode<AnimSeriesJoint>? CurrAnimJoint
    {
        get => _playing;
        set => _playing = value;
    }

    internal LinkedListNode<AnimSeriesJoint>? LeadCyclicJoint => _leadCyclic;
    internal LinkedList<AnimSeriesJoint> AnimRoster => _joints;

    public void AffixAnim(ClipRef animBlob)
    {
        AnimSeriesJoint joint = new AnimSeriesJoint(animBlob, _fetcher);
        if (!joint.HasAnim)
            return;

        _joints.AddLast(joint);
        _leadCyclic = _joints.Last;

        if (_playing is null)
        {
            _playing = _joints.First;
            FrameNumber = _playing!.Value.GetStartingFrame();
        }
    }

    public void Clear()
    {
        WipeAnims();
        WipeKinetics();
        StanceCycle = null;
        StanceCycleIdent = 0;
    }

    public void WipeAnims()
    {
        _joints.Clear();
        _leadCyclic = null;
        _playing = null;
        FrameNumber = 0.0;
    }

    public void WipeKinetics()
    {
        Velocity = Vector3.Zero;
        Omega = Vector3.Zero;
    }

    public void DropCyclicAnims()
    {
        for (LinkedListNode<AnimSeriesJoint>? joint = _leadCyclic; joint is not null;)
        {
            var upcoming = joint.Next;
            if (ReferenceEquals(_playing, joint))
            {
                _playing = joint.Previous;
                FrameNumber = _playing?.Value.FetchEndingCycle() ?? 0.0;
            }
            _joints.Remove(joint);
            joint = upcoming;
        }
        _leadCyclic = _joints.Last;
    }

    /// <summary>Drops up to <paramref name="tally"/> link animations from just before the cyclic tail.</summary>
    public void RemoveLinkAnimations(int tally)
    {
        for (int idx = 0; idx < tally; ++idx)
        {
            var connect = _leadCyclic?.Previous;
            if (connect is null)
                break;

            if (ReferenceEquals(_playing, connect))
            {
                _playing = _leadCyclic;
                if (_leadCyclic is not null)
                    FrameNumber = _leadCyclic.Value.GetStartingFrame();
            }
            _joints.Remove(connect);
        }
    }

    public void DeleteAllConnectAnims()
    {
        while (_leadCyclic?.Previous is not null)
            RemoveLinkAnimations(1);
    }

    public void Apricot()
    {
        var front = _joints.First;
        if (front is null || ReferenceEquals(front, _playing))
            return;

        while (!ReferenceEquals(front, _leadCyclic))
        {
            _joints.Remove(front!);
            front = _joints.First;
            if (front is null || ReferenceEquals(front, _playing))
                break;
        }
    }

    public void AssignVel(Vector3 v) => Velocity = v;

    public void AssignOmega(Vector3 w) => Omega = w;

    public void FuseKinetics(Vector3 v, Vector3 w)
    {
        Velocity += v;
        Omega += w;
    }

    public void SubtractKinetics(Vector3 v, Vector3 w)
    {
        Velocity -= v;
        Omega -= w;
    }

    public void MultiplyCyclicAnimFramerate(float factor)
    {
        for (LinkedListNode<AnimSeriesJoint>? joint = _leadCyclic; joint is not null; joint = joint.Next)
            joint.Value.MultiplyFramerate(factor);
    }

    public void AssignStanceCycle(MotionFrame? cycle, uint ident)
    {
        StanceCycle = cycle;
        StanceCycleIdent = ident;
    }

    public MotionFrame? FetchCurrAnimframe()
    {
        return _playing is null ? StanceCycle : _playing.Value.FetchPieceCycle(FetchCurrCycleNumber());
    }

    public int FetchCurrCycleNumber() => (int)Math.Floor(FrameNumber);

    /// <summary>Integrates the accumulators over |quantum|, signed like <paramref name="signSrc"/>.</summary>
    public void ImposeKinetics(Pose cycle, double quantum, double signSrc)
    {
        double signed = Math.Abs(quantum);
        if (signSrc < 0.0)
            signed = -signed;

        float sq = (float)signed;
        cycle.Origin += Velocity * sq;
        PoseOps.Rotate(cycle, Omega * sq);
    }

    public void Update(double momentPassed, Pose? cycle)
    {
        if (_joints.Count > 0 && _playing is not null)
        {
            RefreshInternal(momentPassed, cycle);
            Apricot();
        }
        else if (cycle is not null)
        {
            ImposeKinetics(cycle, momentPassed, momentPassed);
        }
    }

    public void RefreshInternal(double momentPassed, Pose? cycle)
    {
        while (_playing is not null)
        {
            var playing = _playing.Value;
            double framerate = playing.Framerate;
            double frametime = framerate * momentPassed;
            int previousCycle = (int)Math.Floor(FrameNumber);

            FrameNumber += frametime;
            double carried = 0.0;
            bool finished = false;

            if (frametime > 0.0)
            {
                if (playing.HighFrame < Math.Floor(FrameNumber))
                {
                    double overrun = Math.Max(0.0, FrameNumber - playing.HighFrame - 1.0);
                    if (Plays(framerate))
                        carried = overrun / framerate;
                    FrameNumber = playing.HighFrame;
                    finished = true;
                }
                while (Math.Floor(FrameNumber) > previousCycle)
                {
                    if (cycle is not null)
                        FoldSpotCycle(cycle, playing, previousCycle, ahead: true, momentPassed);
                    PerformTaps(playing.FetchPieceCycle(previousCycle), +1);
                    ++previousCycle;
                }
            }
            else if (frametime < 0.0)
            {
                if (playing.LoFrame > Math.Floor(FrameNumber))
                {
                    double overrun = Math.Min(0.0, FrameNumber - playing.LoFrame);
                    if (Plays(framerate))
                        carried = overrun / framerate;
                    FrameNumber = playing.LoFrame;
                    finished = true;
                }
                while (Math.Floor(FrameNumber) < previousCycle)
                {
                    if (cycle is not null)
                        FoldSpotCycle(cycle, playing, previousCycle, ahead: false, momentPassed);
                    PerformTaps(playing.FetchPieceCycle(previousCycle), -1);
                    --previousCycle;
                }
            }
            else
            {
                if (cycle is not null && Math.Abs(momentPassed) > PoseOps.FEpsilon)
                    ImposeKinetics(cycle, momentPassed, momentPassed);
                return;
            }

            if (!finished)
                return;

            // A finished link animation (anything before the cyclic tail) owes a done-hook.
            if (TapObjRef is not null && _joints.First is not null && !ReferenceEquals(_joints.First, _leadCyclic))
                TapObjRef.AppendAnimDoneTap();

            ProgressToUpcomingAnim(momentPassed, cycle);
            momentPassed = carried;
        }
    }

    public void ProgressToUpcomingAnim(double momentPassed, Pose? cycle)
    {
        if (_playing is null)
            return;

        var outgoing = _playing.Value;
        bool forwards = momentPassed >= 0.0;

        // Leaving a node that plays against our direction undoes its edge frame.
        bool reverseOutgoing = forwards ? outgoing.Framerate < 0f : outgoing.Framerate >= 0f;
        if (cycle is not null && reverseOutgoing)
            FoldSpotCycle(cycle, outgoing, (int)FrameNumber, ahead: false, momentPassed);

        _playing = forwards
            ? _playing.Next ?? _leadCyclic
            : _playing.Previous ?? _joints.Last;
        if (_playing is null)
            return;

        var incoming = _playing.Value;
        FrameNumber = forwards ? incoming.GetStartingFrame() : incoming.FetchEndingCycle();

        // Entering a node that plays with our direction applies its edge frame.
        bool enactIncoming = forwards ? incoming.Framerate > 0f : incoming.Framerate < 0f;
        if (cycle is not null && enactIncoming)
            FoldSpotCycle(cycle, incoming, (int)FrameNumber, ahead: true, momentPassed);
    }

    private static bool Plays(double framerate) => Math.Abs(framerate) > PoseOps.FEpsilon;

    // Folds one position frame into the body (forward adds it, backward removes it) and integrates one
    // frame of physics
    private void FoldSpotCycle(Pose cycle, AnimSeriesJoint joint, int cycleOrdinal, bool ahead, double signSrc)
    {
        Pose? spot = joint.GetPosFrame(cycleOrdinal);
        if (spot is not null)
        {
            if (ahead)
                PoseOps.Combine(cycle, spot);
            else
                PoseOps.Subtract1(cycle, spot);
        }
        if (Plays(joint.Framerate))
            ImposeKinetics(cycle, 1.0 / joint.Framerate, signSrc);
    }

    private void PerformTaps(MotionFrame? pieceCycle, int direction)
    {
        if (pieceCycle is null || TapObjRef is null)
            return;

        foreach (Cue? tap in pieceCycle.Cues)
        {
            if (tap is null)
                continue;
            int dir = (int)tap.Direction;
            if (dir is 0 || dir == direction)
                TapObjRef.AppendAnimTap(tap);
        }
    }
}
