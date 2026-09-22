namespace MacAC.Extensibility.Automation;

public readonly record struct TrackedEnchantmentFacts(
    uint TargetObjectId,
    uint SpellId,
    uint Family,
    int Quality,
    bool IsUntargeted,
    double SecondsRemaining);

public interface IEnchantmentControls
{
    IReadOnlyList<TrackedEnchantmentFacts> Capture(uint markObjectIdent) =>
        Array.Empty<TrackedEnchantmentFacts>();

    bool AnnounceCasting(uint markObjectIdent, uint arcanumIdent, double intervalSecs) => false;
}
