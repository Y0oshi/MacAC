using MacAC.Dat;
using System.Collections.Frozen;
using MacAC.Mechanics.Contracts;
using DatContract =  MacAC.Dat.ContractSpec;
using DatContractTable =  MacAC.Dat.ContractBook;

namespace MacAC.Assets;

/// <summary>Projects the portal's contract table into the quest catalogue the client shows.</summary>
public static class QuestTableReader
{
    public const uint ContractChartDid = 0x0E00001Du;

    public static QuestCatalogue Load(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        var chart = datFiles.Get<DatContractTable>(ContractChartDid);
        if (chart is null || chart.Contracts.Count is 0)
            return QuestCatalogue.Empty;

        var ranks = new Dictionary<uint, QuestRow>(chart.Contracts.Count);
        foreach ((uint tag, DatContract contract) in chart.Contracts)
            ranks[tag] = Project(contract);
        return new QuestCatalogue(ranks.ToFrozenDictionary());
    }

    private static QuestRow Project(DatContract contract)
    {
        return new(
        contract.Version,
        contract.ContractId,
        contract.Name ?? string.Empty,
        contract.Description ?? string.Empty,
        contract.DescriptionProgress ?? string.Empty,
        contract.NpcStartName ?? string.Empty,
        contract.NpcEndName ?? string.Empty,
        contract.QuestflagStamped ?? string.Empty,
        contract.QuestflagStarted ?? string.Empty,
        contract.QuestflagFinished ?? string.Empty,
        contract.QuestflagProgress ?? string.Empty,
        contract.QuestflagTimer ?? string.Empty,
        contract.QuestflagRepeatTime ?? string.Empty,
        contract.NpcStart?.CellId ?? 0u,
        contract.NpcEnd?.CellId ?? 0u,
        contract.QuestArea?.CellId ?? 0u);
    }
}
