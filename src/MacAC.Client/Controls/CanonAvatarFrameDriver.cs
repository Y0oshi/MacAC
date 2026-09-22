using MacAC.Client.Pulse;
using MacAC.Sim;

namespace MacAC.Client.Controls;

internal sealed class CanonAvatarFrameDriver : IPostWireDirectiveFramePhase
{
    private readonly SimAvatarFrameDriver _runtime;

    public readonly record struct DisplayFrame(
        LocomotionResult Movement,
        bool Hidden,
        bool AdvancedBeforeNetwork);

    internal bool ConcealedPiecePostureStale =>
        _runtime.ConcealedPiecePostureStale;

    public CanonAvatarFrameDriver(
        IAvatarFrameEngine runtime,
        ILocomotionInputSource movementInput)
    {
        _runtime = new SimAvatarFrameDriver(
            runtime ?? throw new ArgumentNullException(nameof(runtime)),
            movementInput
                ?? throw new ArgumentNullException(nameof(movementInput)));
    }

    public CanonAvatarFrameDriver(
        SimCore playCore,
        IAvatarFrameEngine runtime,
        ILocomotionInputSource movementInput)
    {
        ArgumentNullException.ThrowIfNull(playCore);
        _runtime = playCore.BuildOwnAvatarCycleDriver(
            runtime ?? throw new ArgumentNullException(nameof(runtime)),
            movementInput
                ?? throw new ArgumentNullException(nameof(movementInput)));
    }

    // Advances the existing local object exactly once on the object side of the inbound-network
    // barrier
    public void AdvanceBeforeNetwork(float diffSecs)
        => _runtime.AdvanceBeforeNetwork(diffSecs);

    public void ExecutePostNetworkDirectiveStage()
        => _runtime.PerformPostNetworkDirectiveStage();

    public bool TryGetPresentationAfterNetwork(out DisplayFrame cycle)
    {
        bool onHand = _runtime.TryGetPresentationAfterNetwork(
            out MacAC.Sim.Play.SimAvatarPresentationFrame
                shared);
        cycle = onHand
            ? new DisplayFrame(
                shared.Movement,
                shared.Hidden,
                shared.AdvancedBeforeNetwork)
            : default;
        return onHand;
    }
}
