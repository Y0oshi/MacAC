using MacAC.Extensibility.World;

namespace MacAC.Mechanics.PluginHosting;

/// <summary>The entity list exposed to plugins, kept dense with swap-remove.</summary>
public sealed class RealmPlayPhase : IWorldLedger
{
    private readonly List<EntityFrame> _cycles = [];
    private readonly Dictionary<uint, int> _socketByIdent = [];

    public IReadOnlyList<EntityFrame> Entities => _cycles;

    public Func<IReadOnlyList<QuestContractFrame>>? ContractsSrc { get; set; }

    public IReadOnlyList<QuestContractFrame> Contracts => ContractsSrc?.Invoke() ?? [];

    public void Add(EntityFrame capture)
    {
        if (_socketByIdent.TryGetValue(capture.Id, out int socket))
        {
            _cycles[socket] = capture;
            return;
        }
        _socketByIdent.Add(capture.Id, _cycles.Count);
        _cycles.Add(capture);
    }

    public bool DropByIdent(uint ident)
    {
        if (!_socketByIdent.Remove(ident, out int socket))
            return false;

        int previous = _cycles.Count - 1;
        if (socket != previous)
        {
            EntityFrame moved = _cycles[previous];
            _cycles[socket] = moved;
            _socketByIdent[moved.Id] = socket;
        }
        _cycles.RemoveAt(previous);
        return true;
    }

    public void Clear()
    {
        _cycles.Clear();
        _socketByIdent.Clear();
    }
}
