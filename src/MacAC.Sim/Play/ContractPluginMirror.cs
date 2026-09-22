using MacAC.Wire.Messages;
using MacAC.Mechanics.Contracts;
using MacAC.Extensibility.World;

namespace MacAC.Sim.Play;

public static class ContractPluginMirror
{
    public static IReadOnlyList<QuestContractFrame> Project(ISimContractLens contracts, QuestCatalogue? registry, DateTime instant)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var followed = contracts.FetchContracts();
        if (followed.Count is 0)
            return [];

        uint displayed = contracts.Snapshot.DisplayContractId;
        QuestContractFrame[] cycles = new QuestContractFrame[followed.Count];
        for (int idx = 0; idx < cycles.Length; ++idx)
        {
            ProjectLoop(followed, idx, registry, cycles, displayed, instant);
        }
        return cycles;
    }

    private static void ProjectLoop(IReadOnlyList<QuestTracker> followed, int idx, QuestCatalogue? registry, QuestContractFrame[] cycles, uint displayed, DateTime instant)
    {
        var tracker = followed[idx];
        QuestRow? rank = registry?.Consult(tracker.ContractId);
        cycles[idx] = new QuestContractFrame(
                        tracker.ContractId,
                        (uint)tracker.Stage,
                        tracker.Progress,
                        tracker.ContractId == displayed,
                        rank?.ContractName ?? string.Empty,
                        rank?.Description ?? string.Empty,
                        rank is null ? string.Empty : QuestProgressText.Build((uint)tracker.Stage, tracker.TimeWhenRepeats, tracker.ReceivedAt, rank, instant));
    }
}
