namespace MacAC.Extensibility.Automation;

public readonly record struct CorpseEntry(
    uint ObjectId,
    uint WeenieClassId,
    string Name,
    float Distance,
    bool HasBeenOpened,
    bool IsRequested,
    bool IsCurrent)
{
    public string LongBlurb { get; init; } = string.Empty;

    public bool IsGeneratedRare { get; init; }

    public bool IsIdentified { get; init; }
}

public readonly record struct AppraisalFrame(
    long Revision,
    uint AwaitingObjectId,
    uint CurrentObjectId);

public interface ILootControls
{
    bool IsAvailable => false;

    bool IsBusy => false;

    uint RequestedContainerId => 0u;

    uint CurrentContainerId => 0u;

    ItemUseReceipt LastItemUseCompletion => default;

    InventoryReceipt LastInventoryCompletion => default;

    AppraisalFrame Appraisal => default;

    IReadOnlyList<CorpseEntry> GrabCorpses(float ceilingGap) =>
        Array.Empty<CorpseEntry>();

    IReadOnlyList<PackEntry> GrabLatestInsides() => Array.Empty<PackEntry>();

    bool TryCaptureProperties(uint objectIdent, out ItemPropertySheet props)
    {
        props = default;
        return false;
    }

    ItemVerdict Open(uint vesselObjectIdent) => new(ItemOutcome.Unavailable);

    ItemVerdict Identify(uint objectIdent) => new(ItemOutcome.Unavailable);

    ItemVerdict Lift(uint objectIdent, bool primaryBundle = false) => new(ItemOutcome.Unavailable);
}
