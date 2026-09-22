using MacAC.Assets;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Arcana;

/// <summary>One component of a spell's formula and whether the avatar currently carries it.</summary>
public readonly record struct ArcanaExamineComponent(uint SpellComponentId, SpellComponentCard Descriptor, bool Owned);

public sealed class ArcanaEngine : IDisposable
{
    private readonly ComponentNeedsService _needs;
    private IDisposable? _castingTenancy;

    private ArcanaEngine(ArcanaCatalog registry, SimArcanaCastLedger casting, ComponentNeedsService needs, IDisposable castingTenancy)
    {
        Registry = registry;
        Casting = casting;
        _needs = needs;
        _castingTenancy = castingTenancy;
    }

    public ArcanaCatalog Registry { get; }
    public SimArcanaCastLedger Casting { get; }

    /// <summary>The spell's appropriate formula as examine rows; unknown components are skipped.</summary>
    public IReadOnlyList<ArcanaExamineComponent> FetchExamineModules(uint arcanumIdent)
    {
        var equation = _needs.FetchAppropriateEquation(arcanumIdent);
        if (equation.Count is 0)
            return [];

        var ranks = new List<ArcanaExamineComponent>(equation.Count);
        foreach (uint moduleIdent in equation)
        {
            if (moduleIdent is not 0u && Registry.TryFetchModuleByArcanumModuleIdent(moduleIdent, out SpellComponentCard card))
                ranks.Add(new ArcanaExamineComponent(moduleIdent, card, _needs.IsModulePossessed(moduleIdent)));
        }

        return ranks;
    }

    public void Reset() => Casting.Reset();

    public void Dispose() => Interlocked.Exchange(ref _castingTenancy, null)?.Dispose();

    internal static ArcanaEngine Create(
        ArcanaCatalog registry,
        SimArcanaCastLedger casting,
        EngineArcanaCastOperationsSlot opsSocket,
        ClientThingChart objects,
        Func<uint> ownAvatarIdent,
        Func<string> acctLabel,
        Action haltCompletely,
        Action<uint> transmitUntargeted,
        Action<uint, uint> transmitTargeted,
        Action<string> readoutMsg,
        Action incrementOccupied,
        Func<bool> canTransmit)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(casting);
        ArgumentNullException.ThrowIfNull(opsSocket);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(readoutMsg);

        var needs = registry.BuildRequirementService(objects, ownAvatarIdent, acctLabel);
        var ops = new OnlineArcanaCastOperations(
            needs, objects, ownAvatarIdent, haltCompletely, transmitUntargeted, transmitTargeted, readoutMsg, incrementOccupied, canTransmit);
        return new ArcanaEngine(registry, casting, needs, opsSocket.BindOwned(ops));
    }
}
