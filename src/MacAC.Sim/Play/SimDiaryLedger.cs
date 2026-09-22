using MacAC.Mechanics.Diary;

namespace MacAC.Sim.Play;

public readonly record struct SimDiaryHoldingCapture(bool IsDisposed, int PageCount)
{
    public bool IsConverged => IsDisposed && PageCount is 0;
}

public readonly record struct SimDiaryCapture(long Revision, int PageCount, int CurrentPage, bool IsDirty);

public interface ISimDiaryLens
{
    SimDiaryCapture Snapshot { get; }

    IReadOnlyList<DiaryPage> Pages { get; }

    DiaryPage Current { get; }

    double LeftoverTickerSecs(DateTime instant);
}

public sealed class SimDiaryLedger : IDisposable
{
    private readonly object _latch = new();
    private readonly List<DiaryPage> _sheets = [];
    private int _sheet;
    private long _rev;
    private bool _stale;
    private bool _destroyed;
    private DateTime? _tickerBegin;
    private double _tickerLen;

    public SimDiaryLedger() => View = new Lens(this);

    public ISimDiaryLens View { get; }

    public bool IsStale
    {
        get { lock (_latch) return _stale; }
    }

    public bool IsPreviousSheet
    {
        get { lock (_latch) return _sheet is not 0 && _sheet == _sheets.Count; }
    }

    private bool OnSheet => !_destroyed && _sheet is not 0;

    private DiaryPage Here
    {
        get => _sheets[_sheet - 1];
        set => _sheets[_sheet - 1] = value;
    }

    public void Load(IReadOnlyList<DiaryPage> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        lock (_latch)
        {
            if (_destroyed)
                return;
            _sheets.Clear();
            _sheets.AddRange(sheets);
            _sheet = _sheets.Count is 0 ? 0 : 1;
            HaltTicker();
            _stale = false;
            ++_rev;
        }
    }

    /// <summary>The pages as they should be written, with the running timer folded into its page.</summary>
    public IReadOnlyList<DiaryPage> GrabForPersist(DateTime instant)
    {
        lock (_latch)
        {
            DiaryPage[] stored = new DiaryPage[_sheets.Count];
            for (int idx = 0; idx < stored.Length; ++idx)
                stored[idx] = idx + 1 == _sheet ? _sheets[idx] with { RunningTimerSeconds = Remaining(instant) } : _sheets[idx];
            return stored;
        }
    }

    public void FlagStored()
    {
        lock (_latch)
            _stale = false;
    }

    public void NewPage()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _sheets.Add(DiaryPage.Empty);
            _sheet = _sheets.Count;
            HaltTicker();
            _stale = true;
            ++_rev;
        }
    }

    public bool EraseSheet(int sheetNumber)
    {
        lock (_latch)
        {
            if (_destroyed || sheetNumber < 1 || sheetNumber > _sheets.Count)
                return false;
            _sheets.RemoveAt(sheetNumber - 1);
            _sheet = Math.Min(_sheet, _sheets.Count);
            HaltTicker();
            _stale = true;
            ++_rev;
            return true;
        }
    }

    public bool GotoSheet(int sheetNumber)
    {
        lock (_latch)
        {
            if (_destroyed || sheetNumber < 1 || sheetNumber > _sheets.Count)
                return false;
            if (_sheet != sheetNumber)
            {
                _sheet = sheetNumber;
                HaltTicker();
                ++_rev;
            }
            return true;
        }
    }

    public void RefreshLatest(string caption, string banner, string notes)
    {
        lock (_latch)
        {
            if (!OnSheet)
                return;
            DiaryPage updated = (Here with { Label = caption ?? string.Empty, Title = banner ?? string.Empty, Notes = notes ?? string.Empty }).Clipped();
            if (updated == Here)
                return;
            Here = updated;
            _stale = true;
            ++_rev;
        }
    }

    public void CaptureLocale(float x, float y)
    {
        Modify(sheet => sheet with { LocationX = x, LocationY = y, HasLocation = true });
    }

    public void AssignTicker(int days, int hours, int minutes)
    {
        Modify(sheet => sheet with { TimerDays = Math.Max(0, days), TimerHours = Math.Max(0, hours), TimerMinutes = Math.Max(0, minutes) });
    }

    public bool BeginTicker(DateTime instant)
    {
        lock (_latch)
        {
            if (!OnSheet)
                return false;
            double secs = Here.TickerInterval.TotalSeconds;
            if (secs <= 0d)
                return false;
            _tickerBegin = instant;
            _tickerLen = secs;
            Here = Here with { RunningTimerSeconds = secs };
            _stale = true;
            ++_rev;
            return true;
        }
    }

    public void RestartTicker()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            bool wasRunning = _tickerBegin is not null;
            HaltTicker();
            if (_sheet is not 0)
                Here = Here with { RunningTimerSeconds = 0d };
            if (wasRunning)
                _stale = true;
            ++_rev;
        }
    }

    public SimDiaryHoldingCapture CaptureOwnership()
    {
        lock (_latch)
            return new SimDiaryHoldingCapture(_destroyed, _sheets.Count);
    }

    public void ResetSession()
    {
        lock (_latch)
            Wipe();
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            Wipe();
            _destroyed = true;
        }
    }

    // An edit to the current page; a no-op when there is none
    private void Modify(Func<DiaryPage, DiaryPage> edit)
    {
        lock (_latch)
        {
            if (!OnSheet)
                return;
            Here = edit(Here);
            _stale = true;
            ++_rev;
        }
    }

    private void Wipe()
    {
        bool altered = _sheets.Count is not 0 || _sheet is not 0;
        _sheets.Clear();
        _sheet = 0;
        _stale = false;
        HaltTicker();
        if (altered)
            ++_rev;
    }

    private void HaltTicker()
    {
        _tickerBegin = null;
        _tickerLen = 0d;
    }

    // Seconds left on the running timer, or the current page's stored remainder when none is running
    private double Remaining(DateTime instant)
    {
        if (_tickerBegin is not { } begun)
            return _sheet is 0 ? 0d : Here.RunningTimerSeconds;
        return Math.Max(0d, _tickerLen - (instant - begun).TotalSeconds);
    }

    private sealed class Lens(SimDiaryLedger holder) : ISimDiaryLens
    {
        public SimDiaryCapture Snapshot
        {
            get
            {
                lock (holder._latch)
                    return new SimDiaryCapture(holder._rev, holder._sheets.Count, holder._sheet, holder._stale);
            }
        }

        public IReadOnlyList<DiaryPage> Pages
        {
            get { lock (holder._latch) return [.. holder._sheets]; }
        }

        public DiaryPage Current
        {
            get { lock (holder._latch) return holder._sheet is 0 ? DiaryPage.Empty : holder.Here; }
        }

        public double LeftoverTickerSecs(DateTime instant)
        {
            lock (holder._latch)
                return holder.Remaining(instant);
        }
    }
}
