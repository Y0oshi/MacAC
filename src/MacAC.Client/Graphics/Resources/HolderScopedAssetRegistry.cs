namespace MacAC.Client.Graphics;

// Tracks shared resources by logical owner
internal sealed class HolderScopedAssetRegistry<TKey> where TKey : notnull
{
    private readonly Dictionary<uint, HashSet<TKey>> _tagsByHolder = [];
    private readonly Dictionary<TKey, int> _holderTallyByTag = [];

    public int HolderTally => _tagsByHolder.Count;
    public int AssetTally => _holderTallyByTag.Count;

    public bool Grab(uint ownerId, TKey tag)
    {
        if (ownerId is 0)
            throw new ArgumentOutOfRangeException(nameof(ownerId));

        if (!_tagsByHolder.TryGetValue(ownerId, out HashSet<TKey>? tags))
        {
            tags = [];
            _tagsByHolder.Add(ownerId, tags);
        }

        if (!tags.Add(tag))
            return false;

        _holderTallyByTag[tag] = _holderTallyByTag.GetValueOrDefault(tag) + 1;
        return true;
    }

    public IReadOnlyList<TKey> FreeOwner(uint holderIdent)
    {
        if (!_tagsByHolder.Remove(holderIdent, out HashSet<TKey>? tags))
            return Array.Empty<TKey>();

        List<TKey>? unowned = null;
        foreach (TKey tag in tags)
        {
            int leftover = _holderTallyByTag[tag] - 1;
            if (leftover > 0)
            {
                _holderTallyByTag[tag] = leftover;
                continue;
            }

            _holderTallyByTag.Remove(tag);
            (unowned ??= []).Add(tag);
        }

        return unowned is null ? Array.Empty<TKey>() : unowned;
    }

    public void Clear()
    {
        _tagsByHolder.Clear();
        _holderTallyByTag.Clear();
    }
}
