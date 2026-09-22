using System.Text.Json;
using MacAC.Client.Bootstrap;
using MacAC.Client.Machine;
using MacAC.Client.Secrets;
using Serilog;

namespace MacAC.Client;

internal static class ClientLaunch
{
    private const string SessSettingsBit = "--session-config";
    private const string DatDirectionVariable = "MACAC_DAT_DIR";

    internal static void OpenTrace(GraphicalHubPlatformServices machine)
    {
        var migrated = GraphicalLegacySetupMigrator.Migrate(machine.Paths);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
        foreach (string trail in migrated)
            Log.Information("migrated legacy graphical configuration to {Path}", trail);

        string natives = string.Join(
            ", ",
            machine.NativeDependencies.Select(dependency => $"{dependency.Feature}={dependency.PublishedFileName}"));
        Log.Information(
            "graphical platform {RuntimeIdentifier}; native closure: {NativeDependencies}",
            machine.RuntimeIdentifier,
            natives);
    }

    // Resolves the run's knobs, or null after logging why the invocation cannot proceed
    internal static EngineKnobs? ScanKnobs(string[] arguments, GraphicalHubPlatformServices machine)
    {
        string? settingsTrail = SessionSetupArgumentParsing.DistillBitVal(arguments, SessSettingsBit, out bool bitPresent);
        if (settingsTrail is null && bitPresent)
        {
            Log.Error("--session-config needs a value (a path to the session-config document)");
            return null;
        }

        string[] positional = SessionSetupArgumentParsing.WithoutBitAndVal(arguments, SessSettingsBit);
        string? datDirectionArgument = positional.FirstOrDefault();
        string? datDirectionEnviron = Environment.GetEnvironmentVariable(DatDirectionVariable);

        if (settingsTrail is null)
        {
            string? datDirection = datDirectionArgument ?? datDirectionEnviron;
            if (string.IsNullOrWhiteSpace(datDirection))
            {
                Log.Error("usage: MacAC.Client <dat-directory>  (or set MACAC_DAT_DIR)");
                return null;
            }

            return Proclaim(EngineKnobs.FromSurroundings(datDirection));
        }

        SessionSetup rig;
        SessionSpec sess;
        try
        {
            (rig, sess) = SessionSetupFetcher.Load(settingsTrail);
        }
        catch (Exception problem) when (problem is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or JsonException
            or SessionSetupException)
        {
            Log.Error("--session-config not valid: {Error}", problem.Message);
            return null;
        }

        string? settledDatDirection = Blank(rig.Process?.Content?.DatDirectory) ?? Blank(datDirectionArgument) ?? Blank(datDirectionEnviron);
        if (settledDatDirection is null)
        {
            Log.Error(
                "usage: MacAC.Client <dat-directory>  (or set MACAC_DAT_DIR, "
                + "or supply process.content.datDirectory in --session-config)");
            return null;
        }

        AppSecretSecret? secret = null;
        try
        {
            AppSecretPicker picker = new AppSecretPicker(
                Console.In,
                machine.Paths.Config,
                machine.OperatingSystem is GraphicalHubOperatingSys.Linux or GraphicalHubOperatingSys.MacOS);
            secret = picker.Resolve(sess.Id, sess.Credential);
            EngineKnobs knobs = EngineKnobs.FromSessSettings(
                settledDatDirection,
                Environment.GetEnvironmentVariable,
                settingsTrail,
                rig,
                sess,
                secret.Reveal());

            Log.Information(
                "--session-config {Path} present; overriding MACAC_LIVE*/MACAC_TEST_* "
                + "env-var live-session settings",
                settingsTrail);
            return Proclaim(knobs);
        }
        catch (AppSecretException problem)
        {
            Log.Error("--session-config credential not available: {Error}", problem.Message);
            return null;
        }
        finally
        {
            secret?.Dispose();
        }
    }

    private static EngineKnobs Proclaim(EngineKnobs knobs)
    {
        if (knobs.DevTools)
            Log.Information("MACAC_DEVTOOLS=1: enables the optional Vulkan validation/debug-utils extensions");
        return knobs;
    }

    private static string? Blank(string? val) => string.IsNullOrWhiteSpace(val) ? null : val;
}
