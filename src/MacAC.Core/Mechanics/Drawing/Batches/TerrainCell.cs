namespace MacAC.Mechanics.Drawing.Batches;

/// <summary>Which fields of a <see cref="TerrainCell"/> are present.</summary>
[Flags]
public enum TerrainCellBits : byte
{
    None = 0,
    Height = 1 << 0,
    Texture = 1 << 1,
    Scenery = 1 << 2,
    Road = 1 << 3,
    Encounters = 1 << 4,
}

public struct TerrainCell
{
    private readonly record struct Field(int Shift, uint Mask, TerrainCellBits Present, byte Max, string Name, string Range);

    private static readonly Field HeightField = new(24, 0xFF000000, TerrainCellBits.Height, 255, "Height", "");
    private static readonly Field TextureField = new(19, 0x00F80000, TerrainCellBits.Texture, 31, "Type", "Texture must be 0-31");
    private static readonly Field SceneryField = new(14, 0x0007C000, TerrainCellBits.Scenery, 31, "Scenery", "Scenery must be 0-31");
    private static readonly Field EncountersField = new(10, 0x00003C00, TerrainCellBits.Encounters, 15, "Encounters", "Encounters must be 0-15");
    private static readonly Field RoadField = new(7, 0x00000380, TerrainCellBits.Road, 7, "Road", "Road must be 0-7");

    private const uint FlagSetBitmask = 0x0000001F;

    private uint _bitset;

    public TerrainCell()
    {
        _bitset = 0;
    }

    public TerrainCell(byte? height, byte? texture, byte? scenery, byte? road, byte? encounters)
    {
        _bitset = 0;
        if (texture > 31)
            throw new ArgumentOutOfRangeException(nameof(texture), "Texture has to be 0-31");
        if (scenery > 31)
            throw new ArgumentOutOfRangeException(nameof(scenery), "Scenery has to be 0-31");
        if (road > 7)
            throw new ArgumentOutOfRangeException(nameof(road), "Road has to be 0-7");
        if (encounters > 15)
            throw new ArgumentOutOfRangeException(nameof(encounters), "Encounters has to be 0-15");

        Height = height;
        Type = texture;
        Scenery = scenery;
        Road = road;
        Encounters = encounters;
    }

    public TerrainCellBits Flags
    {
        readonly get => (TerrainCellBits)(_bitset & FlagSetBitmask);
        private set => _bitset = (_bitset & ~FlagSetBitmask) | (uint)value;
    }

    public byte? Height
    {
        readonly get => Read(HeightField);
        set => Write(HeightField, value);
    }

    public byte? Type
    {
        readonly get => Read(TextureField);
        set => Write(TextureField, value);
    }

    public byte? Scenery
    {
        readonly get => Read(SceneryField);
        set => Write(SceneryField, value);
    }

    public byte? Road
    {
        readonly get => Read(RoadField);
        set => Write(RoadField, value);
    }

    public byte? Encounters
    {
        readonly get => Read(EncountersField);
        set => Write(EncountersField, value);
    }

    public static TerrainCell FromHeight(byte height) => new(height, null, null, null, null);

    public static TerrainCell FromTexture(byte texture) => new(null, texture, null, null, null);

    public static TerrainCell FromScenery(byte scenery) => new(null, null, scenery, null, null);

    public static TerrainCell FromRoad(byte road) => new(null, null, null, road, null);

    public static TerrainCell FromEncounters(byte encounters) => new(null, null, null, null, encounters);

    public static TerrainCell FromTextureScenery(byte texture, byte scenery) => new(null, texture, scenery, null, null);

    public readonly uint Pack() => _bitset;

    public static TerrainCell Unpack(uint dense) => new() { _bitset = dense };

    /// <summary>Takes every present field of <paramref name="another"/>; absent ones are left alone.</summary>
    public void Merge(TerrainCell another)
    {
        if (another.Height is { } h) Height = h;
        if (another.Type is { } t) Type = t;
        if (another.Scenery is { } s) Scenery = s;
        if (another.Road is { } r) Road = r;
        if (another.Encounters is { } e) Encounters = e;
    }

    public override readonly string ToString()
    {
        return $"Height:{Height?.ToString() ?? "null"}, Texture:{Type?.ToString() ?? "null"}, "
        + $"Scenery:{Scenery?.ToString() ?? "null"}, Road:{Road?.ToString() ?? "null"}, "
        + $"Encounters:{Encounters?.ToString() ?? "null"}, Flags:{Flags}";
    }

    private readonly byte? Read(in Field field)
    {
        return (_bitset & (uint)field.Present) is not 0 ? (byte)((_bitset & field.Mask) >> field.Shift) : null;
    }

    private void Write(in Field field, byte? val)
    {
        if (val is { } present)
        {
            if (present > field.Max)
                throw new ArgumentOutOfRangeException(field.Name, field.Range);
            _bitset = (_bitset & ~field.Mask) | ((uint)present << field.Shift);
            Flags |= field.Present;
        }
        else
        {
            _bitset &= ~field.Mask;
            Flags &= ~field.Present;
        }
    }
}
