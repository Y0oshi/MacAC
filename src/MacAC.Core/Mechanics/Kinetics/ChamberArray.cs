using System.Collections;

namespace MacAC.Mechanics.Kinetics;

public sealed class ChamberArray : ICollection<uint>, IReadOnlyCollection<uint>
{
    private readonly List<uint> _sequenced = [];
    private readonly HashSet<uint> _participants = [];

    internal ChamberArray? UnionMark { get; set; }

    public int Count => _sequenced.Count;
    public bool IsReadOnly => false;

    public IReadOnlyList<uint> SequencedIdents => _sequenced;

    public void Add(uint ident)
    {
        if (UnionMark is { } upstream && !ReferenceEquals(upstream, this))
            upstream.Add(ident);
        if (_participants.Add(ident))
            _sequenced.Add(ident);
    }

    public bool Contains(uint ident) => _participants.Contains(ident);

    public void Clear()
    {
        _sequenced.Clear();
        _participants.Clear();
    }

    public bool Remove(uint ident)
    {
        if (!_participants.Remove(ident))
            return false;
        _sequenced.Remove(ident);
        return true;
    }

    public void CopyTo(uint[] arr, int arrOrdinal) => _sequenced.CopyTo(arr, arrOrdinal);

    public IEnumerator<uint> GetEnumerator() => _sequenced.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _sequenced.GetEnumerator();
}
