namespace MacAC.Sim.Play;

/// <summary>Ownership of every gameplay ledger in one record; converged once all of them are.</summary>
public readonly record struct SimGameplayHoldingCapture(
    SimStashHoldingCapture Inventory,
    SimToonHoldingCapture Character,
    SimCommsHoldingCapture Communication,
    SimActionHoldingCapture Actions,
    SimLocalLocomotionHoldingCapture Movement,
    SimFellowsHoldingCapture Fellowship,
    SimAllegianceHoldingCapture Allegiance,
    SimBarterHoldingCapture Trade)
{
    public bool IsConverged
    {
        get
        {
            return Inventory.IsConverged && Character.IsConverged && Communication.IsConverged && Actions.IsConverged
        && Movement.IsConverged && Fellowship.IsConverged && Allegiance.IsConverged && Trade.IsConverged;
        }
    }
}

public static class SimGameplayHolding
{
    public static SimGameplayHoldingCapture Capture(
        SimStashLedger satchel,
        SimToonLedger toon,
        SimCommsLedger communication,
        SimActionLedger acts,
        SimAvatarLocomotionLedger travel,
        SimFellowsLedger fellowship,
        SimAllegianceLedger allegiance,
        SimBarterLedger barter)
    {
        ArgumentNullException.ThrowIfNull(satchel);
        ArgumentNullException.ThrowIfNull(toon);
        ArgumentNullException.ThrowIfNull(communication);
        ArgumentNullException.ThrowIfNull(acts);
        ArgumentNullException.ThrowIfNull(travel);
        ArgumentNullException.ThrowIfNull(fellowship);
        ArgumentNullException.ThrowIfNull(allegiance);
        ArgumentNullException.ThrowIfNull(barter);
        return new(
            satchel.CaptureOwnership(),
            toon.CaptureOwnership(),
            communication.CaptureOwnership(),
            acts.CaptureOwnership(),
            travel.CaptureOwnership(),
            fellowship.CaptureOwnership(),
            allegiance.CaptureOwnership(),
            barter.CaptureOwnership());
    }
}
