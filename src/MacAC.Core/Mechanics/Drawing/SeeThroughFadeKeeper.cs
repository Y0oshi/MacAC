namespace MacAC.Mechanics.Drawing;

public sealed class SeeThroughFadeKeeper
{
    public const float SeeThroughInstantEpsilon = 0.000199999995f;

    private sealed class Ramp(float interval, float from, float to)
    {
        public float Elapsed;
        public readonly float Duration = interval;
        public readonly float From = from;
        public readonly float To = to;

        // Advances and returns the current value plus whether the ramp is over
        public (float Value, bool Done) Step(float dt)
        {
            Elapsed += dt;
            float t = Duration <= SeeThroughInstantEpsilon ? 1f : Math.Clamp(Elapsed / Duration, 0f, 1f);
            return (t >= 1f ? To : From + (To - From) * t, t >= 1f);
        }
    }

    private readonly Dictionary<uint, Dictionary<uint, Ramp>> _ramps = [];
    private readonly Dictionary<uint, Dictionary<uint, float>> _latest = [];
    private ulong _rev = 1;

    public ulong Revision => _rev;

    public void BeginPieceFade(uint actorIdent, uint pieceOrdinal, float begin, float finish, float moment)
    {
        if (moment <= SeeThroughInstantEpsilon)
        {
            if (_ramps.TryGetValue(actorIdent, out var online))
                online.Remove(pieceOrdinal);
            Seal(actorIdent, pieceOrdinal, finish);
            return;
        }

        if (!_ramps.TryGetValue(actorIdent, out var ramps))
            _ramps[actorIdent] = ramps = [];
        ramps[pieceOrdinal] = new Ramp(moment, begin, finish);
        Seal(actorIdent, pieceOrdinal, begin);
    }

    public void AdvanceAll(float dt)
    {
        if (_ramps.Count is 0)
            return;

        List<uint>? idleActors = null;
        foreach ((uint actorIdent, Dictionary<uint, Ramp> ramps) in _ramps)
        {
            List<uint>? finished = null;
            foreach ((uint pieceOrdinal, Ramp ramp) in ramps)
            {
                (float val, bool done) = ramp.Step(dt);
                Seal(actorIdent, pieceOrdinal, val);
                if (done)
                    (finished ??= []).Add(pieceOrdinal);
            }

            if (finished is null)
                continue;
            foreach (uint pieceOrdinal in finished)
                ramps.Remove(pieceOrdinal);
            if (ramps.Count is 0)
                (idleActors ??= []).Add(actorIdent);
        }

        if (idleActors is not null)
        {
            foreach (uint actorIdent in idleActors)
                _ramps.Remove(actorIdent);
        }
    }

    public bool TryFetchLatestVal(uint actorIdent, uint pieceOrdinal, out float val)
    {
        if (_latest.TryGetValue(actorIdent, out var pieces) && pieces.TryGetValue(pieceOrdinal, out val))
            return true;
        val = 0f;
        return false;
    }

    /// <summary>Forgets every ramp and value for an entity (despawn or unload).</summary>
    public void WipeActor(uint actorIdent)
    {
        bool anything = _ramps.Remove(actorIdent);
        anything |= _latest.Remove(actorIdent);
        if (anything)
            Bump();
    }

    private void Seal(uint actorIdent, uint pieceOrdinal, float val)
    {
        if (!_latest.TryGetValue(actorIdent, out var pieces))
            _latest[actorIdent] = pieces = [];

        if (pieces.TryGetValue(pieceOrdinal, out float preceding)
            && BitConverter.SingleToInt32Bits(preceding) == BitConverter.SingleToInt32Bits(val))
            return;

        pieces[pieceOrdinal] = val;
        Bump();
    }

    private void Bump()
    {
        if (_rev == ulong.MaxValue)
            throw new InvalidOperationException("Translucency fade revision space was exhausted");
        ++_rev;
    }
}
