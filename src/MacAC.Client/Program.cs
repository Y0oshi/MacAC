using MacAC.Client;
using MacAC.Client.Extensions;
using MacAC.Client.Machine;
using Serilog;

// The graphical client boots in three moves: settle the host machine and its
// logging, decide where the game data and the session come from, then rig
// the window with its plugin host and run it until it closes.
var machine = GraphicalHubPlatformServices.Resolve();
machine.ConfigurePaneBackend();
ClientLaunch.OpenTrace(machine);

if (ClientLaunch.ScanKnobs(args, machine) is not { } knobs)
    return 2;

using ClientRig rig = new ClientRig(knobs, machine);
GraphicalExtensionSession extensions = GraphicalExtensionSession.Create(
    rig.Trails,
    knobs.Plugins,
    knobs.SessionId ?? "app",
    rig.Host,
    rig.Window.ConditionWriter,
    rig.RenderPacks);
rig.Window.BeginExtensionHosting(extensions);
try
{
    rig.Window.Run();
}
catch (NotSupportedException miss)
{
    // A capability gate (Vulkan, window backend) refused to start; exit 4 is
    // the launcher's cue to show the capability report.
    Log.Error("{GraphicalStartupFailure}", miss.Message);
    return 4;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
