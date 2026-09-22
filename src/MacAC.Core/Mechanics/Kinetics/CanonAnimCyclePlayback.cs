using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public static class CanonAnimCyclePlayback
{
    public static float Advance(float currCycle, int loCycle, int hiCycle, float framerate, float passedSecs)
    {
        int span = hiCycle - loCycle;
        if (span <= 0 || passedSecs <= 0f)
            return currCycle;

        float upcoming = currCycle + passedSecs * framerate;
        if (upcoming > hiCycle)
            return loCycle + ((upcoming - loCycle) % (span + 1));
        return upcoming < loCycle ? loCycle : upcoming;
    }

    public static bool TryLerpPiece(
        MotionClip anim,
        float currCycle,
        int loCycle,
        int hiCycle,
        int pieceOrdinal,
        out Vector3 origin,
        out Quaternion facing)
    {
        ArgumentNullException.ThrowIfNull(anim);

        int cycleTally = anim.Frames.Count;
        int here = (int)MathF.Floor(currCycle);
        if (here < loCycle || here > hiCycle || here >= cycleTally)
            here = loCycle;
        int following = here + 1;
        if (following > hiCycle || following >= cycleTally)
            following = loCycle;
        float t = Math.Clamp(currCycle - here, 0f, 1f);

        var cycles = anim.Frames[here].Poses;
        if (pieceOrdinal >= cycles.Count)
        {
            origin = default;
            facing = default;
            return false;
        }

        var upcomingCycles = anim.Frames[following].Poses;
        Pose frame = cycles[pieceOrdinal];
        Pose b = pieceOrdinal < upcomingCycles.Count ? upcomingCycles[pieceOrdinal] : frame;
        origin = Vector3.Lerp(frame.Origin, b.Origin, t);
        facing = Quaternion.Slerp(frame.Orientation, b.Orientation, t);
        return true;
    }
}
