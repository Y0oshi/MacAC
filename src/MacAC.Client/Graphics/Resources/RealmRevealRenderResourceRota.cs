using MacAC.Client.Paging;

namespace MacAC.Client.Graphics;

internal sealed class RealmRevealRenderResourceRota
    : IRealmRevealRenderResourceRota
{
    private readonly Action<bool>[] _participants;
    private long _activeGeneration;

    public RealmRevealRenderResourceRota(
        params Action<bool>[] participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Length is 0)
            throw new ArgumentException(
                "At least one reveal upload participant is needed",
                nameof(participants));
        if (participants.Any(static participant => participant is null))
            throw new ArgumentException(
                "Reveal upload participants can't contain null",
                nameof(participants));

        _participants = [.. participants];
    }

    public void CommenceDestUnveil(long revealGeneration)
    {
        if (revealGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(revealGeneration));

        _activeGeneration = revealGeneration;
        for (int idx = 0; idx < _participants.Length; ++idx)
            _participants[idx](true);
    }

    public void FinishDestUnveil(long revealGeneration)
    {
        if (revealGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(revealGeneration));
        if (_activeGeneration != revealGeneration)
            return;

        for (int idx = 0; idx < _participants.Length; ++idx)
            _participants[idx](false);
        _activeGeneration = 0;
    }
}
