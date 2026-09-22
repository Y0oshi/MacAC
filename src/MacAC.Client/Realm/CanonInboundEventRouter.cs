namespace MacAC.Client.Realm;

internal sealed class CanonInboundEventRouter
{
    private interface IQueuedOp
    {
        void Invoke();
    }

    private sealed class QueuedAct(Action op) : IQueuedOp
    {
        public void Invoke() => op();
    }

    private sealed class QueuedPhaseOp<TReceiver, TState>(
        TReceiver recipient,
        TState phase,
        Action<TReceiver, TState> op) : IQueuedOp
    {
        public void Invoke() => op(recipient, phase);
    }

    private readonly Queue<IQueuedOp> _queued = new();

    internal int QueuedTally => _queued.Count;
    internal bool IsDraining { get; private set; }

    internal void Run(Action op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (IsDraining)
        {
            _queued.Enqueue(new QueuedAct(op));
            return;
        }

        IsDraining = true;
        try
        {
            op();
            EmptyQueued();
        }
        catch
        {
            _queued.Clear();
            throw;
        }
        finally
        {
            IsDraining = false;
        }
    }

    internal void Run<TReceiver, TState>(
        TReceiver recipient,
        TState phase,
        Action<TReceiver, TState> op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (IsDraining)
        {
            _queued.Enqueue(
                new QueuedPhaseOp<TReceiver, TState>(
                    recipient,
                    phase,
                    op));
            return;
        }

        IsDraining = true;
        try
        {
            op(recipient, phase);
            EmptyQueued();
        }
        catch
        {
            _queued.Clear();
            throw;
        }
        finally
        {
            IsDraining = false;
        }
    }

    internal void Clear()
    {
        if (IsDraining)
        {
            throw new InvalidOperationException(
                "Inbound event work can't be cleared while its retail FIFO is active");
        }
        _queued.Clear();
    }

    private void EmptyQueued()
    {
        while (_queued.TryDequeue(out IQueuedOp? upcoming))
            upcoming.Invoke();
    }
}
