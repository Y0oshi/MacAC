namespace MacAC.Sim;

public readonly record struct SimHotkeyDirective(int Index, uint ObjectId, uint SpellId);

public interface ISimStashStateDirectives
{
    SimDirectiveResult AttachShortcut(SimEpochTicket anticipatedGen, in SimHotkeyDirective directive);

    SimDirectiveResult DeleteShortcut(SimEpochTicket anticipatedGen, int ordinal);
}

public interface ISimArcanabookDirectives
{
    SimDirectiveResult AddFavorite(SimEpochTicket anticipatedGen, int tabOrdinal, int locus, uint arcanumIdent);

    SimDirectiveResult RemoveFavorite(SimEpochTicket anticipatedGen, int tabOrdinal, uint arcanumIdent);

    SimDirectiveResult AssignSift(SimEpochTicket anticipatedGen, uint filters);

    SimDirectiveResult DiscardArcanum(SimEpochTicket anticipatedGen, uint arcanumIdent);

    SimDirectiveResult SetDesiredComponent(SimEpochTicket anticipatedGen, uint moduleIdent, uint quantity);

    SimDirectiveResult WipeWantedModules(SimEpochTicket anticipatedGen);
}

public enum SimProgressionKind
{
    Attribute,
    Vital,
    Skill,
    TrainSkill,
}

public readonly record struct SimProgressionDirective(SimProgressionKind Kind, uint StatId, ulong Cost);

public interface ISimToonDirectives
{
    SimDirectiveResult Advance(SimEpochTicket anticipatedGen, in SimProgressionDirective directive);

    SimDirectiveResult AssignSingleKnob(SimEpochTicket anticipatedGen, uint knobIdent, bool val);

    SimDirectiveResult PersistKnobs(SimEpochTicket anticipatedGen);

    SimDirectiveResult SetTitle(SimEpochTicket anticipatedGen, uint bannerIdent);
}
