using System.Collections.Concurrent;
using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Sound;

public readonly record struct DatWaveCacheTelemetry(
    int CachedWaveCount,
    long ResidentWaveBytes,
    long BudgetBytes,
    long Hits,
    long Misses,
    long Evictions);

public sealed class DatWaveCache
{
    internal const long DefaultUpperWaveOctets = 32L * 1024 * 1024;

    private sealed record Resident(uint WaveId, PcmClip Clip, long Bytes);

    private readonly IDatRecordSource _datFiles;
    private readonly long _allowance;
    private readonly Action<uint>? _onLeadMiss;

    private readonly ConcurrentDictionary<uint, SoundBook?> _charts = new();
    private readonly ConcurrentDictionary<uint, byte> _recognizedAbsent = new();
    private readonly ConcurrentDictionary<uint, Lazy<PcmClip?>> _decoding = new();

    private readonly Lock _synchronize = new();
    private readonly Dictionary<uint, LinkedListNode<Resident>> _byIdent = [];
    private readonly LinkedList<Resident> _recency = new();
    private long _housedOctets;
    private long _strikes;
    private long _misses;
    private long _evictions;

    public DatWaveCache(IDatRecordSource datFiles) : this(datFiles, DefaultUpperWaveOctets)
    {
    }

    /// <summary>Test seam: a small budget makes eviction cheap to exercise.</summary>
    public DatWaveCache(IDatRecordSource datFiles, long upperWaveOctets) : this(datFiles, upperWaveOctets, followingStartingWaveMiss: null)
    {
    }

    internal DatWaveCache(IDatRecordSource datFiles, long upperWaveOctets, Action<uint>? followingStartingWaveMiss)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(upperWaveOctets, 1);
        _datFiles = datFiles;
        _allowance = upperWaveOctets;
        _onLeadMiss = followingStartingWaveMiss;
    }

    public int StashedWaveTally
    {
        get
        {
            lock (_synchronize)
                return _byIdent.Count;
        }
    }

    public long HousedWaveOctets
    {
        get
        {
            lock (_synchronize)
                return _housedOctets;
        }
    }

    /// <summary>How many SoundTables have been asked for.</summary>
    public int StashedSfxChartTally => _charts.Count;

    public DatWaveCacheTelemetry Diagnostics
    {
        get
        {
            lock (_synchronize)
            {
                return new DatWaveCacheTelemetry(
                    _byIdent.Count,
                    _housedOctets,
                    _allowance,
                    Interlocked.Read(ref _strikes),
                    Interlocked.Read(ref _misses),
                    Interlocked.Read(ref _evictions));
            }
        }
    }

    public PcmClip? FetchWave(uint waveIdent)
    {
        if (TryStrike(waveIdent, out PcmClip? strike))
            return strike;

        _onLeadMiss?.Invoke(waveIdent);

        Lazy<PcmClip?> job;
        lock (_synchronize)
        {
            if (TryStrikeBolted(waveIdent, out strike))
                return strike;
            Interlocked.Increment(ref _misses);
            job = _decoding.GetOrAdd(
                waveIdent,
                static (ident, self) => new Lazy<PcmClip?>(() => self.Unpack(ident), LazyThreadSafetyMode.ExecutionAndPublication),
                this);
        }

        try
        {
            PcmClip? clip = job.Value;
            if (clip is null)
            {
                _recognizedAbsent.TryAdd(waveIdent, 0);
                return null;
            }
            return Admit(waveIdent, clip);
        }
        finally
        {
            _decoding.TryRemove(new KeyValuePair<uint, Lazy<PcmClip?>>(waveIdent, job));
        }
    }

    /// <summary>A SoundTable by id, or null when the DATs lack it (also remembered).</summary>
    public SoundBook? FetchSfxChart(uint sfxChartIdent)
    {
        if (_charts.TryGetValue(sfxChartIdent, out SoundBook? recognized))
            return recognized;
        SoundBook? chart = _datFiles.Get<SoundBook>(sfxChartIdent);
        _charts[sfxChartIdent] = chart;
        return chart;
    }

    private bool TryStrike(uint waveIdent, out PcmClip? clip)
    {
        lock (_synchronize)
        {
            if (_byIdent.TryGetValue(waveIdent, out var joint))
            {
                Interlocked.Increment(ref _strikes);
                Touch(joint);
                clip = joint.Value.Clip;
                return true;
            }
        }
        if (_recognizedAbsent.ContainsKey(waveIdent))
        {
            Interlocked.Increment(ref _strikes);
            clip = null;
            return true;
        }
        clip = null;
        return false;
    }

    private bool TryStrikeBolted(uint waveIdent, out PcmClip? clip)
    {
        if (_byIdent.TryGetValue(waveIdent, out var joint))
        {
            Interlocked.Increment(ref _strikes);
            Touch(joint);
            clip = joint.Value.Clip;
            return true;
        }
        if (_recognizedAbsent.ContainsKey(waveIdent))
        {
            Interlocked.Increment(ref _strikes);
            clip = null;
            return true;
        }
        clip = null;
        return false;
    }

    private PcmClip? Unpack(uint waveIdent)
    {
        return _datFiles.Get<SoundClip>(waveIdent) is { } wave ? RiffDecoder.Decipher(wave.Header, wave.Data) : null;
    }

    private PcmClip Admit(uint waveIdent, PcmClip clip)
    {
        long octets = Math.Max(1L, clip.PcmOctets.Length);
        lock (_synchronize)
        {
            if (_byIdent.TryGetValue(waveIdent, out var already))
            {
                Touch(already);
                return already.Value.Clip;
            }

            // Bigger than the whole budget: serve it, never retain it.
            if (octets > _allowance)
                return clip;

            while (_recency.First is { } oldest && _housedOctets + octets > _allowance)
                Evict(oldest);

            _byIdent[waveIdent] = _recency.AddLast(new Resident(waveIdent, clip, octets));
            _housedOctets += octets;
            return clip;
        }
    }

    private void Touch(LinkedListNode<Resident> joint)
    {
        if (ReferenceEquals(joint, _recency.Last))
            return;
        _recency.Remove(joint);
        _recency.AddLast(joint);
    }

    private void Evict(LinkedListNode<Resident> joint)
    {
        _recency.Remove(joint);
        _byIdent.Remove(joint.Value.WaveId);
        _housedOctets -= joint.Value.Bytes;
        Interlocked.Increment(ref _evictions);
    }
}
