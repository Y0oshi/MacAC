using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Sound;

public sealed class AmbienceCollector(AmbienceScheduler scheduler)
{
    public const int CellsPerFlank = 8;

    // Terrain words per landblock side: a 9x9 vertex grid
    private const int VertsPerFlank = 9;

    private const uint NoOrdinal = 0xFFFFFFFFu;
    private const float LbLen = CellsPerFlank * AmbienceTuning.LandChamberLen;

    private readonly AmbienceScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

    public void Rebuild(WorldRegion zone, uint beholderLbIdent, Vector3 listenerOwnLocus, Func<uint, ushort[]?> lbs, double instant)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(lbs);

        _scheduler.CommenceReassemble();

        uint beholderX = beholderLbIdent >> 24;
        uint beholderY = (beholderLbIdent >> 16) & 0xFFu;

        for (int dx = -1; dx <= 1; ++dx)
        {
            for (int dy = -1; dy <= 1; ++dy)
            {
                long bx = beholderX + dx;
                long by = beholderY + dy;
                if (bx is < 0 or > 0xFF || by is < 0 or > 0xFF)
                    continue;

                uint lbIdent = ((uint)bx << 24) | ((uint)by << 16) | 0xFFFFu;
                ushort[]? land = lbs(lbIdent);
                if (land is null || land.Length < VertsPerFlank * VertsPerFlank)
                    continue;

                Sweep(zone, dx, dy, land, listenerOwnLocus);
            }
        }

        _scheduler.FinishReassemble(instant);
    }

    private void Sweep(WorldRegion zone, int chunkDiffX, int chunkDiffY, ushort[] land, Vector3 listener)
    {
        // The neighbour block's origin, relative to the listener's own block.
        float originX = chunkDiffX * LbLen;
        float originY = chunkDiffY * LbLen;

        for (int x = 0; x < CellsPerFlank; ++x)
        {
            for (int y = 0; y < CellsPerFlank; ++y)
            {
                ushort word = land[x * VertsPerFlank + y];
                uint landKind = (uint)((word >> 2) & 0x1F);
                uint tableauOrdinal = (uint)((word >> 11) & 0x1F);
                if (StbFor(zone, landKind, tableauOrdinal) is not { } stb)
                    continue;

                Vector3 shift = new Vector3(
                    originX + x * AmbienceTuning.LandChamberLen - listener.X,
                    originY + y * AmbienceTuning.LandChamberLen - listener.Y,
                    0f);
                if (shift.LengthSquared() > AmbienceTuning.UpperGapSq)
                    continue;

                _scheduler.ContributeChamber(shift, stb, static (chart, idx) =>
                {
                    AmbientSound sfx = chart.Sounds[idx];
                    return new AmbienceCue((SfxId)(uint)sfx.Sound, sfx.Volume, sfx.BaseChance, sfx.MinRate, sfx.MaxRate);
                });
            }
        }
    }

    // terrain type → scene type → scene → STB descriptor, each step bounds-checked
    private static AmbientSoundSet? StbFor(WorldRegion zone, uint landKind, uint tableauOrdinal)
    {
        List<TerrainKindDesc>? landKinds = zone.Terrain?.Kinds;
        if (landKinds is null || landKind >= landKinds.Count)
            return null;

        List<uint> tableauKinds = landKinds[(int)landKind].SceneChoices;
        if (tableauOrdinal >= tableauKinds.Count)
            return null;

        uint tableauKind = tableauKinds[(int)tableauOrdinal];
        List<SceneChoice>? scenes = zone.Scenes?.Choices;
        if (tableauKind == NoOrdinal || scenes is null || tableauKind >= scenes.Count)
            return null;

        uint stbOrdinal = scenes[(int)tableauKind].SoundIndex;
        var descriptors = zone.Sound?.Sets;
        if (stbOrdinal == NoOrdinal || descriptors is null || stbOrdinal >= descriptors.Count)
            return null;

        AmbientSoundSet contender = descriptors[(int)stbOrdinal];
        return contender.SoundBookId is 0 || contender.Sounds.Count is 0 ? null : contender;
    }
}
