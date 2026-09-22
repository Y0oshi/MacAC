namespace MacAC.Assets.Pak;

/// <summary>What the table of contents says about one key.</summary>
public enum PakListingPhase
{
    Missing,
    Available,
    Corrupt,
}

public enum PakReadOutcome
{
    Missing,
    Loaded,
    Corrupt,
}
