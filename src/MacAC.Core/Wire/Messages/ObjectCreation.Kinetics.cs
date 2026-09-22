using System.Numerics;

namespace MacAC.Wire.Messages;

public static partial class ObjectCreation
{
    [Flags]
    public enum KineticDescFlag : uint
    {
        None = 0x000000,
        CSetup = 0x000001,
        MTable = 0x000002,
        Velocity = 0x000004,
        Acceleration = 0x000008,
        Omega = 0x000010,
        Parent = 0x000020,
        Children = 0x000040,
        ObjScale = 0x000080,
        Friction = 0x000100,
        Elasticity = 0x000200,
        Timestamps = 0x000400,
        STable = 0x000800,
        PeTable = 0x001000,
        DefaultScript = 0x002000,
        DefaultScriptIntensity = 0x004000,
        Position = 0x008000,
        Movement = 0x010000,
        AnimationFrame = 0x020000,
        Translucency = 0x040000,
    }

    private const int UpperDescendants = 1024;

    // Null when any flagged field is cut short
    private static KineticSpawnData? ScanKinetics(ref WireCursor cursor)
    {
        if (!cursor.Has(8))
            return null;
        KineticDescFlag flagSet = (KineticDescFlag)cursor.U32();
        uint phase = cursor.U32();
        bool Flagged(KineticDescFlag bit) => (flagSet & bit) != 0;

        KineticMovementData? travel = null;
        uint? animCycle = null;
        if (Flagged(KineticDescFlag.Movement))
        {
            if (!cursor.Has(4))
                return null;
            uint len = cursor.U32();
            if (len > 0)
            {
                if (len > int.MaxValue || !cursor.Has((int)len))
                    return null;
                byte[] raw = cursor.Bytes((int)len);
                var locomotion = TryDecodeTravelBlob(raw);
                if (!cursor.Has(4))
                    return null;
                travel = new KineticMovementData(raw, locomotion, cursor.Bool32());
            }
            else
            {
                travel = new KineticMovementData(ReadOnlyMemory<byte>.Empty, null, null);
            }
        }
        else if (Flagged(KineticDescFlag.AnimationFrame))
        {
            if (!cursor.Has(4))
                return null;
            animCycle = cursor.U32();
        }

        RemotePosition? locus = null;
        if (Flagged(KineticDescFlag.Position))
        {
            if (!cursor.Has(32))
                return null;
            locus = new RemotePosition(cursor.U32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32());
        }

        uint? locomotionChart = null, sfxChart = null, programChart = null, rig = null;
        if (Flagged(KineticDescFlag.MTable) && !Word(ref cursor, out locomotionChart)) return null;
        if (Flagged(KineticDescFlag.STable) && !Word(ref cursor, out sfxChart)) return null;
        if (Flagged(KineticDescFlag.PeTable) && !Word(ref cursor, out programChart)) return null;
        if (Flagged(KineticDescFlag.CSetup) && !Word(ref cursor, out rig)) return null;

        KineticAttachment? ancestor = null;
        if (Flagged(KineticDescFlag.Parent))
        {
            if (!cursor.Has(8))
                return null;
            ancestor = new KineticAttachment(cursor.U32(), cursor.U32());
        }

        ReadOnlyMemory<KineticAttachment>? descendants = null;
        if (Flagged(KineticDescFlag.Children))
        {
            if (!cursor.Has(4))
                return null;
            int tally = cursor.I32();
            if (tally < 0 || tally > UpperDescendants || !cursor.Has(tally * 8))
                return null;
            KineticAttachment[] ranks = new KineticAttachment[tally];
            for (int idx = 0; idx < ranks.Length; ++idx)
                ranks[idx] = new KineticAttachment(cursor.U32(), cursor.U32());
            descendants = ranks;
        }

        float? scaling = null, friction = null, elasticity = null, seeThrough = null;
        if (Flagged(KineticDescFlag.ObjScale) && !Single(ref cursor, out scaling)) return null;
        if (Flagged(KineticDescFlag.Friction) && !Single(ref cursor, out friction)) return null;
        if (Flagged(KineticDescFlag.Elasticity) && !Single(ref cursor, out elasticity)) return null;
        if (Flagged(KineticDescFlag.Translucency) && !Single(ref cursor, out seeThrough)) return null;

        Vector3? vel = null, acceleration = null, omega = null;
        if (Flagged(KineticDescFlag.Velocity) && !Triple(ref cursor, out vel)) return null;
        if (Flagged(KineticDescFlag.Acceleration) && !Triple(ref cursor, out acceleration)) return null;
        if (Flagged(KineticDescFlag.Omega) && !Triple(ref cursor, out omega)) return null;

        uint? programKind = null;
        float? programIntensity = null;
        if (Flagged(KineticDescFlag.DefaultScript) && !Word(ref cursor, out programKind)) return null;
        if (Flagged(KineticDescFlag.DefaultScriptIntensity) && !Single(ref cursor, out programIntensity)) return null;

        if (!cursor.Has(9 * 2))
            return null;
        KineticStamps stamps = new KineticStamps(cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16(), cursor.U16());
        cursor.Align4();
        if (cursor.At > cursor.Length)
            return null;

        return new KineticSpawnData(
            RawState: phase,
            Position: locus,
            Movement: travel,
            AnimationFrame: animCycle,
            SetupTableId: rig,
            MotionTableId: locomotionChart,
            SoundTableId: sfxChart,
            PhysicsScriptTableId: programChart,
            Parent: ancestor,
            Children: descendants,
            Scale: scaling,
            Friction: friction,
            Elasticity: elasticity,
            Translucency: seeThrough,
            Velocity: vel,
            Acceleration: acceleration,
            AngularVelocity: omega,
            DefaultScriptType: programKind,
            DefaultScriptIntensity: programIntensity,
            Timestamps: stamps);
    }

    private static bool Word(ref WireCursor cursor, out uint? val)
    {
        val = cursor.Has(4) ? cursor.U32() : null;
        return val.HasValue;
    }

    private static bool Single(ref WireCursor cursor, out float? val)
    {
        val = cursor.Has(4) ? cursor.F32() : null;
        return val.HasValue;
    }

    private static bool Triple(ref WireCursor cursor, out Vector3? val)
    {
        val = cursor.Has(12) ? new Vector3(cursor.F32(), cursor.F32(), cursor.F32()) : null;
        return val.HasValue;
    }
}
