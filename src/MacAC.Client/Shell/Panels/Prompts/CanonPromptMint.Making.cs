
namespace MacAC.Client.Shell.Panels;

public sealed partial class CanonPromptMint
{
    public uint MakeDialog(CanonPromptData blob)
        => MakeDialog(blob, hook: null);

    public uint MakeDialog(CanonPromptData blob, Action<CanonPromptData>? hook)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(blob);

        uint ctx = UpcomingCtx();
        var possessedBlob = blob.Clone();
        uint fifoTag = possessedBlob.FetchUInt32(CanonPromptProperty.FifoTag, DefaultQueueKey);
        if (fifoTag is 0u)
            fifoTag = DefaultQueueKey;

        PromptInfo details = new PromptInfo
        {
            Data = possessedBlob,
            Context = ctx,
            FifoLookupKey = fifoTag,
            Series = UpcomingSeries(),
            Hook = hook,
        };

        if (fifoTag == NonQueuedTag)
        {
            _engagedNonQueued.Add(ctx, details);
            if (!TryBuildPopup(details))
            {
                _engagedNonQueued.Remove(ctx);
                EnqueueReattempt(details);
            }
            return ctx;
        }

        if (!_engagedQueued.TryGetValue(fifoTag, out PromptInfo? latest))
        {
            if (HasReattempt(fifoTag) && !IsPrecedence(details))
            {
                QueuedFifo(fifoTag).AddLast(details);
                return ctx;
            }

            _engagedQueued.Add(fifoTag, details);
            if (!TryBuildPopup(details))
            {
                _engagedQueued.Remove(fifoTag);
                EnqueueReattempt(details);
            }
            return ctx;
        }

        var fifo = QueuedFifo(fifoTag);
        if (!IsPrecedence(details))
        {
            fifo.AddLast(details);
            RefreshQueuedPopupDisplays();
            return ctx;
        }

        Suspend(latest);
        fifo.AddFirst(latest);
        _engagedQueued[fifoTag] = details;
        if (!TryBuildPopup(details))
        {
            _engagedQueued.Remove(fifoTag);
            fifo.Remove(latest);
            if (fifo.Count is 0)
                _queued.Remove(fifoTag);
            OpenSpecificPopup(latest);
            EnqueueReattempt(details);
        }
        return ctx;
    }

    public uint CraftAck(
        string msg,
        Action<CanonPromptData>? hook = null,
        uint fifoTag = DefaultQueueKey,
        bool precedence = false)
    {
        var blob = CanonPromptData.Confirmation(msg)
            .Set(CanonPromptProperty.FifoTag, fifoTag);
        if (precedence)
            blob.Set(CanonPromptProperty.Priority, true);
        return MakeDialog(blob, hook);
    }

    public uint CraftPause(
        string msg,
        uint fifoTag = DefaultQueueKey,
        bool precedence = false)
    {
        var blob = CanonPromptData.Wait(msg)
            .Set(CanonPromptProperty.FifoTag, fifoTag);
        if (precedence)
            blob.Set(CanonPromptProperty.Priority, true);
        return MakeDialog(blob, hook: null);
    }

    public uint CraftMsg(
        string msg,
        Action<CanonPromptData>? hook = null,
        uint fifoTag = DefaultQueueKey,
        bool precedence = false)
    {
        var blob = CanonPromptData.Message(msg)
            .Set(CanonPromptProperty.FifoTag, fifoTag);
        if (precedence)
            blob.Set(CanonPromptProperty.Priority, true);
        return MakeDialog(blob, hook);
    }

    public uint CraftAckPhraseFeed(
        string msg,
        Action<CanonPromptData>? hook = null,
        uint fifoTag = DefaultQueueKey)
    {
        var blob = CanonPromptData.AckPhraseFeed(msg)
            .Set(CanonPromptProperty.FifoTag, fifoTag);
        return MakeDialog(blob, hook);
    }

    public uint CraftAckMenu(
        IReadOnlyList<string> gearList,
        int chosenOrdinal,
        Action<CanonPromptData>? hook = null,
        uint fifoTag = DefaultQueueKey)
    {
        var blob = CanonPromptData.AckMenu(gearList, chosenOrdinal)
            .Set(CanonPromptProperty.FifoTag, fifoTag);
        return MakeDialog(blob, hook);
    }
}
