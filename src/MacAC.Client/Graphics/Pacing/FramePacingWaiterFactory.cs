using MacAC.Client.Machine;

namespace MacAC.Client.Graphics;

internal interface IFramePacingWaiterMint
{
    ICyclePacingPauser Create();
}

// The sole startup-time platform selector for software frame waits
internal sealed class PlatformFramePacingWaiterMint
    : IFramePacingWaiterMint
{
    private readonly GraphicalHubOperatingSys _operatingSystem;

    internal PlatformFramePacingWaiterMint(
        GraphicalHubOperatingSys operatingSys)
    {
        _operatingSystem = operatingSys;
    }

    internal static PlatformFramePacingWaiterMint ForCurrentProcess() =>
        new(GraphicalHubPlatformServices.SenseOperatingSys());

    public ICyclePacingPauser Create()
    {
        return _operatingSystem switch
        {
            GraphicalHubOperatingSys.Windows =>
                PanesHiResolutionCyclePacingPauser.Create(),
            GraphicalHubOperatingSys.Linux =>
                LinuxMonotonicCyclePacingPauser.Create(),
            GraphicalHubOperatingSys.MacOS =>
                MacMonotonicCyclePacingPauser.Create(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(_operatingSystem)),
        };
    }
}
