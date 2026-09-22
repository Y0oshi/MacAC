using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public static class InventoryMoves
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint StackableCombineOpcode = 0x0054u;
    public const uint StackableDivideToVesselOpcode = 0x0055u;
    public const uint StackableDivideTo3DOpcode = 0x0056u;
    public const uint StackableDivideToWieldOpcode = 0x019Bu;
    public const uint HandObjectReqOpcode = 0x00CDu;
    public const uint AppendShortcutOpcode = 0x019Cu;
    public const uint DropShortcutOpcode = 0x019Du;
    public const uint TeleToPoiOpcode = 0x00B1u;
    public const uint FetchAndWieldGearOpcode = 0x001Au;
    public const uint DiscardGearOpcode = 0x001Bu;
    public const uint NoLongerViewingInsidesOpcode = 0x0195u;
    public const uint SetInscriptionOpcode = 0x00BFu;
    public const uint BuildTinkeringToolOpcode = 0x027Du;

    public static byte[] AssembleStackableCombine(uint seq, uint combineFromOid, uint combineToOid, uint quantity)
    {
        return new GameActionScribe(seq, StackableCombineOpcode, 24).U32(combineFromOid).U32(combineToOid).U32(quantity).Bytes();
    }

    public static byte[] AssembleStackableDivideToVessel(uint seq, uint pileOid, uint vesselOid, uint stance, uint quantity)
    {
        return new GameActionScribe(seq, StackableDivideToVesselOpcode, 28).U32(pileOid).U32(vesselOid).U32(stance).U32(quantity).Bytes();
    }

    /// <summary>Split N off a stack onto the ground.</summary>
    public static byte[] AssembleStackableDivideTo3D(uint seq, uint pileOid, uint quantity)
    {
        return new GameActionScribe(seq, StackableDivideTo3DOpcode, 20).U32(pileOid).U32(quantity).Bytes();
    }

    /// <summary>Split N off a stack straight into an equip slot.</summary>
    public static byte[] AssembleStackableDivideToWield(uint seq, uint pileOid, uint wieldLocale, uint quantity)
    {
        return new GameActionScribe(seq, StackableDivideToWieldOpcode, 24).U32(pileOid).U32(wieldLocale).U32(quantity).Bytes();
    }

    public static byte[] AssembleHandObjectReq(uint seq, uint markOid, uint gearOid, uint quantity)
    {
        return new GameActionScribe(seq, HandObjectReqOpcode, 24).U32(markOid).U32(gearOid).U32(quantity).Bytes();
    }

    public static byte[] AssembleAppendShortcut(uint seq, HotbarSlot listing)
    {
        return new GameActionScribe(seq, AppendShortcutOpcode, 24).I32(listing.Index).U32(listing.ObjectId).U32(listing.SpellId).Bytes();
    }

    public static byte[] AssembleDropShortcut(uint seq, uint socketOrdinal) => One(seq, DropShortcutOpcode, socketOrdinal);

    /// <summary>Quest-driven recall to a point of interest.</summary>
    public static byte[] AssembleTeleToPoi(uint seq, uint poiIdent) => One(seq, TeleToPoiOpcode, poiIdent);

    public static byte[] AssembleDiscardGear(uint seq, uint gearOid) => One(seq, DiscardGearOpcode, gearOid);

    public static byte[] AssembleFetchAndWieldGear(uint seq, uint gearOid, uint wieldBitmask)
    {
        return new GameActionScribe(seq, FetchAndWieldGearOpcode, 20).U32(gearOid).U32(wieldBitmask).Bytes();
    }

    public static byte[] AssembleNoLongerViewingInsides(uint seq, uint vesselOid) => One(seq, NoLongerViewingInsidesOpcode, vesselOid);

    public static byte[] AssembleSetInscription(uint seq, uint gearOid, string inscription)
    {
        ArgumentNullException.ThrowIfNull(inscription);
        return new GameActionScribe(seq, SetInscriptionOpcode, 32)
            .U32(gearOid)
            .String16L(inscription, "Inscription is too long for String16L.", nameof(inscription))
            .Bytes();
    }

    /// <summary>Salvage: the tool, then the non-zero guids of every item to feed it.</summary>
    public static byte[] AssembleBuildTinkeringTool(uint seq, uint toolGuid, IReadOnlyList<uint> itemGuids)
    {
        ArgumentNullException.ThrowIfNull(itemGuids);
        if (toolGuid is 0u)
            throw new ArgumentOutOfRangeException(nameof(toolGuid));
        if (itemGuids.Count is 0)
            throw new ArgumentException("At least one item is needed for salvage", nameof(itemGuids));

        GameActionScribe scribe = new GameActionScribe(seq, BuildTinkeringToolOpcode, 20 + itemGuids.Count * sizeof(uint))
            .U32(toolGuid)
            .U32(checked((uint)itemGuids.Count));
        foreach (uint oid in itemGuids)
        {
            if (oid is 0u)
                throw new ArgumentException("Salvage item ids has to be non-zero", nameof(itemGuids));
            scribe.U32(oid);
        }
        return scribe.Bytes();
    }

    private static byte[] One(uint seq, uint act, uint word) => new GameActionScribe(seq, act, 16).U32(word).Bytes();
}
