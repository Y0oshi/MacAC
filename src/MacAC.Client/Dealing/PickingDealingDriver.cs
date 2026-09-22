using MacAC.Client.Shell;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Dealing;

internal sealed partial class PickingDealingDriver
{
    private readonly PickPhase _pick;

    private readonly IRealmPickingProbe _ask;

    private readonly GearDealingDriver _gearList;

    private readonly SimDealingTransactionLedger _transactions;

    private readonly ISimDealingTransport _conveyance;

    private readonly IAvatarDealingLocomotionSink _movement;

    private readonly AvatarApproachCompletionLedger _approachCompletions;

    private readonly Action<string>? _toast;

    private readonly Func<uint, bool>? _dividePile;

    private readonly Func<IEnumerable<uint>> _fellowshipParticipants;

    public PickingDealingDriver(
        PickPhase selection,
        IRealmPickingProbe query,
        GearDealingDriver items,
        ISimDealingTransport transport,
        IAvatarDealingLocomotionSink movement,
        Action<string>? toast = null,
        AvatarApproachCompletionLedger? approachCompletions = null,
        Func<uint, bool>? dividePile = null,
        Func<IEnumerable<uint>>? fellowshipParticipants = null)
    {
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _ask = query ?? throw new ArgumentNullException(nameof(query));
        _gearList = items ?? throw new ArgumentNullException(nameof(items));
        _transactions = _gearList.CoreTransactions;
        _conveyance = transport ?? throw new ArgumentNullException(nameof(transport));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _toast = toast;
        _approachCompletions = approachCompletions
            ?? new AvatarApproachCompletionLedger();
        _dividePile = dividePile;
        _fellowshipParticipants = fellowshipParticipants ?? (() => Array.Empty<uint>());
    }
}
