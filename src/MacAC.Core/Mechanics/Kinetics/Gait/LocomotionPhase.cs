namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>A motion and the speed it was requested at.</summary>
public sealed class MotionRow(uint locomotion, float paceMod)
{
    public uint Locomotion = locomotion;
    public float PaceMod = paceMod;
}

public sealed class LocomotionPhase
{
    public uint Style;

    public uint Substate;

    public float SubstateMod = 1f;

    private readonly LinkedList<MotionRow> _modifiers = new(); // modifier_head - push-front stack
    private readonly LinkedList<MotionRow> _actions = new();   // action_head/tail - FIFO

    public LocomotionPhase()
    {
    }

    public LocomotionPhase(LocomotionPhase another)
    {
        Style = another.Style;
        Substate = another.Substate;
        SubstateMod = another.SubstateMod;
        foreach (MotionRow rank in another._modifiers)
            _modifiers.AddLast(new MotionRow(rank.Locomotion, rank.PaceMod));
        foreach (MotionRow rank in another._actions)
            _actions.AddLast(new MotionRow(rank.Locomotion, rank.PaceMod));
    }

    public IEnumerable<MotionRow> Modifiers => _modifiers;

    public IEnumerable<MotionRow> Actions => _actions;

    public void AppendModifierNoVerify(uint locomotion, float paceMod) => _modifiers.AddFirst(new MotionRow(locomotion, paceMod));

    /// <summary>Pushes a modifier unless it is already active or is the substate itself.</summary>
    public bool AppendModifier(uint locomotion, float paceMod)
    {
        for (var joint = _modifiers.First; joint is not null; joint = joint.Next)
        {
            if (joint.Value.Locomotion == locomotion)
                return false;
        }
        if (Substate == locomotion)
            return false;

        AppendModifierNoVerify(locomotion, paceMod);
        return true;
    }

    public void DropModifier(MotionRow listing) => _modifiers.Remove(listing);

    public void WipeModifiers() => _modifiers.Clear();

    public void AttachAct(uint locomotion, float paceMod) => _actions.AddLast(new MotionRow(locomotion, paceMod));

    /// <summary>Pops the oldest queued action, or 0 when none is waiting.</summary>
    public uint DropActFront()
    {
        if (_actions.First is not { } front)
            return 0;
        _actions.RemoveFirst();
        return front.Value.Locomotion;
    }

    public void WipeActs() => _actions.Clear();
}
