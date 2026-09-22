namespace MacAC.Mechanics.Genesis;

public enum GenesisSkillTrack : uint
{
    Inactive = 0,
    Untrained = 1,
    Trained = 2,
    Specialized = 3,
}

public readonly record struct GenesisSkillPrice(uint SkillId, int NormalCost, int PrimaryCost);

public readonly record struct GenesisSkillFormula(
    int AdditiveBonus,
    int Attribute1Multiplier,
    int Attribute2Multiplier,
    int Divisor,
    uint Attribute1,
    uint Attribute2);

public readonly record struct GenesisSkillDetail(
    uint SkillId,
    uint MinLevel,
    string Description,
    GenesisSkillFormula Formula);

/// <summary>One training choice per skill id, in the 55-slot wire layout.</summary>
public sealed class GenesisSkillTrackSet
{
    public const int SlotCount = 55;

    private readonly GenesisSkillTrack[] _tracks = new GenesisSkillTrack[SlotCount];

    public GenesisSkillTrack this[uint skillId]
    {
        get => InSpan(skillId) ? _tracks[skillId] : GenesisSkillTrack.Inactive;
        set
        {
            if (!InSpan(skillId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(skillId),
                    skillId,
                    $"Skill id has to be in 1..{SlotCount - 1}.");
            }
            _tracks[skillId] = value;
        }
    }

    public IReadOnlyList<uint> ToWireClasses() => Array.ConvertAll(_tracks, static track => (uint)track);

    private static bool InSpan(uint aptitudeIdent) => aptitudeIdent is >= 1 and < SlotCount;
}
