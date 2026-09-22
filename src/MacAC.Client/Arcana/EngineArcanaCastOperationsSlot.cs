using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Arcana;

// The sim's cast-operations seam, filled by whichever session is online
internal sealed class EngineArcanaCastOperationsSlot : ISimArcanaCastOps
{
    private ISimArcanaCastOps? _tied;

    public IDisposable BindOwned(ISimArcanaCastOps holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_tied is not null)
            throw new InvalidOperationException("Runtime spell-cast operations are by now bound");
        _tied = holder;
        return new Lease(this, holder);
    }
    public bool HasNeededModules(uint arcanumIdent) => _tied?.HasNeededModules(arcanumIdent) == true;

    public uint OwnAvatarIdent => _tied?.OwnAvatarIdent ?? 0u;
    public bool CanTransmit => _tied?.CanTransmit == true;
    public bool IsMarkCompatible(uint markIdent, SpellMeta arcanum, bool unhideMsg) =>
        _tied?.IsMarkCompatible(markIdent, arcanum, unhideMsg) == true;
    public void StopCompletely() => _tied?.StopCompletely();
    public void TransmitUntargeted(uint arcanumIdent) => _tied?.TransmitUntargeted(arcanumIdent);
    public void TransmitTargeted(uint markIdent, uint arcanumIdent) => _tied?.TransmitTargeted(markIdent, arcanumIdent);
    public void ShowMsg(string msg) => _tied?.ShowMsg(msg);
    public void IncrementOccupied() => _tied?.IncrementOccupied();

    private void Release(ISimArcanaCastOps anticipated)
    {
        if (ReferenceEquals(_tied, anticipated))
            _tied = null;
    }

    private sealed class Lease(EngineArcanaCastOperationsSlot socket, ISimArcanaCastOps holder) : IDisposable
    {
        private EngineArcanaCastOperationsSlot? _socket = socket;

        public void Dispose() => Interlocked.Exchange(ref _socket, null)?.Release(holder);
    }
}

// Cast operations for an online session, wired with delegates into the realm session and the sim
internal sealed class OnlineArcanaCastOperations(
    ComponentNeedsService requirements,
    ClientThingChart objects,
    Func<uint> localPlayerId,
    Action stopCompletely,
    Action<uint> sendUntargeted,
    Action<uint, uint> sendTargeted,
    Action<string> displayMessage,
    Action incrementBusy,
    Func<bool> canSend) : ISimArcanaCastOps
{
    private readonly ComponentNeedsService _needs = requirements ?? throw new ArgumentNullException(nameof(requirements));
    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly Func<uint> _avatarIdent = localPlayerId ?? throw new ArgumentNullException(nameof(localPlayerId));
    private readonly Action _halt = stopCompletely ?? throw new ArgumentNullException(nameof(stopCompletely));
    private readonly Action<uint> _castingUntargeted = sendUntargeted ?? throw new ArgumentNullException(nameof(sendUntargeted));
    private readonly Action<uint, uint> _castingTargeted = sendTargeted ?? throw new ArgumentNullException(nameof(sendTargeted));
    private readonly Action<string> _say = displayMessage ?? throw new ArgumentNullException(nameof(displayMessage));
    private readonly Action _occupied = incrementBusy ?? throw new ArgumentNullException(nameof(incrementBusy));
    private readonly Func<bool> _online = canSend ?? throw new ArgumentNullException(nameof(canSend));

    public uint OwnAvatarIdent => _avatarIdent();
    public bool CanTransmit => _online();
    public bool HasNeededModules(uint arcanumIdent) => _needs.HasNeededComponents(arcanumIdent);

    public bool IsMarkCompatible(uint markIdent, SpellMeta arcanum, bool unhideMsg)
    {
        if (_objects.Get(markIdent) is not { } mark)
            return false;

        var ruling = CanonSpellTargetRules.Evaluate(OwnAvatarIdent, mark, arcanum);
        if (unhideMsg && !ruling.Allowed && ruling.Message is { } msg)
            _say(msg);
        return ruling.Allowed;
    }

    public void StopCompletely() => _halt();
    public void TransmitUntargeted(uint arcanumIdent) => _castingUntargeted(arcanumIdent);
    public void TransmitTargeted(uint markIdent, uint arcanumIdent) => _castingTargeted(markIdent, arcanumIdent);
    public void ShowMsg(string msg) => _say(msg);
    public void IncrementOccupied() => _occupied();
}
