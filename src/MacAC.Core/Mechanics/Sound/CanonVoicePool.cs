namespace MacAC.Mechanics.Sound;

public readonly record struct VoiceSlotPhase(bool Occupied, bool StillPlaying, float Priority);

public static class CanonVoicePool
{
    public const int NoSocket = -1;

    public static int Acquire(ReadOnlySpan<VoiceSlotPhase> sockets, int cur, float precedence)
    {
        int num = sockets.Length;
        if (num is 0)
            return NoSocket;

        for (int hop = 0; hop < num; ++hop)
        {
            int at = Ring(cur + hop, num);
            if (!sockets[at].Occupied || !sockets[at].StillPlaying)
                return at;
        }
        for (int hop = 0; hop < num; ++hop)
        {
            int at = Ring(cur + hop, num);
            if (sockets[at].Priority < precedence)
                return at;
        }
        return NoSocket;
    }

    public static int ProgressCur(int claimedSocket, int socketTally) =>
        socketTally <= 0 ? 0 : (claimedSocket + 1) % socketTally;

    private static int Ring(int ordinal, int tally)
    {
        int at = ordinal % tally;
        return at < 0 ? at + tally : at;
    }
}
