namespace MacAC.Client.Fighting;

// The sim's target-operations seam; unbound it neither auto-targets nor selects anything
internal sealed class EngineFightingTargetOperationsSlot : ISimFightingTargetOps
{
    private ISimFightingTargetOps? _tied;

    public IDisposable BindOwned(ISimFightingTargetOps holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_tied is not null)
            throw new InvalidOperationException("Runtime combat-target operations are by now bound");
        _tied = holder;
        return new Lease(this, holder);
    }
    public uint? PickClosestMark() => _tied?.PickClosestMark();

    public bool AutoTarget => _tied?.AutoTarget == true;

    private void Release(ISimFightingTargetOps anticipated)
    {
        if (ReferenceEquals(_tied, anticipated))
            _tied = null;
    }

    private sealed class Lease(EngineFightingTargetOperationsSlot socket, ISimFightingTargetOps holder) : IDisposable
    {
        private EngineFightingTargetOperationsSlot? _socket = socket;

        public void Dispose() => Interlocked.Exchange(ref _socket, null)?.Release(holder);
    }
}

internal sealed class OnlineFightingTargetOperations(Func<bool> autoTarget, Func<uint?> selectClosestTarget) : ISimFightingTargetOps
{
    private readonly Func<bool> _autoMark = autoTarget ?? throw new ArgumentNullException(nameof(autoTarget));
    private readonly Func<uint?> _closest = selectClosestTarget ?? throw new ArgumentNullException(nameof(selectClosestTarget));

    public bool AutoTarget => _autoMark();
    public uint? PickClosestMark() => _closest();
}
