namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{
    /// <summary>0x01C0 UpdateHealth: guid and health fraction 0..1.</summary>
    public readonly record struct RefreshHealth(uint TargetGuid, float HealthPercent);

    public static RefreshHealth? DecodeRefreshHealth(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new RefreshHealth(cursor.U32(), cursor.F32()) : null;
    }

    public readonly record struct VictimNotice(string DeathMessage);

    public static VictimNotice? DecodeVictimNotification(ReadOnlySpan<byte> cargo)
    {
        return Guarded<VictimNotice>(cargo, static (ref WireCursor cursor) => new VictimNotice(cursor.String16L()));
    }

    public readonly record struct KillerNotice(string DeathMessage);

    public static KillerNotice? DecodeKillerNotification(ReadOnlySpan<byte> cargo)
    {
        return Guarded<KillerNotice>(cargo, static (ref WireCursor cursor) => new KillerNotice(cursor.String16L()));
    }

    /// <summary>0x01B1 "you hit X".</summary>
    public readonly record struct AttackerNotice(string DefenderName, uint DamageType, double HealthPercent, uint Damage, uint Critical, ulong AttackConditions);

    public static AttackerNotice? DecodeAttackerNotification(ReadOnlySpan<byte> cargo)
    {
        return Guarded<AttackerNotice>(cargo, static (ref WireCursor cursor) =>
    {
        string label = cursor.String16L();
        if (!cursor.Has(28))
            return null;
        return new AttackerNotice(label, cursor.U32(), cursor.F64(), cursor.U32(), cursor.U32(), cursor.U64());
    });
    }

    /// <summary>0x01B2 "X hit you", with the quadrant struck.</summary>
    public readonly record struct DefenderNotice(string AttackerName, uint DamageType, double HealthPercent, uint Damage, uint HitQuadrant, uint Critical, ulong AttackConditions);

    public static DefenderNotice? DecodeDefenderNotification(ReadOnlySpan<byte> cargo)
    {
        return Guarded<DefenderNotice>(cargo, static (ref WireCursor cursor) =>
    {
        string label = cursor.String16L();
        if (!cursor.Has(32))
            return null;
        return new DefenderNotice(label, cursor.U32(), cursor.F64(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U64());
    });
    }

    /// <summary>0x01B3: the named defender evaded you.</summary>
    public static string? DecodeEvasionAttackerNotification(ReadOnlySpan<byte> cargo)
    {
        return GuardedRef<string>(cargo, static (ref WireCursor cursor) => cursor.String16L());
    }

    /// <summary>0x01B4: you evaded the named attacker.</summary>
    public static string? DecodeEvasionDefenderNotification(ReadOnlySpan<byte> cargo)
    {
        return GuardedRef<string>(cargo, static (ref WireCursor cursor) => cursor.String16L());
    }

    public static bool DecodeFightingCommenceAssault(ReadOnlySpan<byte> cargo) => cargo.Length is 0;

    /// <summary>0x01A7 AttackDone: a single WeenieError word; the sequence is not on the wire.</summary>
    public readonly record struct AssaultFinished(uint AttackSequence, uint WeenieError);

    public static AssaultFinished? DecodeAssaultDone(ReadOnlySpan<byte> cargo) => LeadWord(cargo) is { } problem ? new AssaultFinished(0u, problem) : null;
}
