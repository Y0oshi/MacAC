using MacAC.Sim.Presence;

namespace MacAC.Sim;

public interface ISimCoreDirectives
{
    ISimSessionDirectives Session { get; }

    ISimToonPickDirectives CharacterSelection
    {
        get
        {
            throw new NotSupportedException("This command adapter doesn't project character selection");
        }
    }

    ISimToonGenesisDirectives CharacterCreation
    {
        get
        {
            throw new NotSupportedException("This command adapter doesn't project character creation");
        }
    }

    ISimSelectionDirectives Selection { get; }

    ISimFightingDirectives Combat { get; }

    ISimArcanaDirectives Magic { get; }

    ISimLocomotionDirectives Movement { get; }

    ISimCommsDirectives Chat { get; }

    ISimPortalDirectives Portal { get; }

    ISimStashStateDirectives InventoryState { get; }

    ISimArcanabookDirectives Spellbook { get; }

    ISimToonDirectives Character { get; }

    ISimSocialDirectives Social { get; }

    ISimFellowsDirectives Fellowship { get; }

    ISimAllegianceDirectives Allegiance { get; }
}
