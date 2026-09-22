using MacAC.Mechanics.Genesis;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public enum GenesisTraitId
{
    Strength = 1,
    Endurance = 2,
    Quickness = 3,
    Coordination = 4,
    Focus = 5,
    Self = 6,
}

public enum GenesisAppearanceSlot
{
    EyesStrip,
    NoseStrip,
    MouthStrip,
    HairStyle,
    HairColor,
    EyeColor,
    HeadgearStyle,
    HeadgearColor,
    ShirtStyle,
    ShirtColor,
    TrousersStyle,
    TrousersColor,
    FootwearStyle,
    FootwearColor,
}

public enum GenesisShadeSlot
{
    Skin,
    Hair,
    Headgear,
    Shirt,
    Trousers,
    Footwear,
}

public enum SimToonGenesisDiffKind
{
    Reset,
    StateChanged,
    FinishRefused,
    FinishSent,
    Created,
    CreationFailed,
    RejectionAcknowledged,
}

public readonly record struct SimToonGenesisAppearance(
    uint EyesStrip,
    uint NoseStrip,
    uint MouthStrip,
    uint HairStyle,
    uint HairColor,
    uint EyeColor,
    uint HeadgearStyle,
    uint HeadgearColor,
    uint ShirtStyle,
    uint ShirtColor,
    uint TrousersStyle,
    uint TrousersColor,
    uint FootwearStyle,
    uint FootwearColor,
    double SkinShade,
    double HairShade,
    double HeadgearShade,
    double ShirtShade,
    double TrousersShade,
    double FootwearShade)
{
    public const uint Unset = 0xFFFFFFFFu;

    public const double UnsetShade = -1.0;

    public static SimToonGenesisAppearance Default { get; } = new(
        Unset, Unset, Unset,
        Unset, Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        Unset, Unset,
        UnsetShade, UnsetShade, UnsetShade,
        UnsetShade, UnsetShade, UnsetShade);
}

public readonly record struct SimToonGenesisLocalRefusal(
    bool NoName,
    bool AttributeCreditsUnspent,
    bool AlreadyPending,
    bool RosterFull,
    bool HeritageOrGenderUnset = false)
{
    public bool Any
    {
        get
        {
            return NoName || AttributeCreditsUnspent || AlreadyPending || RosterFull
        || HeritageOrGenderUnset;
        }
    }

    public static SimToonGenesisLocalRefusal None { get; } = default;
}

public readonly record struct SimToonGenesisIdentity(
    uint Guid,
    string Name);

public readonly record struct SimToonGenesisRejection(
    uint RawCode,
    GenesisVerdict.Opcode Code,
    string Reason,
    string AttemptedName);

public readonly record struct SimToonGenesisCapture(
    SimEpochTicket Generation,
    bool IsActive,
    long Revision,
    uint HeritageId,
    uint GenderKey,
    SimToonGenesisAppearance Appearance,
    uint Template,
    GenesisAttributeSpread Attributes,
    uint AttributeLockMask,
    uint TotalAttributeCredits,
    int RemainingAttributeCredits,
    uint TotalSkillCredits,
    int RemainingSkillCredits,
    string Name,
    int StartArea,
    uint Slot,
    bool VerificationPending,
    SimToonGenesisLocalRefusal LastLocalRefusal,
    SimToonGenesisRejection? LastRejection,
    SimToonGenesisIdentity? LastCreated)
{
    public const uint BlueprintUnset = 0xFFFFFFFFu;

    public bool IsAttrBolted(GenesisTraitId attrIdent) =>
        (AttributeLockMask & (1u << ((int)attrIdent - 1))) is not 0u;
}

public readonly record struct SimToonGenesisDiff(
    SimEpochTicket Generation,
    ulong Sequence,
    long Revision,
    SimToonGenesisDiffKind Kind);

public interface ISimToonGenesisWatcher
{
    void OnToonCreationAltered(in SimToonGenesisDiff diff);
}

public interface ISimToonGenesisEventFeed
{
    IDisposable Subscribe(ISimToonGenesisWatcher watcher);
}

public interface ISimToonGenesisLens : ISimToonGenesisEventFeed
{
    SimToonGenesisCapture Snapshot { get; }

    GenesisSkillTrack GetSkillLevel(uint aptitudeIdent);

    GenesisOptions Options { get; }
}
