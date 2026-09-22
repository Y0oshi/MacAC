using System.Text;
using static MacAC.Wire.Messages.ObjectCreation;

namespace MacAC.Wire.Messages;

public static class MotionUpdate
{
    public const uint Opcode = 0xF74Cu;

    private const byte StickyBit = 0x1;
    private const byte LongLeapBit = 0x2;

    public readonly record struct Parsed(uint Guid, RemoteMotionState MotionState, ushort InstanceSequence, ushort MovementSequence, ushort ServerControlSequence, bool IsAutonomous);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        try
        {
            var cursor = new WireCursor(corpus);
            if (!cursor.Opcode(Opcode) || !cursor.Has(4))
                return null;
            uint oid = cursor.U32();
            if (!cursor.Has(2))
                return null;
            ushort inst = cursor.U16();
            if (!cursor.Has(6))
                return null;
            ushort travel = cursor.U16();
            ushort srvControl = cursor.U16();
            bool autonomous = cursor.U8() is not 0;
            cursor.Skip(1); // align(4) after the u8
            if (!cursor.Has(4))
                return null;
            byte travelKind = cursor.U8();
            byte locomotionFlagSet = cursor.U8();
            ushort stance = cursor.U16();

            if (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1")
                PrintFront(corpus, travelKind, locomotionFlagSet, stance);

            Parsed Complete(RemoteMotionState phase) => new(oid, phase, inst, travel, srvControl, autonomous);

            switch (travelKind)
            {
                case 0:
                    {
                        MotionRead got = ScanInterpreted(ref cursor, ref stance, out MotionFields fields);
                        if (got == MotionRead.Bare)
                            return Complete(new RemoteMotionState(stance, null, MovementType: travelKind));
                        uint? sticky = got == MotionRead.Whole && (locomotionFlagSet & StickyBit) is not 0 && cursor.Has(4) ? cursor.U32() : null;
                        return Complete(fields.ToPhase(stance, travelKind, sticky, (locomotionFlagSet & LongLeapBit) is not 0));
                    }
                case 6 or 7:
                    {
                        (RelocateToTrailBlob? trail, float? pace, float? execRate) = ShiftTo(cursor.Rest, travelKind);
                        return Complete(new RemoteMotionState(
                            stance, null, MovementType: travelKind,
                            MoveToParameters: trail?.Bitfield, MoveToSpeed: pace, MoveToRunRate: execRate, MoveToPath: trail,
                            StandingLongJump: (locomotionFlagSet & LongLeapBit) is not 0));
                    }
                case 8 or 9:
                    return Complete(new RemoteMotionState(stance, null, MovementType: travelKind, TurnToPath: PivotTo(cursor.Rest, travelKind), StandingLongJump: (locomotionFlagSet & LongLeapBit) is not 0));
                default:
                    return Complete(new RemoteMotionState(stance, null, MovementType: travelKind, StandingLongJump: (locomotionFlagSet & LongLeapBit) is not 0));
            }
        }
        catch
        {
            return null;
        }
    }

    private static void PrintFront(ReadOnlySpan<byte> corpus, byte travelKind, byte locomotionFlagSet, ushort stance)
    {
        StringBuilder hex = new StringBuilder();
        for (int idx = 0; idx < Math.Min(corpus.Length, 32); ++idx)
            hex.Append($"{corpus[idx]:X2} ");
        Console.WriteLine($"  UM raw: mt=0x{travelKind:X2} mf=0x{locomotionFlagSet:X2} cs=0x{stance:X4}  | {hex}");
    }

    // [target guid for type 6], origin (cell + xyz), then the eight-word MoveToParameters block;
    // all-null when cut short
    private static (RelocateToTrailBlob? Path, float? Speed, float? RunRate) ShiftTo(ReadOnlySpan<byte> rest, byte travelKind)
    {
        WireCursor cursor = new WireCursor(rest);
        uint? mark = null;
        if (travelKind is 6)
        {
            if (!cursor.Has(4))
                return default;
            mark = cursor.U32();
        }
        if (!cursor.Has(16 + 28 + 4))
            return default;
        uint chamber = cursor.U32();
        float x = cursor.F32(), y = cursor.F32(), z = cursor.F32();
        uint bitfield = cursor.U32();
        float gap = cursor.F32(), lowerGap = cursor.F32(), failGap = cursor.F32();
        float pace = cursor.F32();
        float threshold = cursor.F32(), bearing = cursor.F32();
        float execRate = cursor.F32();
        return (new RelocateToTrailBlob(mark, chamber, x, y, z, gap, lowerGap, failGap, threshold, bearing, bitfield), pace, execRate);
    }

    // [target guid + wire heading for type 8], then bitfield, speed, desired heading
    private static PivotToTrailBlob? PivotTo(ReadOnlySpan<byte> rest, byte travelKind)
    {
        WireCursor cursor = new WireCursor(rest);
        uint? mark = null;
        float? bearing = null;
        if (travelKind is 8)
        {
            if (!cursor.Has(8))
                return null;
            mark = cursor.U32();
            bearing = cursor.F32();
        }
        if (!cursor.Has(12))
            return null;
        return new PivotToTrailBlob(mark, bearing, cursor.U32(), cursor.F32(), cursor.F32());
    }
}
