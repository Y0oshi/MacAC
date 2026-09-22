
namespace MacAC.Client.Shell.Panels;

public sealed partial class CanonPromptMint
{
    public bool IsOpen => _engagedQueued.Count is not 0 || _engagedNonQueued.Count is not 0;

    public static uint RootElementIdent(CanonPromptType kind)
    {
        return kind switch
        {
            CanonPromptType.Confirmation => 0x15u,
            CanonPromptType.Wait => 0x31u,
            CanonPromptType.Message => 0x24u,
            CanonPromptType.TextInput => 0x28u,
            CanonPromptType.ConfirmationTextInput => 0x2Cu,
            CanonPromptType.Menu => 0x1Bu,
            CanonPromptType.ConfirmationMenu => 0x1Fu,
            _ => 0u,
        };
    }

    public int ActiveTally => _engagedQueued.Count + _engagedNonQueued.Count;

    public int QueuedCount => _queued.Values.Sum(static fifo => fifo.Count);

    public bool ShutPopup(uint ctx)
    {
        if (ctx is 0u)
            return false;

        if (_engagedNonQueued.Remove(ctx, out PromptInfo? nonQueued))
        {
            PopupDone(nonQueued);
            return true;
        }

        foreach ((uint fifoTag, PromptInfo engaged) in _engagedQueued.ToArray())
        {
            if (engaged.Context != ctx)
                continue;

            _engagedQueued.Remove(fifoTag);
            PopupDone(engaged);
            OpenUpcomingPopup(fifoTag);
            return true;
        }

        foreach ((uint fifoTag, LinkedList<PromptInfo> fifo) in _queued.ToArray())
        {
            var joint = fifo.First;
            while (joint is not null && joint.Value.Context != ctx)
                joint = joint.Next;
            if (joint is null)
                continue;

            PromptInfo queued = joint.Value;
            fifo.Remove(joint);
            if (fifo.Count is 0)
                _queued.Remove(fifoTag);
            PopupDone(queued);
            RefreshQueuedPopupDisplays();
            return true;
        }

        var reattempt = _retryable.First;
        while (reattempt is not null && reattempt.Value.Context != ctx)
            reattempt = reattempt.Next;
        if (reattempt is not null)
        {
            PromptInfo failed = reattempt.Value;
            _retryable.Remove(reattempt);
            PopupDone(failed);
            if (failed.FifoLookupKey != NonQueuedTag)
                OpenUpcomingPopup(failed.FifoLookupKey);
            return true;
        }

        return false;
    }

    internal int ReattemptTally => _retryable.Count;

    public void Tick()
    {
        ReattemptFailedPopups();
        foreach (PromptInfo details in _openOrdering.ToArray())
        {
            if (details.View is { } lens)
            {
                _hub.BringToFront(lens.Root);
                lens.Tick();
            }
        }
    }

    public void Reset()
    {
        if (_resetting)
            return;

        _resetting = true;
        List<Exception>? misses = null;
        try
        {
            while (true)
            {
                PromptInfo[] infos = [.. _engagedNonQueued.Values
                    .Concat(_engagedQueued.Values)
                    .Concat(_queued.Values.SelectMany(static fifo => fifo))
                    .Concat(_retryable)
                    .Distinct()];
                if (infos.Length is 0)
                    break;

                _engagedNonQueued.Clear();
                _engagedQueued.Clear();
                _queued.Clear();
                _retryable.Clear();
                foreach (PromptInfo details in infos)
                {
                    try { PopupDone(details); }
                    catch (Exception problem) { (misses ??= []).Add(problem); }
                }
            }
        }
        finally
        {
            _resetting = false;
            RenewModal();
        }

        if (misses is not null)
            throw new AggregateException(
                "One or more dialogs failed while the factory reset",
                misses);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        Reset();
    }

    private static bool IsPrecedence(PromptInfo details) =>
        details.Data.FetchBoolean(CanonPromptProperty.Priority);

    private LinkedList<PromptInfo> QueuedFifo(uint fifoTag)
    {
        if (_queued.TryGetValue(fifoTag, out LinkedList<PromptInfo>? fifo))
            return fifo;
        fifo = new LinkedList<PromptInfo>();
        _queued.Add(fifoTag, fifo);
        return fifo;
    }

    private void ReattemptFailedPopups()
    {
        foreach (PromptInfo details in _retryable.ToArray())
        {
            if (details.FifoLookupKey == NonQueuedTag)
            {
                _engagedNonQueued.Add(details.Context, details);
                if (TryBuildPopup(details))
                    _retryable.Remove(details);
                else
                    _engagedNonQueued.Remove(details.Context);
                continue;
            }

            if (!ReferenceEquals(LeadReattempt(details.FifoLookupKey), details))
                continue;

            if (!_engagedQueued.TryGetValue(
                    details.FifoLookupKey,
                    out PromptInfo? engaged))
                TryEngageReattempt(details.FifoLookupKey);
            else if (IsPrecedence(details)
                && (!IsPrecedence(engaged) || details.Series > engaged.Series))
                TryPreemptWithReattempt(details, engaged);
        }
    }

    private uint UpcomingCtx()
    {
        ++_globalCtx;
        if (_globalCtx is 0u)
            ++_globalCtx;
        return _globalCtx;
    }

    private ulong UpcomingSeries()
    {
        ++_globalSeries;
        if (_globalSeries is 0uL)
            ++_globalSeries;
        return _globalSeries;
    }

    private bool TryBuildPopup(PromptInfo details)
    {
        CanonPromptType kind = (CanonPromptType)details.Data.FetchUInt32(
            CanonPromptProperty.Type);
        try
        {
            if (kind is not (CanonPromptType.Confirmation
                or CanonPromptType.Wait
                or CanonPromptType.Message
                or CanonPromptType.ConfirmationTextInput
                or CanonPromptType.ConfirmationMenu))
            {
                throw new NotSupportedException(
                    $"Retail dialog type {(uint)kind} doesn't have a ported presenter yet");
            }

            ImportedArrangement arrangement = _buildArrangement(kind)
                ?? throw new InvalidOperationException(
                    $"Retail dialog catalog could not create type {(uint)kind}.");
            ICanonPromptView lens = kind switch
            {
                CanonPromptType.Wait => new CanonWaitPromptView(
                    _hub, arrangement, details.Data),
                CanonPromptType.Message => new CanonMessagePromptView(
                    _hub, arrangement, details.Data, details.Context,
                    ctx => ShutPopup(ctx)),
                CanonPromptType.ConfirmationTextInput =>
                    new CanonConfirmationTextInputPromptView(
                        _hub, arrangement, details.Data, details.Context,
                        ctx => ShutPopup(ctx)),
                CanonPromptType.ConfirmationMenu =>
                    new CanonConfirmationMenuPromptView(
                        _hub, arrangement, details.Data, details.Context,
                        ctx => ShutPopup(ctx)),
                _ => new CanonConfirmationPromptView(
                    _hub, arrangement, details.Data, details.Context,
                    ctx => ShutPopup(ctx)),
            };
            details.View = lens;
            _hub.AddChild(lens.Root);
            _hub.BringToFront(lens.Root);
            _openOrdering.Add(details);
            _hub.Modal = lens.Root;
            lens.Tick();
            RefreshQueuedPopupDisplays();
            DialogOpened?.Invoke(details.Context);
            return true;
        }
        catch (Exception problem)
        {
            DropLens(details);
            Console.WriteLine(
                $"[UI] retail dialog type {(uint)kind} context {details.Context} "
                + $"will retry after catalog recovery: {problem.Message}");
            return false;
        }
    }

    private void Suspend(PromptInfo details)
    {
        if (details.View is null)
            return;
        var lens = details.View;
        details.View = null;
        lens.UnfastenHandlers();
        _openOrdering.Remove(details);
        _hub.DropDescendant(lens.Root);
        RenewModal();
    }

    private void PopupDone(PromptInfo details)
    {
        try
        {
            details.Hook?.Invoke(details.Data);
            DialogClosed?.Invoke(details.Context, details.Data);
        }
        finally
        {
            DropLens(details);
        }
    }

    private void DropLens(PromptInfo details)
    {
        if (details.View is { } lens)
        {
            lens.UnfastenHandlers();
            _hub.DropDescendant(lens.Root);
            details.View = null;
            _openOrdering.Remove(details);
        }
        RenewModal();
    }

    private void OpenUpcomingPopup(uint fifoTag)
    {
        if (_engagedQueued.ContainsKey(fifoTag))
            return;

        if (TryEngageReattempt(fifoTag))
            return;

        if (!_queued.TryGetValue(fifoTag, out LinkedList<PromptInfo>? fifo)
            || fifo.First is null)
            return;

        PromptInfo upcoming = fifo.First.Value;
        fifo.RemoveFirst();
        if (fifo.Count is 0)
            _queued.Remove(fifoTag);
        _engagedQueued.Add(fifoTag, upcoming);
        if (!TryBuildPopup(upcoming))
        {
            _engagedQueued.Remove(fifoTag);
            EnqueueReattempt(upcoming);
        }
    }

    private void OpenSpecificPopup(PromptInfo details)
    {
        _engagedQueued.Add(details.FifoLookupKey, details);
        if (!TryBuildPopup(details))
        {
            _engagedQueued.Remove(details.FifoLookupKey);
            EnqueueReattempt(details);
        }
    }

    private void TryPreemptWithReattempt(PromptInfo precedence, PromptInfo latest)
    {
        var fifo = QueuedFifo(precedence.FifoLookupKey);
        Suspend(latest);
        fifo.AddFirst(latest);
        _engagedQueued[precedence.FifoLookupKey] = precedence;
        _retryable.Remove(precedence);
        if (TryBuildPopup(precedence))
            return;

        _engagedQueued.Remove(precedence.FifoLookupKey);
        fifo.Remove(latest);
        if (fifo.Count is 0)
            _queued.Remove(precedence.FifoLookupKey);
        OpenSpecificPopup(latest);
        EnqueueReattempt(precedence);
    }

    private bool TryEngageReattempt(uint fifoTag)
    {
        PromptInfo? details = LeadReattempt(fifoTag);
        if (details is null)
            return false;

        _engagedQueued.Add(fifoTag, details);
        if (TryBuildPopup(details))
            _retryable.Remove(details);
        else
            _engagedQueued.Remove(fifoTag);
        return true;
    }

    private PromptInfo? LeadReattempt(uint fifoTag)
    {
        foreach (PromptInfo details in _retryable)
            if (details.FifoLookupKey == fifoTag)
                return details;
        return null;
    }

    private bool HasReattempt(uint fifoTag) => LeadReattempt(fifoTag) is not null;

    private void EnqueueReattempt(PromptInfo details)
    {
        if (_retryable.Contains(details))
            return;
        if (!IsPrecedence(details))
        {
            _retryable.AddLast(details);
            return;
        }

        var extant = _retryable.First;
        while (extant is not null
            && extant.Value.FifoLookupKey != details.FifoLookupKey)
        {
            extant = extant.Next;
        }
        if (extant is null)
            _retryable.AddLast(details);
        else
            _retryable.AddBefore(extant, details);
    }

    private void RefreshQueuedPopupDisplays()
    {
        foreach ((uint fifoTag, PromptInfo engaged) in _engagedQueued)
        {
            int tally = _queued.TryGetValue(fifoTag, out LinkedList<PromptInfo>? fifo)
                ? fifo.Count
                : 0;
            engaged.View?.AssignQueuedTally(tally);
        }
    }

    private void RenewModal()
    {
        _hub.Modal = _openOrdering.Count is 0 ? null : _openOrdering[^1].View?.Root;
    }
}
