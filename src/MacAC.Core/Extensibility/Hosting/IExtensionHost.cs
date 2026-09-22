using MacAC.Extensibility.Automation;
using MacAC.Extensibility.Loot;
using MacAC.Extensibility.Panels;
using MacAC.Extensibility.World;

namespace MacAC.Extensibility.Hosting;

public interface IExtensionLog
{
    void Info(string msg);

    void Warn(string msg);

    void Error(string msg, Exception? exception = null);
}

/// <summary>Per-extension key/value settings chosen for one launch.</summary>
public interface IExtensionSessionSettings
{
    IReadOnlyDictionary<string, string> SessPrefsFor(string extensionIdent);
}

public interface IExtensionHost
{
    bool HasWidget { get; }

    IExtensionLog Trace { get; }

    IWorldLedger State { get; }

    IWorldPulse Signals { get; }

    ITargetPicker Selection { get; }

    IPanelRegistry Widget { get; }

    IAutomationCockpit Automation { get; }

    ISlashCommandRegistry Commands => MuteSlashCommandRegistry.Instance;

    /// <summary>Durable files the host scopes to this extension's manifest id.</summary>
    IExtensionVault Depot => SealedVault.Instance;

    ILootRuleRegistry LootClassifiers => MuteLootRuleRegistry.Instance;

    IExtensionVault VtankProfiles => SealedVault.Instance;

    IReadOnlyDictionary<string, string> SessPrefs => NoPrefs;

    private static readonly IReadOnlyDictionary<string, string> NoPrefs =
        new Dictionary<string, string>();
}
