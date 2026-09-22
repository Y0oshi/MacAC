namespace MacAC.Client.Realm;

public sealed class OnlineActorMotionEngineView<TAnimation>
    : IEnumerable<KeyValuePair<uint, TAnimation>>
    where TAnimation : class, IOnlineActorMotionEngine
{
    private readonly IOnlineActorEngineSource _runtime;
    private readonly List<KeyValuePair<uint, TAnimation>> _iterationCapture = [];

    internal OnlineActorMotionEngineView(IOnlineActorEngineSource runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public int Count => _runtime.Current?.SpatialAnimCoreTally ?? 0;

    public TAnimation this[uint localEntityId]
    {
        set
        {
            OnlineActorCore core = _runtime.Current
                ?? throw new InvalidOperationException("Live entity runtime isn't initialized");
            if (!core.TryFetchSrvOid(localEntityId, out uint oid))
                throw new InvalidOperationException($"No live entity owns local id 0x{localEntityId:X8}.");
            core.AssignAnimCore(oid, value);
        }
    }

    public bool TryGetValue(uint ownActorIdent, out TAnimation anim)
    {
        if (_runtime.Current?.TryFetchAnimCore(ownActorIdent, out var located) == true
            && located is TAnimation typed)
        {
            anim = typed;
            return true;
        }

        anim = null!;
        return false;
    }

    public bool Remove(uint ownActorIdent)
    {
        var core = _runtime.Current;
        return core is not null
            && core.TryFetchSrvOid(ownActorIdent, out uint oid)
            && core.PurgeAnimCore(oid);
    }

    public void DuplicateSpatialIdentsTo(HashSet<uint> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var core = _runtime.Current;
        if (core is null)
            dest.Clear();
        else
            core.DuplicateSpatialAnimOwnIdentsTo(dest);
    }

    public Walker GetEnumerator()
    {
        var core = _runtime.Current;
        if (core is null)
            _iterationCapture.Clear();
        else
            core.DuplicateSpatialAnimRuntimesTo(_iterationCapture);

        return new Walker(core, _iterationCapture);
    }

    internal bool Drop(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return _runtime.Current?.WipeAnimCore(capture) == true;
    }

    IEnumerator<KeyValuePair<uint, TAnimation>> IEnumerable<KeyValuePair<uint, TAnimation>>.GetEnumerator() =>
        GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Walker : IEnumerator<KeyValuePair<uint, TAnimation>>
    {
        private readonly OnlineActorCore? _runtime;
        private readonly List<KeyValuePair<uint, TAnimation>> _capture;
        private int _ordinal;

        internal Walker(
            OnlineActorCore? core,
            List<KeyValuePair<uint, TAnimation>> capture)
        {
            _runtime = core;
            _capture = capture;
            _ordinal = -1;
            Current = default;
        }

        public KeyValuePair<uint, TAnimation> Current { get; private set; }
        object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (++_ordinal < _capture.Count)
            {
                var duo = _capture[_ordinal];
                if (_runtime?.IsLatestSpatialAnim(
                        duo.Key,
                        duo.Value) == true)
                {
                    Current = duo;
                    return true;
                }
            }

            Current = default;
            return false;
        }

        public void Dispose() { }

        void System.Collections.IEnumerator.Reset() =>
            throw new NotSupportedException();
    }
}
