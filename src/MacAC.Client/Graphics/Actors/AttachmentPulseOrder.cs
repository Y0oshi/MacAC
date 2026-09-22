namespace MacAC.Client.Graphics;

internal sealed class AttachmentPulseOrder<TKey, TChild>
    where TKey : struct
{
    private readonly HashSet<TKey> _visiting = [];
    private readonly HashSet<TKey> _completed = [];
    private readonly HashSet<TKey> _failedSet = [];
    private readonly List<TKey> _failed = [];
    private readonly List<TKey> _subtree = [];
    private readonly Queue<TKey> _recoveryFifo = new();
    private readonly HashSet<TKey> _recoveryVisited = [];

    public IReadOnlyList<TKey> ForEachAncestorLead(
        Dictionary<TKey, TChild> descendants,
        Func<TChild, TKey?> ancestorOf,
        Func<TKey, bool> refresh)
    {
        ArgumentNullException.ThrowIfNull(descendants);
        ArgumentNullException.ThrowIfNull(ancestorOf);
        ArgumentNullException.ThrowIfNull(refresh);

        _visiting.Clear();
        _completed.Clear();
        _failedSet.Clear();
        _failed.Clear();
        foreach (TKey descendantIdent in descendants.Keys)
            Tour(descendantIdent, descendants, ancestorOf, refresh);
        return _failed;
    }

    public IReadOnlyList<TKey> GatherSubtreePostOrdering(
        Dictionary<TKey, TChild> descendants,
        TKey trunkIdent,
        Func<TChild, TKey?> ancestorOf)
    {
        _subtree.Clear();
        _visiting.Clear();
        Collect(trunkIdent, descendants, ancestorOf);
        return _subtree;
    }

    public void RealizeDescendants(
        TKey ancestorIdent,
        Func<TKey, IReadOnlyList<TKey>> descendantsWaitingForAncestor,
        Func<TKey, bool> realize)
    {
        ArgumentNullException.ThrowIfNull(descendantsWaitingForAncestor);
        ArgumentNullException.ThrowIfNull(realize);

        _recoveryFifo.Clear();
        _recoveryVisited.Clear();
        _recoveryFifo.Enqueue(ancestorIdent);
        _recoveryVisited.Add(ancestorIdent);
        while (_recoveryFifo.Count > 0)
        {
            TKey latestAncestor = _recoveryFifo.Dequeue();
            var waiting = descendantsWaitingForAncestor(latestAncestor);
            for (int idx = 0; idx < waiting.Count; ++idx)
            {
                TKey descendantIdent = waiting[idx];
                if (realize(descendantIdent) && _recoveryVisited.Add(descendantIdent))
                    _recoveryFifo.Enqueue(descendantIdent);
            }
        }
    }

    private void Collect(
        TKey trunkIdent,
        Dictionary<TKey, TChild> descendants,
        Func<TChild, TKey?> ancestorOf)
    {
        if (!_visiting.Add(trunkIdent))
            return;
        foreach ((TKey contenderIdent, TChild descendant) in descendants)
        {
            if (ancestorOf(descendant) is { } ancestor
                && EqualityComparer<TKey>.Default.Equals(ancestor, trunkIdent))
                Collect(contenderIdent, descendants, ancestorOf);
        }
        _subtree.Add(trunkIdent);
    }

    private void Tour(
        TKey descendantIdent,
        Dictionary<TKey, TChild> descendants,
        Func<TChild, TKey?> ancestorOf,
        Func<TKey, bool> refresh)
    {
        if (_completed.Contains(descendantIdent)
            || !descendants.TryGetValue(descendantIdent, out TChild? descendant))

            return;
        if (!_visiting.Add(descendantIdent))
            return;

        TKey? ancestorIdent = ancestorOf(descendant);
        if (ancestorIdent is { } affixedAncestorIdent && descendants.ContainsKey(affixedAncestorIdent))
            Tour(affixedAncestorIdent, descendants, ancestorOf, refresh);

        _visiting.Remove(descendantIdent);
        if (!_completed.Add(descendantIdent))
            return;

        bool succeeded = ancestorIdent is not { } ancestor
            || !_failedSet.Contains(ancestor);
        if (succeeded)
            succeeded = refresh(descendantIdent);
        if (!succeeded && _failedSet.Add(descendantIdent))
            _failed.Add(descendantIdent);
    }
}
