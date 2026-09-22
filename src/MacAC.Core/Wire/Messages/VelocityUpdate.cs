using System.Numerics;

namespace MacAC.Wire.Messages;

/// <summary>0xF74E: an object's linear and angular velocity.</summary>
public static class VelocityUpdate
{
    public const uint Opcode = 0xF74Eu;

    public readonly record struct Parsed(uint Guid, Vector3 Velocity, Vector3 Omega, ushort InstanceSequence, ushort VectorSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(36) || !cursor.Opcode(Opcode))
            return null;
        uint oid = cursor.U32();
        Vector3 vel = new Vector3(cursor.F32(), cursor.F32(), cursor.F32());
        Vector3 omega = new Vector3(cursor.F32(), cursor.F32(), cursor.F32());
        return new Parsed(oid, vel, omega, cursor.U16(), cursor.U16());
    }
}
