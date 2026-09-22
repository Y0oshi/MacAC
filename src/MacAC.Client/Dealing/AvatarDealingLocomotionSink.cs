using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Client.Dealing;

internal interface IAvatarDealingLocomotionSink
{
    bool OpenApproach(
        DealingApproach approach,
        Action<AvatarApproachToken>? armFollowingAbort = null);
}

internal sealed class AvatarDealingLocomotionSink(
    Func<AvatarLocomotionDriver?> player,
    IAvatarApproachTokenSource approachTokens)
    : IAvatarDealingLocomotionSink
{
    private readonly Func<AvatarLocomotionDriver?> _avatar = player
        ?? throw new ArgumentNullException(nameof(player));
    private readonly IAvatarApproachTokenSource _approachTickets = approachTokens
        ?? throw new ArgumentNullException(nameof(approachTokens));

    public bool OpenApproach(
        DealingApproach approach,
        Action<AvatarApproachToken>? armFollowingAbort = null)
    {
        var driver = _avatar();
        if (driver?.MoveTo is null)
            return false;

        LocomotionParams parameters = new LocomotionParams
        {
            GapToObject = approach.UseRadius,
            CanCharge = approach.CanCharge,
        };
        LocomotionPacket travel = new LocomotionPacket
        {
            ObjectId = approach.Target.ServerGuid,
            TopTierIdent = approach.Target.ServerGuid,
            Spot = new Locus(
                approach.Player.CellId,
                approach.Target.Entity.Position,
                System.Numerics.Quaternion.Identity),
            Params = parameters,
            Type = approach.IsCloseRange
                ? TravelKind.TurnToObject
                : TravelKind.MoveToObject,
            Radius = approach.TargetRadius,
            Height = approach.TargetHeight,
        };

        driver.Movement.CancelMoveTo(WeenieProblem.ActionCancelled);
        if (!_approachTickets.TryCommenceApproach(out AvatarApproachToken ticket))
            return false;
        armFollowingAbort?.Invoke(ticket);

        driver.AssignPreviousRelocateWasAutonomous(false);
        return driver.Movement.PerformMovement(travel) == WeenieProblem.None;
    }
}
