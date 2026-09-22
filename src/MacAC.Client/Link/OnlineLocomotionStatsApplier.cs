namespace MacAC.Client.Link;

internal sealed class OnlineLocomotionStatsApplier(
    SimAvatarLocomotionLedger movement,
    SimLocomotionSkillLedger skills,
    Action<string> log)
{
    private readonly SimAvatarLocomotionLedger _movement = movement
        ?? throw new ArgumentNullException(nameof(movement));
    private readonly SimLocomotionSkillLedger _aptitudes = skills
        ?? throw new ArgumentNullException(nameof(skills));
    private readonly Action<string> _trace = log
        ?? throw new ArgumentNullException(nameof(log));
    private readonly StaminaExhaustionEdgeLedger _staminaExhaustion = new();

    public void Reset() => _staminaExhaustion.Reset();

    public SimLocomotionStatsApplication Apply(string cause)
    {
        var verdict =
            _movement.ImposeToonTravelStats(_aptitudes);
        switch (verdict)
        {
            case SimLocomotionStatsApplication.DroppedNoController:
            case SimLocomotionStatsApplication.DroppedIncompleteSnapshot:
                // Byte-identical to the pre-F1 ApplyTo=false silent skip.
                return verdict;
            case SimLocomotionStatsApplication.DroppedDisplacedController:
                _trace(
                    $"player: dropped displaced movement {cause} — the "
                    + "Runtime movement controller is terminal");
                return verdict;
        }

        var capture = _aptitudes.Snapshot;
        if (_staminaExhaustion.Observe(capture.CurrentStamina)
            && verdict is SimLocomotionStatsApplication.AppliedLive)

            _movement.RelayExhaustion();

        _trace(
            $"player: applied server movement {cause} "
            + $"run={capture.RunSkill} jump={capture.JumpSkill} "
            + $"burden={capture.Burden:F2} stamina={capture.CurrentStamina}");
        return verdict;
    }
}
