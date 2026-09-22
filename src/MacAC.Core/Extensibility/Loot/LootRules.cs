using MacAC.Extensibility.Automation;

namespace MacAC.Extensibility.Loot;

/// <summary>What to do with an item, in VTank's public loot vocabulary.</summary>
public enum LootVerb
{
    NoLoot = 0,
    Keep = 1,
    Salvage = 2,
    Sell = 3,
    Read = 4,
    User1 = 5,
    User2 = 6,
    User3 = 7,
    User4 = 8,
    User5 = 9,
    KeepUpTo = 10,
}

public readonly record struct LootJudgementContext(
    PackEntry Item,
    ItemPropertySheet Properties,
    IReadOnlyList<PackEntry> OwnedItems);

public readonly record struct LootJudgement(
    bool Matched,
    LootVerb Action,
    string RuleName = "",
    int Priority = 0,
    int KeepCount = 0);

public readonly record struct LootedEntry(
    PackEntry Item,
    LootVerb Action);

/// <summary>An extension-supplied loot decision maker.</summary>
public interface ILootRule
{
    LootJudgement Classify(in LootJudgementContext ctx);

    void OnLooted(in LootedEntry gear)
    {
    }

    void OnGearRemoved(uint objectIdent)
    {
    }
}

public readonly record struct LootRuleFacts(
    string Id,
    string DisplayName);

public interface ILootRuleRegistry
{
    IReadOnlyList<LootRuleFacts> Available => Array.Empty<LootRuleFacts>();

    IDisposable Register(string classifierIdent, string readoutLabel, ILootRule classifier) =>
        throw new NotSupportedException("Loot classifiers are not available");

    bool TryClassify(
        string classifierIdent,
        in LootJudgementContext ctx,
        out LootJudgement taxonomy)
    {
        taxonomy = default;
        return false;
    }

    bool TryAlertLooted(string classifierIdent, in LootedEntry gear) => false;

    bool TryAlertGearRemoved(string classifierIdent, uint objectIdent) => false;
}

public sealed class MuteLootRuleRegistry : ILootRuleRegistry
{
    public static MuteLootRuleRegistry Instance { get; } = new();

    private MuteLootRuleRegistry()
    {
    }
}
