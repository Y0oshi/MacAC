using MacAC.Sim.Presence;

namespace MacAC.Sim;

public interface ISimToonGenesisDirectives
{
    SimDirectiveResult PickLineage(SimEpochTicket anticipatedGen, uint lineageIdent);

    SimDirectiveResult PickGender(SimEpochTicket anticipatedGen, uint genderTag);

    SimDirectiveResult PickBlueprint(SimEpochTicket anticipatedGen, uint blueprintOrdinal);

    SimDirectiveResult AssignAttr(SimEpochTicket anticipatedGen, GenesisTraitId attrIdent, int val);

    SimDirectiveResult AssignAttrLock(SimEpochTicket anticipatedGen, GenesisTraitId attrIdent, bool bolted);

    SimDirectiveResult TrainSkill(SimEpochTicket anticipatedGen, uint aptitudeIdent);

    SimDirectiveResult SpecializeAptitude(SimEpochTicket anticipatedGen, uint aptitudeIdent);

    SimDirectiveResult UntrainAptitude(SimEpochTicket anticipatedGen, uint aptitudeIdent);

    SimDirectiveResult AssignLooksOrdinal(SimEpochTicket anticipatedGen, GenesisAppearanceSlot socket, uint ordinal);

    SimDirectiveResult AssignShade(SimEpochTicket anticipatedGen, GenesisShadeSlot socket, double val);

    SimDirectiveResult PickBeginArea(SimEpochTicket anticipatedGen, int beginAreaOrdinal);

    SimDirectiveResult AssignLabel(SimEpochTicket anticipatedGen, string label);

    SimDirectiveResult AssignSocket(SimEpochTicket anticipatedGen, uint socket);

    SimDirectiveResult Finish(SimEpochTicket anticipatedGen, bool confirmUnspentCredits = false);

    SimDirectiveResult AcknowledgeRejection(SimEpochTicket anticipatedGen);

    SimDirectiveResult RandomizeToon(SimEpochTicket anticipatedGen);

    SimDirectiveResult RandomizeLooks(SimEpochTicket anticipatedGen);

    SimDirectiveResult RandomizeClothing(SimEpochTicket anticipatedGen);
}
