using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public readonly record struct RealmDropDispatch(
    PackRequestInFlight Request,
    uint Amount);
