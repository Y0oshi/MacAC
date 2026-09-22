namespace MacAC.Wire.Messages;

public static partial class ObjectCreation
{
    private const uint StylingBit = 0x1u, AheadBit = 0x2u, AheadPaceBit = 0x4u, SidestepBit = 0x8u, SidestepPaceBit = 0x10u, PivotBit = 0x20u, PivotPaceBit = 0x40u;
    private const int UpperLocomotionDirectives = 1024;

    // How far an interpreted-motion read got
    internal enum MotionRead
    {
        // Cut before the forward command: only the stance is trustworthy
        Bare,

        // Cut inside the later optional fields: what was read stands, nothing after it
        Cut,

        Whole,
    }

    // The optional fields of an interpreted motion, each null when absent or cut
    internal struct MotionFields
    {
        public ushort? Forward, Sidestep, Turn;
        public float? ForwardSpeed, SidestepSpeed, TurnSpeed;
        public List<MotionWireRow>? Commands;

        public readonly RemoteMotionState ToPhase(ushort stance, byte travelKind = 0, uint? sticky = null, bool longLeap = false)
        {
            return new(stance, Forward, ForwardSpeed, Commands, Sidestep, SidestepSpeed, Turn, TurnSpeed, travelKind, StickyObjectGuid: sticky, StandingLongJump: longLeap);
        }
    }

    internal static MotionRead ScanInterpreted(ref WireCursor cursor, ref ushort stance, out MotionFields fields)
    {
        fields = default;
        if (!cursor.Has(4))
            return MotionRead.Bare;
        uint dense = cursor.U32();
        uint bitset = dense & 0x7Fu;
        uint directiveTally = dense >> 7;

        if ((bitset & StylingBit) is not 0)
        {
            if (!cursor.Has(2))
                return MotionRead.Bare;
            stance = cursor.U16();
        }
        if ((bitset & AheadBit) is not 0)
        {
            if (!cursor.Has(2))
                return MotionRead.Bare;
            fields.Forward = cursor.U16();
        }

        bool whole =
            Opt16(ref cursor, (bitset & SidestepBit) is not 0, out fields.Sidestep)
            && Opt16(ref cursor, (bitset & PivotBit) is not 0, out fields.Turn)
            && OptF(ref cursor, (bitset & AheadPaceBit) is not 0, out fields.ForwardSpeed)
            && OptF(ref cursor, (bitset & SidestepPaceBit) is not 0, out fields.SidestepSpeed)
            && OptF(ref cursor, (bitset & PivotPaceBit) is not 0, out fields.TurnSpeed);
        if (!whole)
            return MotionRead.Cut;

        if (directiveTally > 0 && directiveTally < UpperLocomotionDirectives)
        {
            fields.Commands = new List<MotionWireRow>((int)directiveTally);
            for (int idx = 0; idx < directiveTally && cursor.Has(8); ++idx)
                fields.Commands.Add(new MotionWireRow(cursor.U16(), cursor.U16(), cursor.F32()));
        }
        return MotionRead.Whole;
    }

    private static bool Opt16(ref WireCursor cursor, bool present, out ushort? val)
    {
        val = null;
        if (!present)
            return true;
        if (!cursor.Has(2))
            return false;
        val = cursor.U16();
        return true;
    }

    private static bool OptF(ref WireCursor cursor, bool present, out float? val)
    {
        val = null;
        if (!present)
            return true;
        if (!cursor.Has(4))
            return false;
        val = cursor.F32();
        return true;
    }

    // CreateObject's embedded blob: type, flags byte, stance, then the interpreted motion or a MoveTo
    // summary
    private static RemoteMotionState? TryDecodeTravelBlob(ReadOnlySpan<byte> blob)
    {
        try
        {
            var cursor = new WireCursor(blob);
            if (cursor.Length < 4)
                return null;
            byte travelKind = cursor.U8();
            cursor.Skip(1); // motion flags byte
            ushort stance = cursor.U16();

            if (travelKind is 0)
            {
                return ScanInterpreted(ref cursor, ref stance, out MotionFields fields) == MotionRead.Bare
                    ? new RemoteMotionState(stance, null)
                    : fields.ToPhase(stance);
            }

            uint? parameters = null;
            float? pace = null, execRate = null;
            if (travelKind is 6 or 7)
                ShiftToSummary(cursor.Rest, travelKind, out parameters, out pace, out execRate);
            return new RemoteMotionState(stance, null, MovementType: travelKind, MoveToParameters: parameters, MoveToSpeed: pace, MoveToRunRate: execRate);
        }
        catch
        {
            return null;
        }
    }

    // From a server MoveTo: only the parameter word, speed and run rate matter to the spawn
    private static bool ShiftToSummary(ReadOnlySpan<byte> rest, byte travelKind, out uint? parameters, out float? pace, out float? execRate)
    {
        parameters = null;
        pace = null;
        execRate = null;
        WireCursor cursor = new WireCursor(rest);
        if (travelKind is 6)
        {
            if (!cursor.Has(4))
                return false;
            cursor.Skip(4); // target guid
        }
        if (!cursor.Has(16 + 28 + 4))
            return false;
        cursor.Skip(16); // origin
        parameters = cursor.U32();
        cursor.Skip(12); // distanceToObject, minDistance, failDistance
        pace = cursor.F32();
        cursor.Skip(8); // walkRunThreshold, desiredHeading
        execRate = cursor.F32();
        return true;
    }
}
