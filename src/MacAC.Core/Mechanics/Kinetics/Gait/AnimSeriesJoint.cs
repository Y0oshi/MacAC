using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class AnimSeriesJoint
{
    private const int Unset = -1;

    public MotionClip? Anim { get; private set; }

    public float Framerate = 30f;

    public int LoFrame = Unset;

    public int HighFrame = Unset;

    public bool HasAnim => Anim is not null;

    public AnimSeriesJoint()
    {
    }

    public AnimSeriesJoint(ClipRef animBlob, IAnimReader fetcher)
    {
        Framerate = animBlob.Framerate;
        LoFrame = animBlob.LowFrame;
        HighFrame = animBlob.HighFrame;
        AssignAnimIdent((uint)animBlob.ClipId, fetcher);
    }

    public void AssignAnimIdent(uint animIdent, IAnimReader fetcher)
    {
        Anim = animIdent is 0 ? null : fetcher.PullAnim(animIdent);
        if (Anim is null)
            return;

        // Clamp the window to what the animation actually has
        int previous = Anim.Frames.Count - 1;
        if (HighFrame < 0 || HighFrame > previous)
            HighFrame = previous;
        if (LoFrame > previous)
            LoFrame = previous;
        if (LoFrame > HighFrame)
            HighFrame = LoFrame;
    }

    private bool PlaysBackwards => Framerate < 0f;

    public int GetStartingFrame() => PlaysBackwards ? HighFrame + 1 : LoFrame;

    public int FetchEndingCycle() => PlaysBackwards ? LoFrame : HighFrame + 1;

    public void MultiplyFramerate(float factor)
    {
        if (factor < 0f)
            (LoFrame, HighFrame) = (HighFrame, LoFrame);
        Framerate *= factor;
    }

    public Pose? GetPosFrame(double cycleNumber) => GetPosFrame((int)Math.Floor(cycleNumber));

    public Pose? GetPosFrame(int ordinal)
    {
        if (FetchPieceCycle(ordinal) is null)
            return null;
        List<Pose> loci = Anim!.RootPoses;
        return loci is not null && ordinal < loci.Count ? loci[ordinal] : null;
    }

    public MotionFrame? FetchPieceCycle(int ordinal)
    {
        return Anim is not null && (uint)ordinal < (uint)Anim.Frames.Count ? Anim.Frames[ordinal] : null;
    }
}
