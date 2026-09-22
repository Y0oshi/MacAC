using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Parent/child attachment: children ride their root's placement and cells.</summary>
public sealed partial class ProxyRegistry
{
    private const int UpperFastenChainZDepth = 64;

    public bool FastenDescendant(
        uint descendantActorIdent,
        uint trunkActorIdent,
        IReadOnlyList<ProxyShape> descendantPieceArr)
    {
        ArgumentNullException.ThrowIfNull(descendantPieceArr);
        if (!TryVetFasten(descendantActorIdent, trunkActorIdent))
            return false;

        if (_ancestorOf.ContainsKey(descendantActorIdent))
            UnfastenDescendantCore(descendantActorIdent, dropFromAncestorRoster: true);

        _ancestorOf[descendantActorIdent] = trunkActorIdent;
        if (!_descendantsOf.TryGetValue(trunkActorIdent, out List<uint>? siblings))
        {
            siblings = new List<uint>();
            _descendantsOf[trunkActorIdent] = siblings;
        }
        siblings.Add(descendantActorIdent);
        _descendantForms[descendantActorIdent] = descendantPieceArr;

        BroadcastDescendantListings(descendantActorIdent);
        ProgressAlterationRev();
        return true;
    }

    public bool UnfastenDescendant(uint descendantActorIdent)
    {
        return UnfastenDescendantCore(descendantActorIdent, dropFromAncestorRoster: true);
    }

    private bool UnfastenDescendantCore(uint descendantActorIdent, bool dropFromAncestorRoster)
    {
        if (!_ancestorOf.TryGetValue(descendantActorIdent, out uint ancestorIdent))
            return false;

        if (_descendantsOf.TryGetValue(descendantActorIdent, out List<uint>? grandchildren)
            && grandchildren.Count > 0)
        {
            uint[] toUnfasten = grandchildren.ToArray();
            for (int idx = 0; idx < toUnfasten.Length; ++idx)
                UnfastenDescendantCore(toUnfasten[idx], dropFromAncestorRoster: false);
            _descendantsOf.Remove(descendantActorIdent);
        }

        if (_canonChambersByHolder.TryGetValue(descendantActorIdent, out List<uint>? chambers))
        {
            DiscardCanonRanks(descendantActorIdent, chambers);
            _canonChambersByHolder.Remove(descendantActorIdent);
        }
        _descendantForms.Remove(descendantActorIdent);
        _ancestorOf.Remove(descendantActorIdent);

        if (dropFromAncestorRoster
            && _descendantsOf.TryGetValue(ancestorIdent, out List<uint>? siblings))
        {
            siblings.Remove(descendantActorIdent);
            if (siblings.Count is 0)
                _descendantsOf.Remove(ancestorIdent);
        }
        ProgressAlterationRev(); // see AttachChild (arch F2)
        return true;
    }

    private bool TryVetFasten(uint descendantActorIdent, uint ancestorIdent)
    {
        if (descendantActorIdent == ancestorIdent)
            return false;

        uint latest = ancestorIdent;
        for (int zDepth = 0; zDepth < UpperFastenChainZDepth; ++zDepth)
        {
            if (!_ancestorOf.TryGetValue(latest, out uint upcoming))
                return true;
            if (upcoming == descendantActorIdent)
                return false;
            latest = upcoming;
        }
        return false;
    }

    private uint LocateFastenTrunk(uint actorIdent)
    {
        uint latest = actorIdent;
        for (int zDepth = 0; zDepth < UpperFastenChainZDepth; ++zDepth)
        {
            if (!_ancestorOf.TryGetValue(latest, out uint ancestor))
                return latest;
            latest = ancestor;
        }
        return latest;
    }

    private void BroadcastDescendantListings(uint descendantActorIdent)
    {
        if (_canonChambersByHolder.TryGetValue(descendantActorIdent, out List<uint>? earlierChambers))
        {
            DiscardCanonRanks(descendantActorIdent, earlierChambers);
            _canonChambersByHolder.Remove(descendantActorIdent);
        }
        if (!_descendantForms.TryGetValue(descendantActorIdent, out IReadOnlyList<ProxyShape>? pieceArr))
            return;

        uint trunk = LocateFastenTrunk(descendantActorIdent);
        if (trunk == descendantActorIdent
            || !_canonChambersByHolder.TryGetValue(trunk, out List<uint>? trunkChambers)
            || trunkChambers.Count is 0)

            return;

        _canonChambersByHolder[descendantActorIdent] = trunkChambers;
        BroadcastCanonRanks(descendantActorIdent, trunkChambers, pieceArr);
    }

    private void RepublishAffixedDescendants(uint trunkIdent)
    {
        if (!_descendantsOf.TryGetValue(trunkIdent, out List<uint>? descendants)
            || descendants.Count is 0)

            return;
        for (int idx = 0; idx < descendants.Count; ++idx)
        {
            uint descendantIdent = descendants[idx];
            BroadcastDescendantListings(descendantIdent);
            RepublishAffixedDescendants(descendantIdent);
        }
    }
}
