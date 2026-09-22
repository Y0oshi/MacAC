namespace MacAC.Extensibility.Automation;

public enum CastGate
{
    Unavailable = 0,
    Ready,
    NotKnown,
    Busy,
    NoTargetSelected,
    TargetIncompatible,
    /// <summary>The host said no for a reason this enum does not model.</summary>
    Refused,
}

public enum CastRequestOutcome
{
    Sent = 0,
    UnknownSpell,
    NoTarget,
    IncompatibleTarget,
    MissingComponents,
    Unavailable,
}

/// <summary>One server receipt for a cast the extension asked for.</summary>
public readonly record struct CastReceipt(
    long Revision,
    uint SpellId,
    uint TargetObjectId,
    uint WeenieError)
{
    public bool IsSuccess => Revision is not 0 && WeenieError is 0u;
}

public interface ICastingControls
{
    bool IsCasting { get; }

    CastReceipt LastCompletion => default;

    CastGate EvaluateGate(uint arcanumIdent);

    bool Cast(uint arcanumIdent);

    CastGate EvaluateGate(uint arcanumIdent, uint markObjectIdent) => CastGate.Refused;

    bool Cast(uint arcanumIdent, uint markObjectIdent) => false;

    CastRequestOutcome RequestCast(uint arcanumIdent)
    {
        return Cast(arcanumIdent) ? CastRequestOutcome.Sent : CastRequestOutcome.Unavailable;
    }

    CastRequestOutcome RequestCast(uint arcanumIdent, uint markObjectIdent)
    {
        return Cast(arcanumIdent, markObjectIdent)
            ? CastRequestOutcome.Sent
            : CastRequestOutcome.Unavailable;
    }

    bool HasModules(uint arcanumIdent) => true;
}
