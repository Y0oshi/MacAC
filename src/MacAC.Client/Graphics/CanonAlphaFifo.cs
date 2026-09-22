using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using MacAC.Client.Graphics.Tenancy;

namespace MacAC.Client.Graphics;

internal enum CanonAlphaList : byte
{
    Alpha = 0,
    Clip = 1,
}

internal enum CanonAlphaFlushSite
{
    DrawBuilding,
    SortCellExit,
    LandscapeFlush,
    RenderNormalMode,
}

internal interface ICanonAlphaDrawSource
{
    void ReadyAlphaDraws(ReadOnlySpan<int> tickets);

    void SketchReadiedAlphaLot(int leadReadiedPaint, int paintTally);

    void RestartAlphaSubmissions();
}

internal readonly record struct CanonAlphaEntry(
    ICanonAlphaDrawSource Source,
    int Token,
    bool OverrideClipmap);

internal interface IRealmStageAlphaFrame
{
    void BeginFrame();

    void EndFrame();

    void AbortFrame();
}

internal sealed class CanonAlphaFifo : IRealmStageAlphaFrame
{
    internal const int RosterCap = 3000;

    private const int FloorSubmissionCap = 256;
    private const int SubmissionGrowthQuantum = 256;
    private const int FloorSrcCap = 4;

    private readonly List<CanonAlphaEntry> _clip = new(256);
    private readonly List<CanonAlphaEntry> _alpha = new(256);
    private readonly List<ICanonAlphaDrawSource> _sources = new(4);
    private int[] _ticketTemp = new int[256];
    private int[] _srcPaintShifts = new int[4];
    private readonly RetainedScratchCapacityRule _tempRule;
    private readonly Action<CanonAlphaFlushSite>? _emptyWatcher;

    internal CanonAlphaFifo(
        long? tempAllowanceOctets = null,
        Action<CanonAlphaFlushSite>? emptyWatcher = null)
    {
        long allowance = tempAllowanceOctets
            ?? AlphaScratchAllowanceProfile.Create(
                TenancyAllowanceKnobs.Default.AlphaScratchBytes).QueueBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(allowance);
        _tempAllowanceOctets = allowance;
        _tempRule = new RetainedScratchCapacityRule(allowance);
        _emptyWatcher = emptyWatcher;
    }

    public bool IsCollecting { get; private set; }

    internal int ClipTally => _clip.Count;

    internal int AlphaTally => _alpha.Count;

    // Total pending entries across both lists
    internal int QueuedTally => _clip.Count + _alpha.Count;

    private readonly long _tempAllowanceOctets;

    internal long TempAllowanceOctets => _tempAllowanceOctets;
    internal long KeptTempOctets
    {
        get
        {
            return checked(
        (long)_clip.Capacity * Unsafe.SizeOf<CanonAlphaEntry>()
        + (long)_alpha.Capacity * Unsafe.SizeOf<CanonAlphaEntry>()
        + (long)_ticketTemp.Length * sizeof(int)
        + (long)_sources.Capacity * IntPtr.Size
        + (long)_srcPaintShifts.Length * sizeof(int));
        }
    }

    public void BeginFrame()
    {
        if (IsCollecting)
            throw new InvalidOperationException("Retail alpha frame is by now active");
        if (_clip.Count is not 0 || _alpha.Count is not 0 || _sources.Count is not 0)
            throw new InvalidOperationException("Retail alpha queue retained payload beyond a frame");

        IsCollecting = true;
    }

    public void Flush(CanonAlphaFlushSite site, float threshold)
    {
        if (!IsCollecting)
            throw new InvalidOperationException("Retail alpha flush needs an active frame");

        if (_clip.Count < threshold * RosterCap && _alpha.Count < threshold * RosterCap)
            return;

        _emptyWatcher?.Invoke(site);
        EmptyAndRestart();
    }

    public void EndFrame()
    {
        if (!IsCollecting)
            throw new InvalidOperationException("Retail alpha frame isn't active");

        try
        {
            Flush(CanonAlphaFlushSite.RenderNormalMode, 0f);
        }
        finally
        {
            IsCollecting = false;
        }
    }

    // Discards an incomplete frame without drawing it
    public void AbortFrame()
    {
        List<Exception>? misses = null;
        try
        {
            for (int idx = 0; idx < _sources.Count; ++idx)
            {
                try
                {
                    _sources[idx].RestartAlphaSubmissions();
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }
        }
        finally
        {
            int observedClip = _clip.Count;
            int observedAlpha = _alpha.Count;
            int observedSrcs = _sources.Count;
            _sources.Clear();
            _clip.Clear();
            _alpha.Clear();
            IsCollecting = false;
            ApplyScratchRetention(observedClip + observedAlpha, observedSrcs);
        }

        if (misses is { Count: > 0 })
            throw new AggregateException("Retail alpha frame abort failed", misses);
    }

    internal bool TryAffix(
        CanonAlphaList roster,
        ICanonAlphaDrawSource src,
        int token,
        bool overrideClipmap)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (!IsCollecting)
            throw new InvalidOperationException("Retail alpha submission needs an active frame");
        if (token < 0)
            throw new ArgumentOutOfRangeException(nameof(token));

        List<CanonAlphaEntry> mark = roster == CanonAlphaList.Clip ? _clip : _alpha;
        EnrollSrc(src);
        if (mark.Count >= RosterCap)
            return false;

        mark.Add(new CanonAlphaEntry(src, token, overrideClipmap));
        return true;
    }

    private void EnrollSrc(ICanonAlphaDrawSource src)
    {
        for (int idx = 0; idx < _sources.Count; ++idx)
            if (ReferenceEquals(_sources[idx], src))
                return;
        _sources.Add(src);
    }

    private void EmptyAndRestart()
    {
        Exception? paintMiss = null;
        List<Exception>? restartMisses = null;
        try
        {
            int sum = _clip.Count + _alpha.Count;
            if (sum > 0)
            {
                SecureTicketCap(sum);
                SecureSrcCap(_sources.Count);
                Array.Clear(_srcPaintShifts, 0, _sources.Count);

                for (int srcOrdinal = 0; srcOrdinal < _sources.Count; ++srcOrdinal)
                {
                    var src = _sources[srcOrdinal];
                    int srcTally = 0;
                    for (int idx = 0; idx < sum; ++idx)
                    {
                        var listing = Listing(idx);
                        if (ReferenceEquals(listing.Source, src))
                            _ticketTemp[srcTally++] = listing.Token;
                    }

                    if (srcTally > 0)
                        src.ReadyAlphaDraws(_ticketTemp.AsSpan(0, srcTally));
                }

                int begin = 0;
                while (begin < sum)
                {
                    var src = Listing(begin).Source;
                    int finish = begin + 1;
                    while (finish < sum && ReferenceEquals(Listing(finish).Source, src))
                        ++finish;

                    int tally = finish - begin;
                    int srcOrdinal = SeekSrcOrdinal(src);
                    int leadReadiedPaint = _srcPaintShifts[srcOrdinal];
                    src.SketchReadiedAlphaLot(leadReadiedPaint, tally);
                    _srcPaintShifts[srcOrdinal] += tally;
                    begin = finish;
                }
            }
        }
        catch (Exception problem)
        {
            paintMiss = problem;
        }
        finally
        {
            int observedClip = _clip.Count;
            int observedAlpha = _alpha.Count;
            int observedSrcs = _sources.Count;
            for (int idx = 0; idx < _sources.Count; ++idx)
            {
                try
                {
                    _sources[idx].RestartAlphaSubmissions();
                }
                catch (Exception problem)
                {
                    (restartMisses ??= []).Add(problem);
                }
            }
            _sources.Clear();
            _clip.Clear();
            _alpha.Clear();
            ApplyScratchRetention(observedClip + observedAlpha, observedSrcs);
        }

        if (paintMiss is not null)
        {
            if (restartMisses is { Count: > 0 })
            {
                restartMisses.Insert(0, paintMiss);
                throw new AggregateException(
                    "Retail alpha drawing failed and its submissions could not be fully reset",
                    restartMisses);
            }

            ExceptionDispatchInfo.Capture(paintMiss).Throw();
        }

        if (restartMisses is { Count: > 0 })
        {
            throw new AggregateException(
                "Retail alpha submissions could not be fully reset",
                restartMisses);
        }
    }

    private CanonAlphaEntry Listing(int ordinal) =>
        ordinal < _clip.Count ? _clip[ordinal] : _alpha[ordinal - _clip.Count];

    private void SecureTicketCap(int tally)
    {
        if (_ticketTemp.Length >= tally)
            return;
        Array.Resize(ref _ticketTemp, tally + 256);
    }

    private void SecureSrcCap(int tally)
    {
        if (_srcPaintShifts.Length >= tally)
            return;
        Array.Resize(ref _srcPaintShifts, tally + 4);
    }

    private int SeekSrcOrdinal(ICanonAlphaDrawSource src)
    {
        for (int idx = 0; idx < _sources.Count; ++idx)
            if (ReferenceEquals(_sources[idx], src))
                return idx;
        throw new InvalidOperationException("Retail alpha source wasn't registered for this frame");
    }

    private void ApplyScratchRetention(
        int observedListingTally,
        int observedSrcTally)
    {
        int latestCap = Math.Max(
            Math.Max(_clip.Capacity, _alpha.Capacity),
            _ticketTemp.Length);
        int octetsPerListing =
            checked(
                2 * Unsafe.SizeOf<CanonAlphaEntry>()
                + sizeof(int)
                + IntPtr.Size);
        int markCap = _tempRule.WatchAndPickCap(
            latestCap,
            observedListingTally,
            octetsPerListing,
            FloorSubmissionCap,
            SubmissionGrowthQuantum);
        if (markCap < latestCap)
        {
            _clip.Capacity = markCap;
            _alpha.Capacity = markCap;
            Array.Resize(ref _ticketTemp, markCap);

            int srcMark = Math.Max(
                FloorSrcCap,
                observedSrcTally is 0
                    ? FloorSrcCap
                    : checked(observedSrcTally * 2));
            srcMark = Math.Min(srcMark, _sources.Capacity);
            _sources.Capacity = srcMark;
            if (_srcPaintShifts.Length > srcMark)
                Array.Resize(ref _srcPaintShifts, srcMark);
        }
    }
}
