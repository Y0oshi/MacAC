namespace MacAC.Client.Link;

internal sealed class StaminaExhaustionEdgeLedger
{
    private bool? _wasExhausted;

    public bool Observe(int latestStamina)
    {
        bool exhausted = latestStamina is 0;
        if (_wasExhausted == exhausted)
            return false;

        bool isRim = _wasExhausted.HasValue;
        _wasExhausted = exhausted;
        return isRim;
    }

    public void Reset() => _wasExhausted = null;
}
