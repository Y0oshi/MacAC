namespace MacAC.Mechanics.Contracts;

/// <summary>One row of the shipped quest-contract table.</summary>
public sealed record QuestRow(
    uint Version,
    uint ContractId,
    string ContractName,
    string Description,
    string DescriptionProgress,
    string NameNpcStart,
    string NameNpcEnd,
    string QuestflagStamped,
    string QuestflagStarted,
    string QuestflagFinished,
    string QuestflagProgress,
    string QuestflagTimer,
    string QuestflagRepeatTime,
    uint LocationNpcStartCell = 0u,
    uint LocationNpcEndCell = 0u,
    /// <summary>Landcell of the quest area: the "Quest Location" row.</summary>
    uint LocationQuestAreaCell = 0u)
{
    /// <summary>What a lookup yields for an id the table does not know.</summary>
    public static readonly QuestRow Unknown = new(
        Version: 0u,
        ContractId: 0u,
        ContractName: "",
        Description: "",
        DescriptionProgress: "",
        NameNpcStart: "",
        NameNpcEnd: "",
        QuestflagStamped: "",
        QuestflagStarted: "",
        QuestflagFinished: "",
        QuestflagProgress: "",
        QuestflagTimer: "",
        QuestflagRepeatTime: "");
}

/// <summary>The whole contract table, keyed by contract id.</summary>
public sealed class QuestCatalogue(IReadOnlyDictionary<uint, QuestRow> contracts)
{
    public static readonly QuestCatalogue Empty = new(new Dictionary<uint, QuestRow>());

    public IReadOnlyDictionary<uint, QuestRow> Contracts { get; } = contracts;

    public int Count => Contracts.Count;

    public QuestRow Consult(uint contractIdent) =>
        Contracts.GetValueOrDefault(contractIdent) ?? QuestRow.Unknown;
}
