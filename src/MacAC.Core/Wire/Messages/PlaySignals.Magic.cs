using System.Buffers.Binary;

namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{
    private const uint UpperLayeredArcana = 0x4000;

    /// <summary>0x02C1: a spell id learned.</summary>
    public static uint? DecodeMagicRefreshArcanum(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    /// <summary>0x01A8: a spell id forgotten.</summary>
    public static uint? DecodeMagicDropArcanum(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    /// <summary>A (spell id, layer) pair packed as two u16s.</summary>
    public readonly record struct StackedSpellId(ushort SpellId, ushort Layer)
    {
        public uint Packed => SpellId | ((uint)Layer << 16);
    }

    public readonly record struct MagicDropEnchantment(ushort SpellId, ushort Layer);

    public static MagicDropEnchantment? DecodeMagicDropEnchantment(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(4) ? new MagicDropEnchantment(cursor.U16(), cursor.U16()) : null;
    }

    /// <summary>0x02C7 dispel has the same shape as a removal.</summary>
    public static MagicDropEnchantment? DecodeMagicDispelEnchantment(ReadOnlySpan<byte> cargo) => DecodeMagicDropEnchantment(cargo);

    public static PlayerDescReader.EnchantmentRow? DecodeMagicRefreshEnchantment(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            return EnchantmentReader.Read(ref cursor);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static IReadOnlyList<PlayerDescReader.EnchantmentRow>? DecodeMagicRefreshMultipleEnchantments(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            return EnchantmentReader.ScanRoster(ref cursor);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static IReadOnlyList<StackedSpellId>? DecodeMagicLayeredArcanumRoster(ReadOnlySpan<byte> cargo)
    {
        if (cargo.Length < 4)
            return null;
        uint tally = BinaryPrimitives.ReadUInt32LittleEndian(cargo);
        if (tally > UpperLayeredArcana || cargo.Length - 4 < checked((int)tally * 4))
            return null;

        WireCursor cursor = new WireCursor(cargo);
        cursor.Skip(4);
        StackedSpellId[] idents = new StackedSpellId[tally];
        for (int idx = 0; idx < idents.Length; ++idx)
            idents[idx] = new StackedSpellId(cursor.U16(), cursor.U16());
        return idents;
    }
}
