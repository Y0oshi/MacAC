using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics;

internal sealed class SyntheticActorMeshReferenceOwner : IDisposable
{
    private sealed class ReferenceLedger(ulong gfxObjRefIdent)
    {
        public ulong GfxObjId { get; } = gfxObjRefIdent;
        public bool Held { get; set; }
    }

    private readonly IBatchMeshBridge _triMeshBridge;
    private readonly ReferenceLedger[] _references;
    private bool _wanted;
    private bool _teardownAsked;
    private bool _reconciling;
    private bool _reconcileAgain;

    public SyntheticActorMeshReferenceOwner(
        IBatchMeshBridge meshAdapter,
        IEnumerable<ulong> gfxObjRefIdents)
    {
        _triMeshBridge = meshAdapter ?? throw new ArgumentNullException(nameof(meshAdapter));
        ArgumentNullException.ThrowIfNull(gfxObjRefIdents);
        _references = [.. gfxObjRefIdents
            .Where(static ident => ident != 0u)
            .Distinct()
            .Select(static ident => new ReferenceLedger(ident))];
    }

    public bool IsDisposed { get; private set; }

    public void Acquire()
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        _wanted = true;
        try
        {
            Reconcile();
            ObjectDisposedException.ThrowIf(_teardownAsked, this);
        }
        catch (Exception acquisitionMiss)
        {
            _wanted = false;
            try
            {
                Reconcile();
            }
            catch (Exception undoMiss)
            {
                throw new AggregateException(
                    "Synthetic entity mesh acquisition failed and its rollback didn't fully converge",
                    acquisitionMiss,
                    undoMiss);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(acquisitionMiss)
                .Throw();
            throw new InvalidOperationException("Unreachable exception dispatch path");
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        _teardownAsked = true;
        _wanted = false;
        if (_reconciling)
        {
            _reconcileAgain = true;
            return;
        }

        Reconcile();
    }

    private void Reconcile()
    {
        if (_reconciling)
        {
            _reconcileAgain = true;
            return;
        }

        _reconciling = true;
        List<Exception>? misses = null;
        try
        {
            do
            {
                _reconcileAgain = false;
                for (int idx = 0; idx < _references.Length; ++idx)
                {
                    var reference = _references[idx];
                    bool markPinned = _wanted;
                    if (reference.Held == markPinned)
                        continue;

                    try
                    {
                        if (markPinned)
                            _triMeshBridge.IncrementRefTally(reference.GfxObjId);
                        else
                            _triMeshBridge.DecrementRefTally(reference.GfxObjId);
                        reference.Held = markPinned;
                    }
                    catch (Exception problem)
                    {
                        if (problem is TriMeshRefAlterationFault
                            {
                                AlterationSealed: true,
                            })

                            reference.Held = markPinned;

                        string op = markPinned ? "acquisition" : "release";
                        (misses ??= []).Add(new InvalidOperationException(
                            $"Synthetic entity mesh 0x{reference.GfxObjId:X10} reference {op} failed",
                            problem));
                    }
                }
            }
            while (_reconcileAgain);
        }
        finally
        {
            _reconciling = false;
            if (_teardownAsked && _references.All(static reference => !reference.Held))
                IsDisposed = true;
        }

        if (misses is not null)
            throw new AggregateException(
                "One or more synthetic entity mesh references could not reconcile",
                misses);
    }
}
