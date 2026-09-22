using MacAC.Client.Controls;
using MacAC.Client.Shell;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Presence;

namespace MacAC.Client.Fighting;

internal interface IOnlineFightingModeAuthority
{
    bool IsInWorld { get; }

    void TransmitEditFightingManner(FightingManner manner);
}

internal sealed class OnlineSessionFightingModeAuthority(OnlineSessionHarbor session)
    : IOnlineFightingModeAuthority
{
    private readonly OnlineSessionHarbor _session = session
        ?? throw new ArgumentNullException(nameof(session));

    public bool IsInWorld =>
        _session.IsInWorld && _session.CurrentSess is not null;

    public void TransmitEditFightingManner(FightingManner manner)
    {
        if (!IsInWorld)
        {
            throw new InvalidOperationException(
                "A combat-mode request needs an active in-world session");
        }

        _session.CurrentSess!.TransmitChangeCombatMode(manner);
    }
}

internal interface IFightingEquipmentSource
{
    IReadOnlyList<ClientThing> FetchSequencedEquipment();
}

internal sealed class AvatarFightingEquipmentSource(
    ClientThingChart objects,
    IAvatarIdentitySource identity) : IFightingEquipmentSource
{
    private readonly ClientThingChart _objects = objects
        ?? throw new ArgumentNullException(nameof(objects));
    private readonly IAvatarIdentitySource _identity = identity
        ?? throw new ArgumentNullException(nameof(identity));

    public IReadOnlyList<ClientThing> FetchSequencedEquipment() =>
        _objects.FetchEquippedBy(_identity.SrvOid);
}

internal interface IExplicitFightingModeIntentSink
{
    void AlertExplicitFightingMannerReq();
}

internal sealed class GearDealingFightingModeIntentSink(
    GearDealingDriver items) : IExplicitFightingModeIntentSink
{
    private readonly GearDealingDriver _gearList = items
        ?? throw new ArgumentNullException(nameof(items));

    public void AlertExplicitFightingMannerReq() =>
        _gearList.NotifyExplicitFightingModeRequest();
}

internal interface IOnlineFightingModeDirective
{
    void Toggle();
}

internal sealed class OnlineFightingModeDirectiveSlot : IOnlineFightingModeDirective
{
    private readonly object _latch = new();
    private IOnlineFightingModeDirective? _mark;
    private bool _deactivated;

    public void Bind(IOnlineFightingModeDirective mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null && !ReferenceEquals(_mark, mark))
            {
                throw new InvalidOperationException(
                    "Live combat-mode commands are by now bound");
            }

            _mark = mark;
        }
    }

    public void Unbind(IOnlineFightingModeDirective mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            if (ReferenceEquals(_mark, mark))
                _mark = null;
        }
    }

    public IDisposable BindOwned(IOnlineFightingModeDirective mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null)
            {
                throw new InvalidOperationException(
                    "Live combat-mode commands are by now bound");
            }

            _mark = mark;
        }

        return new ClientBinding(this, mark);
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _mark = null;
        }
    }

    public void Toggle()
    {
        lock (_latch)
        {
            if (!_deactivated)
                _mark?.Toggle();
        }
    }

    private sealed class ClientBinding(
        OnlineFightingModeDirectiveSlot holder,
        IOnlineFightingModeDirective anticipated) : IDisposable
    {
        private OnlineFightingModeDirectiveSlot? _holder = holder;
        private readonly IOnlineFightingModeDirective _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_anticipated);
    }
}

internal sealed class EngineFightingModeOperationsSlot
    : ISimFightingModeOps
{
    private ISimFightingModeOps? _holder;

    public IDisposable BindOwned(ISimFightingModeOps holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_holder is not null)
            throw new InvalidOperationException(
                "Runtime combat-mode operations are by now bound");
        _holder = holder;
        return new BindingDef(this, holder);
    }
    public IReadOnlyList<ClientThing> ObtainSequencedEquipment() =>
        _holder?.ObtainSequencedEquipment() ?? [];

    public bool IsInRealm => _holder?.IsInRealm == true;
    public void InformExplicitFightingMannerReq() =>
        _holder?.InformExplicitFightingMannerReq();
    public void DispatchEditFightingManner(FightingManner manner) =>
        _holder?.DispatchEditFightingManner(manner);

    private void Loosen(ISimFightingModeOps anticipated)
    {
        if (ReferenceEquals(_holder, anticipated))
            _holder = null;
    }

    private sealed class BindingDef(
        EngineFightingModeOperationsSlot socket,
        ISimFightingModeOps anticipated) : IDisposable
    {
        private EngineFightingModeOperationsSlot? _socket = socket;
        private readonly ISimFightingModeOps _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _socket, null)?.Loosen(_anticipated);
    }
}

internal sealed class OnlineFightingModeOperations(
    IOnlineFightingModeAuthority authority,
    IFightingEquipmentSource equipment,
    IExplicitFightingModeIntentSink itemIntent)
        : ISimFightingModeOps
{
    private readonly IOnlineFightingModeAuthority _arbiter = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly IFightingEquipmentSource _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
    private readonly IExplicitFightingModeIntentSink _gearIntent = itemIntent ?? throw new ArgumentNullException(nameof(itemIntent));

    public bool IsInRealm => _arbiter.IsInWorld;
    public IReadOnlyList<ClientThing> ObtainSequencedEquipment() =>
        _equipment.FetchSequencedEquipment();
    public void InformExplicitFightingMannerReq() =>
        _gearIntent.AlertExplicitFightingMannerReq();
    public void DispatchEditFightingManner(FightingManner manner) =>
        _arbiter.TransmitEditFightingManner(manner);
}

internal sealed class EngineFightingModeDirectiveBridge(
    SimFightingModeLedger owner,
    Action<string>? trace = null,
    Action<string>? toast = null,
    Action<string>? sysMsg = null) : IOnlineFightingModeDirective
{
    private readonly SimFightingModeLedger _holder = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly Action<string> _trace = trace ?? (_ => { });
    private readonly Action<string>? _toast = toast;
    private readonly Action<string> _sysMsg = sysMsg ?? (_ => { });

    public void Toggle()
    {
        var outcome = _holder.Toggle();
        if (outcome.Status == SimFightingModeRequestStatus.Inactive)
            return;

        if (outcome.Status == SimFightingModeRequestStatus.Rejected)
        {
            string notice = outcome.Notice ?? string.Empty;
            _trace($"combat: {notice}");
            _sysMsg(notice);
            return;
        }

        string msg = $"Combat mode {outcome.Mode}";
        _trace($"combat: {msg}");
        _toast?.Invoke(msg);
    }
}
