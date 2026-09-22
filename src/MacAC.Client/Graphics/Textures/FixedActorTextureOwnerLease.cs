using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics;

internal sealed class FixedActorTextureOwnerLease : IDisposable
{
    private readonly IActorTextureLifetime _lifespan;
    private readonly uint _holderOwnIdent;
    private bool _engaged;

    public FixedActorTextureOwnerLease(IActorTextureLifetime lifetime, uint ownerLocalId)
    {
        _lifespan = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        if (ownerLocalId is 0)
            throw new ArgumentOutOfRangeException(nameof(ownerLocalId));
        _holderOwnIdent = ownerLocalId;
    }

    public void Replace(bool hasSubstitute)
    {
        if (_engaged)
            _lifespan.FreeHolder(_holderOwnIdent);
        _engaged = hasSubstitute;
    }

    public void Dispose() => Replace(hasSubstitute: false);
}
