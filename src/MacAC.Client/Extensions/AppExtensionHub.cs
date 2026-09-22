using MacAC.Extensibility.Automation;
using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.Loot;
using MacAC.Extensibility.Panels;
using MacAC.Extensibility.World;

namespace MacAC.Client.Extensions;

public sealed class AppExtensionHub(
    IExtensionLog trace,
    IWorldLedger phase,
    IWorldPulse signals,
    ITargetPicker pick,
    IPanelRegistry widget,
    IAutomationCockpit automation,
    IExtensionVault? depot = null,
    ISlashCommandRegistry? directives = null,
    ILootRuleRegistry? lootClassifiers = null,
    IExtensionVault? vtankProfiles = null) : IExtensionHost
{
    public bool HasWidget => true;
    public IExtensionLog Trace { get; } = trace;
    public IWorldLedger State { get; } = phase;
    public IWorldPulse Signals { get; } = signals;
    public ITargetPicker Selection { get; } = pick;
    public IPanelRegistry Widget { get; } = widget;
    public IAutomationCockpit Automation { get; } = automation;
    public IExtensionVault Depot { get; } = depot ?? SealedVault.Instance;
    public ISlashCommandRegistry Commands { get; } = directives ?? MuteSlashCommandRegistry.Instance;
    public ILootRuleRegistry LootClassifiers { get; } = lootClassifiers
            ?? MuteLootRuleRegistry.Instance;
    public IExtensionVault VtankProfiles { get; } = vtankProfiles ?? SealedVault.Instance;
}
