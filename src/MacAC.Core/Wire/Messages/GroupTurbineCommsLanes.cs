using System.Buffers.Binary;

namespace MacAC.Wire.Messages;

/// <summary>GameEvent 0x0295: the ten Turbine chat room ids, in wire order.</summary>
public static class GroupTurbineCommsLanes
{
    public const uint SignalKind = 0x0295u;
    public const int CargoDims = 40;

    public readonly record struct Parsed(
        uint AllegianceRoom,
        uint GeneralRoom,
        uint TradeRoom,
        uint LfgRoom,
        uint RoleplayRoom,
        uint OlthoiRoom,
        uint SocietyRoom,
        uint SocietyCelestialHandRoom,
        uint SocietyEldrytchWebRoom,
        uint SocietyRadiantBloodRoom);

    public static Parsed? TryParse(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        if (!cursor.Has(CargoDims))
            return null;
        return new Parsed(cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32());
    }

    public static byte[] Serialize(Parsed decoded)
    {
        ReadOnlySpan<uint> halls =
        [
            decoded.AllegianceRoom, decoded.GeneralRoom, decoded.TradeRoom, decoded.LfgRoom, decoded.RoleplayRoom,
            decoded.OlthoiRoom, decoded.SocietyRoom, decoded.SocietyCelestialHandRoom, decoded.SocietyEldrytchWebRoom, decoded.SocietyRadiantBloodRoom,
        ];
        byte[] octets = new byte[CargoDims];
        for (int idx = 0; idx < halls.Length; ++idx)
            BinaryPrimitives.WriteUInt32LittleEndian(octets.AsSpan(idx * 4), halls[idx]);
        return octets;
    }
}
