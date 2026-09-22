using System.Numerics;

namespace MacAC.Wire.Messages;

public static class RefreshLocus
{
    public const uint Opcode = 0xF748u;

    [Flags]
    public enum LocusFlagSet : uint
    {
        None = 0x00,
        HasVelocity = 0x01,
        HasPlacementID = 0x02,
        IsGrounded = 0x04,
        OrientationHasNoW = 0x08,
        OrientationHasNoX = 0x10,
        OrientationHasNoY = 0x20,
        OrientationHasNoZ = 0x40,
    }

    public readonly record struct Parsed(
        uint Guid,
        ObjectCreation.RemotePosition Position,
        Vector3? Velocity,
        uint? PlacementId,
        bool IsGrounded,
        ushort InstanceSequence = 0,
        ushort PositionSequence = 0,
        ushort TeleportSequence = 0,
        ushort ForcePositionSequence = 0);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        try
        {
            var cursor = new WireCursor(corpus);
            if (!cursor.Opcode(Opcode) || !cursor.Has(8))
                return null;
            uint oid = cursor.U32();
            LocusFlagSet flagSet = (LocusFlagSet)cursor.U32();
            if (!cursor.Has(16))
                return null;
            uint chamber = cursor.U32();
            float x = cursor.F32(), y = cursor.F32(), z = cursor.F32();

            float w = 0f, qx = 0f, qy = 0f, qz = 0f;
            if (!Component(ref cursor, flagSet, LocusFlagSet.OrientationHasNoW, ref w)
                || !Component(ref cursor, flagSet, LocusFlagSet.OrientationHasNoX, ref qx)
                || !Component(ref cursor, flagSet, LocusFlagSet.OrientationHasNoY, ref qy)
                || !Component(ref cursor, flagSet, LocusFlagSet.OrientationHasNoZ, ref qz))

                return null;

            Vector3? vel = null;
            if ((flagSet & LocusFlagSet.HasVelocity) != 0)
            {
                if (!cursor.Has(12))
                    return null;
                vel = new Vector3(cursor.F32(), cursor.F32(), cursor.F32());
            }

            uint? stance = null;
            if ((flagSet & LocusFlagSet.HasPlacementID) != 0)
            {
                if (!cursor.Has(4))
                    return null;
                stance = cursor.U32();
            }

            if (!cursor.Has(8))
                return null;
            var locus = new ObjectCreation.RemotePosition(chamber, x, y, z, w, qx, qy, qz);
            return new Parsed(oid, locus, vel, stance, (flagSet & LocusFlagSet.IsGrounded) != 0, cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16());
        }
        catch
        {
            return null;
        }
    }

    // Reads a quaternion component unless its "absent" flag is set; false when the body ran out
    private static bool Component(ref WireCursor cursor, LocusFlagSet flagSet, LocusFlagSet absent, ref float val)
    {
        if ((flagSet & absent) != 0)
            return true;
        if (!cursor.Has(4))
            return false;
        val = cursor.F32();
        return true;
    }
}
