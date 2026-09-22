using System.Numerics;

namespace MacAC.Mechanics.Effects;

public sealed partial class MoteSys : IParticleField
{
    private const int PassTally = 3;

    private readonly EmitterSpecRegistry _specs;
    private readonly Random _rng;
    private readonly Dictionary<int, MoteSpout> _spouts = [];
    private readonly SortedSet<int> _every = [];
    private readonly SortedSet<int> _simulating = [];
    private readonly List<int> _realmSimulating = [];
    private readonly PassIndex[] _passs = [new(), new(), new()];
    private readonly List<int> _beatLot = [];
    private readonly List<int> _tempHnds = [];
    private int _upcomingHnd = 1;
    private float _clock;
    private int _onlineMotes;

    public MoteSys(EmitterSpecRegistry registry, Random? rng = null)
    {
        _specs = registry ?? throw new ArgumentNullException(nameof(registry));
        _rng = rng ?? Random.Shared;
    }

    public event Action<int>? EmitterDied;

    public int EngagedSpoutTally => _spouts.Count;

    public int EngagedMoteTally => _onlineMotes;

    internal int PreviousBeatSpoutTourTally { get; private set; }

    internal int PreviousCanonLensSpoutTourTally { get; private set; }

    internal int PreviousRasterizeAmbitSpoutTourTally { get; private set; }

    public bool IsSpoutAlive(int hnd) => _spouts.ContainsKey(hnd);

    public int SummonSpout(
        EmitterSpec descriptor,
        Vector3 mooring,
        Quaternion? rot = null,
        uint affixedObjectIdent = 0,
        int affixedPieceOrdinal = -1,
        ParticleDrawPass rasterizePass = ParticleDrawPass.Scene,
        ParticleVisibilityRules visRule = ParticleVisibilityRules.World)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        int hnd = _upcomingHnd++;
        MoteSpout spout = new MoteSpout
        {
            Handle = hnd,
            Desc = descriptor,
            MooringSpot = mooring,
            HolderLocus = mooring,
            MooringRot = rot ?? Quaternion.Identity,
            AffixedObjectIdent = affixedObjectIdent,
            AffixedPieceOrdinal = affixedPieceOrdinal,
            RasterizePass = rasterizePass,
            VisRule = visRule,
            Particles = new Mote[Math.Max(1, descriptor.MaxParticles)],
            StartedAt = _clock,
            PreviousEmitMoment = _clock,
            PreviousEmitShift = mooring,
        };

        _spouts[hnd] = spout;
        _every.Add(hnd);
        _simulating.Add(hnd);
        if (visRule == ParticleVisibilityRules.World)
            FollowRealmSimulation(hnd);
        Pass(spout).Enrol(spout);

        for (int idx = 0; idx < descriptor.InitialParticles; ++idx)
            Birth(spout, evictWhenWhole: false);
        return hnd;
    }

    public int SummonSpoutByIdent(
        uint spoutIdent,
        Vector3 mooring,
        Quaternion? rot = null,
        uint affixedObjectIdent = 0,
        int affixedPieceOrdinal = -1,
        ParticleDrawPass rasterizePass = ParticleDrawPass.Scene,
        ParticleVisibilityRules visRule = ParticleVisibilityRules.World)
    {
        return SummonSpout(_specs.Get(spoutIdent), mooring, rot, affixedObjectIdent, affixedPieceOrdinal, rasterizePass, visRule);
    }

    public bool TrySpawnEmitterById(
        uint spoutIdent,
        Vector3 mooring,
        Quaternion? rot,
        uint affixedObjectIdent,
        int affixedPieceOrdinal,
        ParticleDrawPass rasterizePass,
        ParticleVisibilityRules visRule,
        out int hnd)
    {
        return TrySpawnEmitterById(spoutIdent, mooring, rot, affixedObjectIdent, affixedPieceOrdinal, rasterizePass, visRule, out hnd, out _);
    }

    public bool TrySpawnEmitterById(
        uint spoutIdent,
        Vector3 mooring,
        Quaternion? rot,
        uint affixedObjectIdent,
        int affixedPieceOrdinal,
        ParticleDrawPass rasterizePass,
        ParticleVisibilityRules visRule,
        out int hnd,
        out EmitterSpecMiss miss)
    {
        if (!_specs.TryGet(spoutIdent, out EmitterSpec? descriptor, out miss))
        {
            hnd = 0;
            return false;
        }
        hnd = SummonSpout(descriptor, mooring, rot, affixedObjectIdent, affixedPieceOrdinal, rasterizePass, visRule);
        miss = EmitterSpecMiss.None;
        return true;
    }

    public void PlayProgram(uint programIdent, uint markObjectIdent, float modifier = 1f)
    {
        // Script scheduling is KineticScriptRunner's job.
    }

    public void HaltSpout(int hnd, bool fadeOut)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter))
            return;
        emitter.Finished = true;
        if (fadeOut)
            return;
        for (int idx = 0; idx < emitter.Particles.Length; ++idx)
            emitter.Particles[idx].Alive = false;
        emitter.ActiveCount = 0;
        Retire(hnd, emitter);
    }

    public void RefreshSpoutMooring(int hnd, Vector3 mooring, Quaternion? rot = null)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter))
            return;
        emitter.MooringSpot = mooring;
        if (!emitter.SimulationTurnedOn)
            emitter.PreviousEmitShift = mooring;
        if (rot is { } spin)
            emitter.MooringRot = spin;
    }

    public void RefreshSpoutHolderLocus(int hnd, Vector3 holderLocus)
    {
        if (_spouts.TryGetValue(hnd, out MoteSpout? emitter))
            emitter.HolderLocus = holderLocus;
    }

    public void RefreshSpoutHolderChamber(int hnd, uint holderChamberIdent)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter) || emitter.HolderChamberIdent == holderChamberIdent)
            return;
        Pass(emitter).ShiftChamber(emitter, holderChamberIdent);
    }

    public void AssignSpoutVisRule(int hnd, ParticleVisibilityRules visRule)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter) || emitter.VisRule == visRule)
            return;

        bool wasDrawable = IsDrawable(emitter);
        if (emitter.VisRule == ParticleVisibilityRules.World)
            _realmSimulating.Remove(hnd);

        emitter.VisRule = visRule;
        if (visRule == ParticleVisibilityRules.World)
        {
            if (emitter.SimulationTurnedOn)
                FollowRealmSimulation(hnd);
        }
        else
        {
            // Examination and dedicated-pass emitters skip the world view test
            emitter.LensEligible = true;
        }
        Pass(emitter).Reindex(emitter, wasDrawable);
    }

    public void ImposeCanonLens(Vector3 beholderLocus, IReadOnlySet<uint> shownSceneryChamberIdents, bool hasFinishedLens, float spanMultiplier = 1f)
    {
        ArgumentNullException.ThrowIfNull(shownSceneryChamberIdents);
        if (!float.IsFinite(spanMultiplier) || spanMultiplier <= 0f)
            spanMultiplier = 1f;

        PreviousCanonLensSpoutTourTally = 0;
        for (int idx = 0; idx < _realmSimulating.Count; ++idx)
        {
            if (!_spouts.TryGetValue(_realmSimulating[idx], out MoteSpout? emitter))
                continue;
            ++PreviousCanonLensSpoutTourTally;
            bool wasDrawable = IsDrawable(emitter);

            if (hasFinishedLens)
            {
                float reach = emitter.Desc.UpperDowngradeGap * spanMultiplier;
                float gap = CanonGap(emitter.HolderLocus, beholderLocus);
                uint lo = emitter.HolderChamberIdent & 0xFFFFu;
                bool chamberInLens = lo >= 0x0100u || (lo > 0u && shownSceneryChamberIdents.Contains(emitter.HolderChamberIdent));
                emitter.LensEligible = emitter.HolderChamberIdent is not 0 && chamberInLens
                    && (float.IsNaN(gap) || float.IsNaN(reach) || gap <= reach);
            }
            else
            {
                emitter.LensEligible = false;
            }
            Pass(emitter).Reindex(emitter, wasDrawable);
        }
    }

    public void AssignSpoutExhibitShown(int hnd, bool shown)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter) || emitter.ExhibitShown == shown)
            return;
        bool wasDrawable = IsDrawable(emitter);
        emitter.ExhibitShown = shown;
        Pass(emitter).Reindex(emitter, wasDrawable);
    }

    public void AssignSpoutSimulationTurnedOn(int hnd, bool turnedOn)
    {
        if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter) || emitter.SimulationTurnedOn == turnedOn)
            return;

        if (!turnedOn)
        {
            bool wasDrawable = IsDrawable(emitter);
            emitter.SimulationTurnedOn = false;
            _simulating.Remove(hnd);
            _realmSimulating.Remove(hnd);
            if (emitter.VisRule == ParticleVisibilityRules.World)
            {
                emitter.LensEligible = false;
                Pass(emitter).Reindex(emitter, wasDrawable);
            }
            return;
        }

        // A rate-driven emitter restarts its accumulator so no burst is owed for the pause.
        if (emitter.Desc.Birthrate <= 0f && emitter.Desc.EmitRate > 0f)
        {
            emitter.PreviousEmitMoment = _clock;
            emitter.EmittedAccumulator = 0f;
        }
        emitter.SimulationTurnedOn = true;
        _simulating.Add(hnd);
        if (emitter.VisRule == ParticleVisibilityRules.World)
            FollowRealmSimulation(hnd);
        else
            emitter.LensEligible = true;
    }

    public void Tick(float dt)
    {
        if (dt <= 0f)
            return;

        _clock += dt;
        _onlineMotes = 0;
        PreviousBeatSpoutTourTally = 0;

        _beatLot.Clear();
        _beatLot.AddRange(_simulating);

        foreach (int hnd in _beatLot)
        {
            if (!_spouts.TryGetValue(hnd, out MoteSpout? emitter) || !emitter.SimulationTurnedOn)
                continue;
            ++PreviousBeatSpoutTourTally;
            bool finishedPrior = emitter.Finished;

            if (emitter.LensEligible)
            {
                emitter.DegradedOut = false;
                Advance(emitter);
            }
            else
            {
                emitter.DegradedOut = true;
                ProgressDegraded(emitter, dt);
            }

            int online = emitter.ActiveCount;
            _onlineMotes += online;

            if (emitter.Desc.SumInterval > 0f && _clock - emitter.StartedAt > emitter.Desc.SumInterval)
                emitter.Finished = true;
            if (emitter.Desc.TotalParticles > 0 && emitter.SumEmitted >= emitter.Desc.TotalParticles)
                emitter.Finished = true;

            // An emitter dies one tick after it finished with nothing left alive
            if (emitter.Finished && online is 0 && finishedPrior)
                Retire(hnd, emitter);
        }
    }

    public LiveParticleWalk IterateOnline() => new(this);

    public LiveEmitterWalk IterateSpouts() => new(this);

    /// <summary>Drawable emitters in one pass, in spawn order.</summary>
    public DrawableEmitterWalk IterateRenderableSpouts(ParticleDrawPass rasterizePass) => new(this, rasterizePass);

    public void DuplicateRenderableSpoutsForHolders(
        ParticleDrawPass rasterizePass,
        IReadOnlySet<uint> affixedHolderIdents,
        bool includeUnattached,
        List<MoteSpout> dest,
        IReadOnlySet<uint>? excludedAffixedHolderIdents = null,
        LooseEmitterCellScope unattachedChamberAmbit = LooseEmitterCellScope.Any)
    {
        ArgumentNullException.ThrowIfNull(affixedHolderIdents);
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        PreviousRasterizeAmbitSpoutTourTally = 0;
        PassIndex pass = _passs[PassSocket(rasterizePass)];

        if (includeUnattached)
        {
            foreach (int hnd in pass.DrawableLoose)
            {
                ++PreviousRasterizeAmbitSpoutTourTally;
                if (_spouts.TryGetValue(hnd, out MoteSpout? emitter) && InChamberAmbit(emitter, unattachedChamberAmbit))
                    dest.Add(emitter);
            }
        }

        foreach (uint holderIdent in affixedHolderIdents)
        {
            if (excludedAffixedHolderIdents?.Contains(holderIdent) == true || !pass.ByHolder.TryGetValue(holderIdent, out MechBucket? bin))
                continue;
            _tempHnds.Clear();
            bin.DuplicateDrawableTo(_tempHnds);
            PreviousRasterizeAmbitSpoutTourTally += _tempHnds.Count;
            foreach (int hnd in _tempHnds)
            {
                if (_spouts.TryGetValue(hnd, out MoteSpout? emitter))
                    dest.Add(emitter);
            }
        }

        dest.Sort(static (l, r) => l.Handle.CompareTo(r.Handle));
    }

    public bool HasRenderableSpoutsInCell(ParticleDrawPass rasterizePass, uint chamberIdent)
    {
        return _passs[PassSocket(rasterizePass)].ByCell.TryGetValue(chamberIdent, out MechBucket? bin) && bin.LeadDrawable is not 0;
    }

    public void DuplicateRenderableSpoutsInChamber(ParticleDrawPass rasterizePass, uint chamberIdent, List<MoteSpout> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        PreviousRasterizeAmbitSpoutTourTally = 0;
        if (!_passs[PassSocket(rasterizePass)].ByCell.TryGetValue(chamberIdent, out MechBucket? bin))
            return;

        _tempHnds.Clear();
        bin.DuplicateDrawableTo(_tempHnds);
        PreviousRasterizeAmbitSpoutTourTally = _tempHnds.Count;
        foreach (int hnd in _tempHnds)
        {
            if (_spouts.TryGetValue(hnd, out MoteSpout? emitter))
                dest.Add(emitter);
        }
    }

    private PassIndex Pass(MoteSpout emitter) => _passs[PassSocket(emitter.RasterizePass)];

    private static int PassSocket(ParticleDrawPass pass)
    {
        int socket = (int)pass;
        if ((uint)socket >= PassTally)
            throw new ArgumentOutOfRangeException(nameof(pass));
        return socket;
    }

    private static bool IsDrawable(MoteSpout emitter) => emitter.ExhibitShown && emitter.LensEligible;

    private static bool InChamberAmbit(MoteSpout emitter, LooseEmitterCellScope ambit)
    {
        if (ambit == LooseEmitterCellScope.Any)
            return true;
        uint lo = emitter.HolderChamberIdent & 0xFFFFu;
        return ambit == LooseEmitterCellScope.OutdoorCells ? lo is not 0 && lo < 0x0100u : lo >= 0x0100u;
    }

    private void FollowRealmSimulation(int hnd)
    {
        int at = _realmSimulating.BinarySearch(hnd);
        if (at < 0)
            _realmSimulating.Insert(~at, hnd);
    }

    private void Retire(int hnd, MoteSpout emitter)
    {
        if (!_spouts.Remove(hnd))
            return;
        _every.Remove(hnd);
        _simulating.Remove(hnd);
        _realmSimulating.Remove(hnd);
        Pass(emitter).Withdraw(emitter);
        ProclaimDeath(hnd);
    }

    private void ProclaimDeath(int hnd)
    {
        if (EmitterDied is not { } watchers)
            return;
        List<Exception>? misses = null;
        foreach (Action<int> watcher in watchers.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                watcher(hnd);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        if (misses is not null)
            throw new AggregateException($"One or more emitter-death callbacks failed for handle {hnd}.", misses);
    }

    // The retail scaled-magnitude distance, which survives huge coordinates without overflow
    private static float CanonGap(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        float scaling = MathF.Max(MathF.Abs(dx), MathF.Max(MathF.Abs(dy), MathF.Abs(dz)));
        if (float.IsNaN(scaling) || float.IsInfinity(scaling) || scaling == 0f)
            return scaling;
        dx /= scaling;
        dy /= scaling;
        dz /= scaling;
        return scaling * MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
