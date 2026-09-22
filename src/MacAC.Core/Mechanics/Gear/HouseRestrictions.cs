using System.Collections.Frozen;

namespace MacAC.Mechanics.Gear;

/// <summary>Who may enter a house: everyone, the owner's allegiance, or a guest list.</summary>
public sealed record HouseAccessRecord
{
    public HouseAccessRecord(bool OpenToPublic, uint AllegianceMonarchIdent, IReadOnlyDictionary<uint, uint> Guests)
    {
        ArgumentNullException.ThrowIfNull(Guests);
        this.OpenToPublic = OpenToPublic;
        this.AllegianceMonarchTag = AllegianceMonarchIdent;
        this.Guests = Guests.ToFrozenDictionary();
    }

    public bool OpenToPublic { get; }

    public uint AllegianceMonarchTag { get; }

    public IReadOnlyDictionary<uint, uint> Guests { get; }

    public bool IsAllowedIn(uint carrierIdent, uint carrierMonarchIdent)
    {
        if (OpenToPublic)
            return true;
        if (AllegianceMonarchTag is not 0 && carrierMonarchIdent == AllegianceMonarchTag)
            return true;
        return carrierIdent is not 0 && Guests.ContainsKey(carrierIdent);
    }
}
