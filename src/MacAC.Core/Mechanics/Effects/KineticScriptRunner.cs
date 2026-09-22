using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using DatPhysicsScript =  MacAC.Dat.EffectScript;

namespace MacAC.Mechanics.Effects;

public sealed class KineticScriptRunner
{
    public const float ImmediateCallPesThresholdSecs = 0.0002f;

    private sealed class Lane
    {
        public List<Playing> Programs { get; } = [];
    }

    private sealed class Playing(uint programDid, DatPhysicsScript program, double beginMoment, HashSet<uint>? ancestors, long earliestBeat)
    {
        public uint ProgramDid { get; } = programDid;

        public DatPhysicsScript Program { get; } = program;

        public double StartTime { get; } = beginMoment;

        public double Duration { get; } = program.Steps[^1].StartTime;

        // Scripts that started this one at zero delay; used to break CallPES cycles
        public HashSet<uint>? Ancestors { get; } = ancestors;

        public long EarliestBeat { get; } = earliestBeat;

        public int UpcomingTap { get; set; }
    }

    private readonly record struct PendingCall(uint OwnerLocalId, uint ScriptDid, double DueTime, long EarliestTick);

    private sealed record Firing(uint OwnerLocalId, Playing Script);

    private readonly Func<uint, DatPhysicsScript?> _fetch;
    private readonly IAnimHookTap _tap;
    private readonly Func<double> _clock;
    private readonly Func<double> _dice;
    private readonly Func<uint, bool> _holderMayProceed;
    private readonly Dictionary<uint, DatPhysicsScript?> _programs = [];
    private readonly Dictionary<uint, Lane> _lanes = [];
    private readonly Dictionary<uint, Vector3> _moorings = [];
    private readonly List<PendingCall> _queuedCalls = [];
    private readonly List<uint> _laneOrdering = [];
    private double _instant;
    private long _beat;
    private bool _momentPublishedThisCycle;
    private bool _drainingQueuedCalls;
    private Firing? _firing;

    public KineticScriptRunner(
        Func<uint, DatPhysicsScript?> resolver,
        IAnimHookTap sink,
        Func<double>? timer = null,
        Func<double>? randomUnit = null,
        Func<uint, bool>? canProceedHolder = null)
    {
        _fetch = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _tap = sink ?? throw new ArgumentNullException(nameof(sink));
        _clock = timer ?? (() => _instant);
        _dice = randomUnit ?? Random.Shared.NextDouble;
        _holderMayProceed = canProceedHolder ?? (_ => true);
    }

    /// <summary>Queued scripts across every owner, tails included.</summary>
    public int EngagedProgramTally => _lanes.Values.Sum(static lane => lane.Programs.Count);

    public int EngagedOwnerCount => _lanes.Count;

    public int ScheduledCallPesTally => _queuedCalls.Count;

    public int HolderMooringTally => _moorings.Count;

    public bool DiagTurnedOn { get; set; } = Environment.GetEnvironmentVariable("MACAC_DUMP_PLAYSCRIPT") == "1";

    /// <summary>Always-on structural diagnostics; production wires a logger here.</summary>
    public Action<string>? DiagnosticSink { get; set; }

    public void AssignHolderMooring(uint holderOwnIdent, Vector3 realmLocus)
    {
        if (holderOwnIdent is not 0)
            _moorings[holderOwnIdent] = realmLocus;
    }

    public bool Play(uint programIdent, uint actorIdent, Vector3 mooringRealmSpot)
    {
        AssignHolderMooring(actorIdent, mooringRealmSpot);
        return PlayDirect(actorIdent, programIdent);
    }

    /// <summary>Queues a PhysicsScript by DID behind whatever the owner is already playing.</summary>
    public bool PlayDirect(uint holderOwnIdent, uint programDid)
    {
        if (holderOwnIdent is 0 || programDid is 0)
            return false;

        var program = Fetch(programDid);
        if (program is null || program.Steps.Count is 0)
        {
            Report($"KineticsProgram 0x{programDid:X8} for owner 0x{holderOwnIdent:X8} is missing or empty.");
            if (DiagTurnedOn)
                Console.WriteLine($"[pes] absent/empty script=0x{programDid:X8} owner=0x{holderOwnIdent:X8}");
            return false;
        }
        if (program.Steps.Any(static data => !double.IsFinite(data.StartTime)))
        {
            Report($"KineticsProgram 0x{programDid:X8} for owner 0x{holderOwnIdent:X8} has a non-finite hook time.");
            return false;
        }

        if (!_lanes.TryGetValue(holderOwnIdent, out Lane? lane))
            _lanes.Add(holderOwnIdent, lane = new Lane());

        double begin = lane.Programs.Count is 0 ? _clock() : lane.Programs[^1].StartTime + lane.Programs[^1].Duration;
        if (!double.IsFinite(begin))
        {
            Report($"KineticsProgram 0x{programDid:X8} for owner 0x{holderOwnIdent:X8} produced a non-finite start time.");
            return false;
        }

        // A script queued by a hook at zero delay inherits the firing script's ancestry.
        HashSet<uint>? ancestors = null;
        if (_firing is { } firing && firing.OwnerLocalId == holderOwnIdent && begin <= firing.Script.StartTime)
        {
            ancestors = firing.Script.Ancestors is null
                ? [firing.Script.ProgramDid]
                : new HashSet<uint>(firing.Script.Ancestors) { firing.Script.ProgramDid };
            if (ancestors.Contains(programDid))
            {
                Report(
                    $"Rejected zero-time recursive KineticsProgram 0x{programDid:X8} for owner " +
                    $"0x{holderOwnIdent:X8}; DAT CallPES chain would never yield.");
                return false;
            }
        }

        lane.Programs.Add(new Playing(programDid, program, begin, ancestors, EarliestBeatForProgram(holderOwnIdent)));
        if (DiagTurnedOn)
            Console.WriteLine($"[pes] enqueue script=0x{programDid:X8} owner=0x{holderOwnIdent:X8} start={begin:R} depth={lane.Programs.Count}");
        return true;
    }

    public bool PlanCallPes(uint holderOwnIdent, uint programDid, float ceilingSuspend)
    {
        if (holderOwnIdent is 0 || programDid is 0 || !float.IsFinite(ceilingSuspend))
            return false;
        if (ceilingSuspend < ImmediateCallPesThresholdSecs)
            return PlayDirect(holderOwnIdent, programDid);

        double roll = _dice();
        if (!double.IsFinite(roll))
            return false;
        double due = _clock() + Math.Clamp(roll, 0.0, 1.0) * ceilingSuspend;
        _queuedCalls.Add(new PendingCall(holderOwnIdent, programDid, due, _momentPublishedThisCycle ? _beat + 2 : _beat + 1));
        return true;
    }

    public void PublishTime(double playMoment)
    {
        DemandMonotonic(playMoment);
        _instant = playMoment;
        _momentPublishedThisCycle = true;
    }

    public void Tick(float diffSecs) => Tick(_instant + MathF.Max(0f, diffSecs));

    public void Tick(double playMoment)
    {
        DemandMonotonic(playMoment);
        _instant = playMoment;
        _momentPublishedThisCycle = false;
        ++_beat;

        TriggerQueuedCalls(playMoment);

        _laneOrdering.Clear();
        _laneOrdering.AddRange(_lanes.Keys);
        foreach (uint holderIdent in _laneOrdering)
        {
            if (_lanes.TryGetValue(holderIdent, out Lane? lane) && _holderMayProceed(holderIdent))
                Advance(holderIdent, lane, playMoment);
        }
    }

    public void HaltAllForActor(uint holderOwnIdent)
    {
        _lanes.Remove(holderOwnIdent);
        _moorings.Remove(holderOwnIdent);
        _queuedCalls.RemoveAll(call => call.OwnerLocalId == holderOwnIdent);
    }

    public void Clear()
    {
        _lanes.Clear();
        _moorings.Clear();
        _queuedCalls.Clear();
    }

    public void EnrollProgramForTest(uint ident, DatPhysicsScript program)
    {
        ArgumentNullException.ThrowIfNull(program);
        _programs[ident] = program;
    }

    private void TriggerQueuedCalls(double playMoment)
    {
        _drainingQueuedCalls = true;
        try
        {
            for (int idx = _queuedCalls.Count - 1; idx >= 0; --idx)
            {
                PendingCall call = _queuedCalls[idx];
                if (call.DueTime > playMoment || call.EarliestTick > _beat || !_holderMayProceed(call.OwnerLocalId))
                    continue;
                _queuedCalls.RemoveAt(idx);
                PlayDirect(call.OwnerLocalId, call.ScriptDid);
            }
        }
        finally
        {
            _drainingQueuedCalls = false;
        }
    }

    private void Advance(uint holderIdent, Lane lane, double playMoment)
    {
        while (lane.Programs.Count > 0)
        {
            Playing front = lane.Programs[0];
            if (front.EarliestBeat > _beat)
                return;

            while (front.UpcomingTap < front.Program.Steps.Count)
            {
                EffectStep listing = front.Program.Steps[front.UpcomingTap];
                if (front.StartTime + listing.StartTime > playMoment)
                    break;
                front.UpcomingTap++;
                Fire(holderIdent, front, listing.Cue);

                // The hook may have stopped or replaced this owner's lane.
                if (!_lanes.TryGetValue(holderIdent, out Lane? online) || !ReferenceEquals(online, lane))
                    return;
            }

            if (front.UpcomingTap < front.Program.Steps.Count)
                return;
            lane.Programs.RemoveAt(0);
        }
        _lanes.Remove(holderIdent);
    }

    private void Fire(uint holderIdent, Playing program, Cue tap)
    {
        if (DiagTurnedOn)
            Console.WriteLine($"[pes] fire script=0x{program.ProgramDid:X8} owner=0x{holderIdent:X8} hook={tap.Kind}");

        _moorings.TryGetValue(holderIdent, out Vector3 mooring);
        Firing? outer = _firing;
        _firing = new Firing(holderIdent, program);
        try
        {
            _tap.OnTap(holderIdent, mooring, tap);
        }
        finally
        {
            _firing = outer;
        }
    }

    private DatPhysicsScript? Fetch(uint ident)
    {
        if (_programs.TryGetValue(ident, out DatPhysicsScript? recognized))
            return recognized;
        DatPhysicsScript? program;
        try
        {
            program = _fetch(ident);
        }
        catch (Exception problem)
        {
            Report($"Failed to load KineticsProgram 0x{ident:X8}: {problem.Message}");
            if (DiagTurnedOn)
                Console.WriteLine($"[pes] load failed script=0x{ident:X8}: {problem.Message}");
            program = null;
        }
        _programs[ident] = program;
        return program;
    }

    // Scripts queued from a timed hook or by the owner's own firing script may run this tick
    private long EarliestBeatForProgram(uint holderOwnIdent)
    {
        return _drainingQueuedCalls || (_firing is { OwnerLocalId: var who } && who == holderOwnIdent) ? _beat : _beat + 1;
    }

    private void DemandMonotonic(double gameTime)
    {
        if (!double.IsFinite(gameTime))
            throw new ArgumentOutOfRangeException(nameof(gameTime));
        if (gameTime < _instant)
            throw new ArgumentOutOfRangeException(nameof(gameTime), "KineticsProgram time has to be monotonic");
    }

    private void Report(string msg) => DiagnosticSink?.Invoke(msg);
}
