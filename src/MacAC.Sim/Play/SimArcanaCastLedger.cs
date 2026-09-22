using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Arcana;

namespace MacAC.Sim.Play;

public readonly record struct SimArcanaCastFinish(long Revision, uint SpellId, uint TargetObjectId, uint WeenieError)
{
    public bool IsSuccess => Revision is not 0 && WeenieError is 0u;
}

public interface ISimArcanaCastOps
{
    uint OwnAvatarIdent { get; }

    bool CanTransmit { get; }

    bool HasNeededModules(uint arcanumIdent);

    bool IsMarkCompatible(uint markIdent, SpellMeta arcanum, bool unhideMsg);

    void StopCompletely();

    void TransmitUntargeted(uint arcanumIdent);

    void TransmitTargeted(uint markIdent, uint arcanumIdent);

    void ShowMsg(string msg);

    void IncrementOccupied();
}

public enum CastingRequestOutcome
{
    Sent,
    UnknownSpell,
    NoTarget,
    IncompatibleTarget,
    MissingComponents,
    Unavailable,
}

public enum ArcanaCastTurnstile
{
    Unknown,
    NoTargetNeeded,
    TargetCompatible,
    TargetIncompatible,
    NoTargetSelected,
}

public sealed class SimArcanaCastLedger(Grimoire spellbook, PickPhase selection, ISimArcanaCastOps operations)
{
    private readonly Grimoire _grimoire = spellbook ?? throw new ArgumentNullException(nameof(spellbook));
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));
    private readonly ISimArcanaCastOps _ops = operations ?? throw new ArgumentNullException(nameof(operations));

    public uint? PreviousAskedArcanumIdent { get; private set; }
    public uint? PreviousAskedMarkIdent { get; private set; }
    public uint? QueuedArcanumIdent { get; private set; }
    public uint? QueuedMarkIdent { get; private set; }
    public SimArcanaCastFinish PreviousWrapUp { get; private set; }

    public event Action? StateChanged;

    public bool HasRequiredModules(uint arcanumIdent) => _ops.HasNeededModules(arcanumIdent);

    public bool IsMarkPrimed(uint arcanumIdent)
    {
        return EvaluateCastingLatch(arcanumIdent) is ArcanaCastTurnstile.NoTargetNeeded or ArcanaCastTurnstile.TargetCompatible;
    }

    public ArcanaCastTurnstile EvaluateCastingLatch(uint arcanumIdent)
    {
        if (!_grimoire.Knows(arcanumIdent) || !_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta arcanum))
            return ArcanaCastTurnstile.Unknown;
        if (arcanum.IsSelfTargeted || arcanum.IsUntargeted || arcanum.TargetMask is 0u)
            return ArcanaCastTurnstile.NoTargetNeeded;
        if (_pick.ChosenObjectTag is not (uint mark and not 0u))
            return ArcanaCastTurnstile.NoTargetSelected;
        return _ops.IsMarkCompatible(mark, arcanum, unhideMsg: false)
            ? ArcanaCastTurnstile.TargetCompatible
            : ArcanaCastTurnstile.TargetIncompatible;
    }

    public CastingRequestOutcome Cast(uint arcanumIdent)
    {
        if (!_grimoire.Knows(arcanumIdent) || !_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta arcanum))
            return Refuse("You do not know that spell.", CastingRequestOutcome.UnknownSpell);
        if (!_ops.HasNeededModules(arcanumIdent))
            return Refuse("You do not have all of this spell's components.", CastingRequestOutcome.MissingComponents);

        // Work out who the spell is aimed at: self, nobody, or the selection.
        uint? mark;
        bool untargeted;
        if (arcanum.IsSelfTargeted)
        {
            uint self = _ops.OwnAvatarIdent;
            mark = self is 0 ? null : self;
            untargeted = mark is null;
        }
        else if (arcanum.IsUntargeted || arcanum.TargetMask is 0)
        {
            mark = null;
            untargeted = true;
        }
        else
        {
            mark = _pick.ChosenObjectTag;
            untargeted = false;
            if (mark is null or 0)
                return Refuse("You must select a suitable target.", CastingRequestOutcome.NoTarget);
            if (!_ops.IsMarkCompatible(mark.Value, arcanum, unhideMsg: true))
                return CastingRequestOutcome.IncompatibleTarget;
        }

        if (!_ops.CanTransmit || QueuedArcanumIdent is not null)
            return Refuse("You cannot cast a spell right now.", CastingRequestOutcome.Unavailable);

        try
        {
            _ops.StopCompletely();
            PreviousAskedArcanumIdent = arcanumIdent;
            PreviousAskedMarkIdent = mark;
            QueuedArcanumIdent = arcanumIdent;
            QueuedMarkIdent = mark;
            if (untargeted)
                _ops.TransmitUntargeted(arcanumIdent);
            else
                _ops.TransmitTargeted(mark!.Value, arcanumIdent);
            _ops.IncrementOccupied();
        }
        catch
        {
            Drop();
            throw;
        }
        StateChanged?.Invoke();
        return CastingRequestOutcome.Sent;
    }

    /// <summary>The server's verdict on the pending cast; false when nothing was pending.</summary>
    public bool CompleteUse(uint weenieProblem)
    {
        if (QueuedArcanumIdent is not uint arcanumIdent)
            return false;

        PreviousWrapUp = new SimArcanaCastFinish(PreviousWrapUp.Revision + 1, arcanumIdent, QueuedMarkIdent ?? 0u, weenieProblem);
        QueuedArcanumIdent = null;
        QueuedMarkIdent = null;
        StateChanged?.Invoke();
        return true;
    }

    public void Reset()
    {
        bool altered = PreviousAskedArcanumIdent is not null || PreviousAskedMarkIdent is not null
            || QueuedArcanumIdent is not null || QueuedMarkIdent is not null || PreviousWrapUp.Revision is not 0;
        Drop();
        PreviousWrapUp = default;
        if (altered)
            StateChanged?.Invoke();
    }

    private CastingRequestOutcome Refuse(string msg, CastingRequestOutcome outcome)
    {
        _ops.ShowMsg(msg);
        return outcome;
    }

    private void Drop()
    {
        PreviousAskedArcanumIdent = null;
        PreviousAskedMarkIdent = null;
        QueuedArcanumIdent = null;
        QueuedMarkIdent = null;
    }
}
