namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{
    public readonly record struct AppraisalHeader(uint Guid, uint AppraiseFlags, bool Success);

    public static AppraisalHeader? DecodeRecognizeResponsePreamble(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(12) ? new AppraisalHeader(cursor.U32(), cursor.U32(), cursor.U32() is not 0) : null;
    }

    /// <summary>0x0023: the server equipped an item.</summary>
    public readonly record struct WieldThing(uint ItemGuid, uint EquipLoc);

    public static WieldThing? DecodeWieldObject(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new WieldThing(cursor.U32(), cursor.U32()) : null;
    }

    public readonly record struct SatchelPutObjRefInVessel(uint ItemGuid, uint ContainerGuid, uint Placement, uint ContainerType);

    public static SatchelPutObjRefInVessel? DecodePutObjRefInVessel(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(16) ? new SatchelPutObjRefInVessel(cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32()) : null;
    }

    public readonly record struct ContainerViewRow(uint Guid, uint ContainerType);

    public readonly record struct LensInsides(uint ContainerGuid, IReadOnlyList<ContainerViewRow> Items);

    public static LensInsides? DecodeLensInsides(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        if (!cursor.Has(8))
            return null;
        uint vessel = cursor.U32();
        uint tally = cursor.U32();
        if ((long)cursor.Left < (long)tally * 8)
            return null;
        ContainerViewRow[] ranks = new ContainerViewRow[tally];
        for (int idx = 0; idx < ranks.Length; ++idx)
            ranks[idx] = new ContainerViewRow(cursor.U32(), cursor.U32());
        return new LensInsides(vessel, ranks);
    }

    public static uint? DecodeUseDone(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    /// <summary>0x019A: the server dropped an item to the ground.</summary>
    public static uint? DecodePutObjectIn3D(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public readonly record struct SatchelSrvPersistBotched(uint ItemGuid, uint WeenieError);

    public static SatchelSrvPersistBotched? DecodeSatchelSrvPersistFailed(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new SatchelSrvPersistBotched(cursor.U32(), cursor.U32()) : null;
    }

    public static uint? DecodeShutTerrainVessel(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public readonly record struct AskGearManaReply(uint ItemGuid, float ManaPercent, bool Valid);

    public static AskGearManaReply? DecodeAskGearManaResponse(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(12) ? new AskGearManaReply(cursor.U32(), cursor.F32(), cursor.U32() is not 0) : null;
    }

    public readonly record struct SalvageLine(uint MaterialType, double Workmanship, uint Units);

    public readonly record struct SalvageOperationsOutcome(uint SkillId, IReadOnlyList<uint> UnsuitableItemGuids, IReadOnlyList<SalvageLine> Results, int AugmentationBonusPercent);

    public static SalvageOperationsOutcome? DecodeSalvageOpsOutcome(ReadOnlySpan<byte> cargo)
    {
        const int StrokeOctets = 16;
        WireCursor cursor = new WireCursor(cargo);
        if (!cursor.Has(16))
            return null;
        uint aptitude = cursor.U32();
        uint unsuitableTally = cursor.U32();
        if (unsuitableTally > (uint)(cursor.Left / sizeof(uint)))
            return null;
        uint[] unsuitable = new uint[(int)unsuitableTally];
        for (int idx = 0; idx < unsuitable.Length; ++idx)
            unsuitable[idx] = cursor.U32();

        if (!cursor.Has(sizeof(uint) + sizeof(int)))
            return null;
        uint strokeTally = cursor.U32();
        if (strokeTally > (uint)((cursor.Left - sizeof(int)) / StrokeOctets))
            return null;
        SalvageLine[] strokes = new SalvageLine[(int)strokeTally];
        for (int idx = 0; idx < strokes.Length; ++idx)
            strokes[idx] = new SalvageLine(cursor.U32(), cursor.F64(), cursor.U32());

        if (!cursor.Has(sizeof(int)))
            return null;
        return new SalvageOperationsOutcome(aptitude, unsuitable, strokes, cursor.I32());
    }

    public readonly record struct EnrollBarter(uint Initiator, uint Partner, ulong Stamp);

    public static EnrollBarter? DecodeEnrollBarter(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(16) ? new EnrollBarter(cursor.U32(), cursor.U32(), cursor.U64()) : null;
    }

    public static uint? DecodeShutBarter(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public readonly record struct AppendToBarter(uint ItemGuid, uint Side, uint SlotIndex);

    public static AppendToBarter? DecodeAppendToBarter(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(12) ? new AppendToBarter(cursor.U32(), cursor.U32(), cursor.U32()) : null;
    }

    public readonly record struct DropFromBarter(uint ItemGuid, uint Mode);

    public static DropFromBarter? DecodeDropFromBarter(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new DropFromBarter(cursor.U32(), cursor.U32()) : null;
    }

    public static uint? DecodeAdmitBarter(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    /// <summary>0x0203: who declined.</summary>
    public static uint? DecodeDeclineBarter(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public static uint? DecodeRestartBarter(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public readonly record struct BarterMiss(uint ItemGuid, uint Reason);

    public static BarterMiss? DecodeBarterMiss(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new BarterMiss(cursor.U32(), cursor.U32()) : null;
    }
}
