using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class CommandAsks
{
    public const uint MarketplaceOpcode = 0x028Du;
    public const uint PkArenaOpcode = 0x0027u;
    public const uint PkLiteArenaOpcode = 0x0026u;
    public const uint JoinPkLiteOpcode = 0x028Fu;
    public const uint HouseRecallOpcode = 0x0262u;
    public const uint MansionRecallOpcode = 0x0278u;
    public const uint AskAgeOpcode = 0x01C2u;
    public const uint AskBirthOpcode = 0x01C4u;
    public const uint AckResponseOpcode = 0x0275u;
    public const uint SuicideOpcode = 0x0279u;
    public const uint SetAfkMannerOpcode = 0x000Fu;
    public const uint SetAfkMsgOpcode = 0x0010u;
    public const uint EmoteOpcode = 0x01DFu;
    public const uint SoulEmoteOpcode = 0x01E1u;
    public const uint AppendFriendOpcode = 0x0018u;
    public const uint AbandonContractOpcode = 0x0316u;
    public const uint DropFriendOpcode = 0x0017u;
    public const uint WipeFriendsOpcode = 0x0025u;
    public const uint ModifyToonSquelchOpcode = 0x0058u;
    public const uint ModifyAcctSquelchOpcode = 0x0059u;
    public const uint ModifyGlobalSquelchOpcode = 0x005Bu;
    public const uint WipeConsentOpcode = 0x0216u;
    public const uint ReadoutConsentOpcode = 0x0217u;
    public const uint DropConsentOpcode = 0x0218u;
    public const uint SetWantedModuleTierOpcode = 0x0224u;
    public const uint AppendArcanumFavoriteOpcode = 0x01E3u;
    public const uint DropArcanumFavoriteOpcode = 0x01E4u;
    public const uint GrimoireSiftOpcode = 0x0286u;
    public const uint DropArcanumOpcode = 0x01A8u;
    public const uint LegacyFriendsOpcode = 0xF7CDu;
    public const uint OrdinalLanesOpcode = 0x0149u;
    public const uint RosterLanesOpcode = 0x0148u;
    public const uint AppendLaneOpcode = 0x0145u;
    public const uint DropLaneOpcode = 0x0146u;
    public const uint RecallAllegianceHometownOpcode = 0x02ABu;
    public const uint AllegianceDetailsReqOpcode = 0x027Bu;
    public const uint RosterOnHandHousesOpcode = 0x0270u;
    public const uint AppendAvatarPermissionOpcode = 0x0219u;
    public const uint DropAvatarPermissionOpcode = 0x021Au;
    public const uint AbandonHouseOpcode = 0x021Fu;
    public const uint AskAllegianceLabelOpcode = 0x0030u;
    public const uint WipeAllegianceLabelOpcode = 0x0031u;
    public const uint SetAllegianceLabelOpcode = 0x0033u;
    public const uint SetAllegianceOfficerOpcode = 0x003Bu;
    public const uint SetAllegianceOfficerBannerOpcode = 0x003Cu;
    public const uint RosterAllegianceOfficerBannersOpcode = 0x003Du;
    public const uint WipeAllegianceOfficerBannersOpcode = 0x003Eu;
    public const uint DoAllegianceLockActOpcode = 0x003Fu;
    public const uint SetAllegianceApprovedVassalOpcode = 0x0040u;
    public const uint AllegianceCommsGagOpcode = 0x0041u;
    public const uint DoAllegianceHouseActOpcode = 0x0042u;
    public const uint AppendPermanentGuestOpcode = 0x0245u;
    public const uint DropPermanentGuestOpcode = 0x0246u;
    public const uint SetOpenHouseConditionOpcode = 0x0247u;
    public const uint EditDepotPermissionOpcode = 0x0249u;
    public const uint BootSpecificHouseGuestOpcode = 0x024Au;
    public const uint DropAllDepotPermissionOpcode = 0x024Cu;
    public const uint ReqWholeGuestRosterOpcode = 0x024Du;
    public const uint SetMotdOpcode = 0x0254u;
    public const uint AskMotdOpcode = 0x0255u;
    public const uint WipeMotdOpcode = 0x0256u;
    public const uint AppendAllDepotPermissionOpcode = 0x025Cu;
    public const uint DropAllPermanentGuestsOpcode = 0x025Eu;
    public const uint BootEveryoneOpcode = 0x025Fu;
    public const uint SetTapsVisOpcode = 0x0266u;
    public const uint ModifyAllegianceGuestPermissionOpcode = 0x0267u;
    public const uint ModifyAllegianceDepotPermissionOpcode = 0x0268u;
    public const uint BreakAllegianceBootOpcode = 0x0277u;
    public const uint AllegianceCommsBootOpcode = 0x02A0u;
    public const uint AppendAllegianceBanOpcode = 0x02A1u;
    public const uint DropAllegianceBanOpcode = 0x02A2u;
    public const uint RosterAllegianceBansOpcode = 0x02A3u;
    public const uint DropAllegianceOfficerOpcode = 0x02A5u;
    public const uint RosterAllegianceOfficersOpcode = 0x02A6u;
    public const uint WipeAllegianceOfficersOpcode = 0x02A7u;
    public const uint HouseAskOpcode = 0x021Eu;

    public static byte[] AssembleMarketplace(uint series) => Bare(series, MarketplaceOpcode);

    public static byte[] AssemblePkArena(uint series) => Bare(series, PkArenaOpcode);

    public static byte[] AssemblePkLiteArena(uint series) => Bare(series, PkLiteArenaOpcode);

    public static byte[] AssembleJoinPkLite(uint series) => Bare(series, JoinPkLiteOpcode);

    public static byte[] AssembleHouseRecall(uint series) => Bare(series, HouseRecallOpcode);

    public static byte[] AssembleMansionRecall(uint series) => Bare(series, MansionRecallOpcode);

    public static byte[] AssembleAskAge(uint series, uint objectIdent = 0u) => Word(series, AskAgeOpcode, objectIdent);

    public static byte[] AssembleAskBirth(uint series, uint objectIdent = 0u) => Word(series, AskBirthOpcode, objectIdent);

    public static byte[] AssembleAckResponse(uint series, uint ackKind, uint ctxIdent, bool approved)
    {
        return Open(series, AckResponseOpcode).U32(ackKind).U32(ctxIdent).Bool32(approved).Bytes();
    }

    public static byte[] AssembleSuicide(uint series) => Bare(series, SuicideOpcode);

    public static byte[] AssembleSetAfkManner(uint series, bool away) => Word(series, SetAfkMannerOpcode, away ? 1u : 0u);

    public static byte[] AssembleSetAfkMsg(uint series, string msg) => Text(series, SetAfkMsgOpcode, msg);

    public static byte[] AssembleEmote(uint series, string msg) => Text(series, EmoteOpcode, msg);

    public static byte[] AssembleSoulEmote(uint series, string msg) => Text(series, SoulEmoteOpcode, msg);

    public static byte[] AssembleAppendFriend(uint series, string label) => Text(series, AppendFriendOpcode, label);

    public static byte[] AssembleAbandonContract(uint series, uint contractIdent) => Word(series, AbandonContractOpcode, contractIdent);

    public static byte[] AssembleDropFriend(uint series, uint friendIdent) => Word(series, DropFriendOpcode, friendIdent);

    public static byte[] AssembleWipeFriends(uint series) => Bare(series, WipeFriendsOpcode);

    /// <summary>The pre-GameAction 0xF7CD friends command: opcode, command word, name.</summary>
    public static byte[] AssembleLegacyFriendsDirective(uint directive, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        byte[] dense = WireEncodings.Windows1252.GetBytes(name);
        if (dense.Length > ushort.MaxValue)
            throw new ArgumentException("String too long for String16L", nameof(name));
        DatagramScribe scribe = new DatagramScribe(32);
        scribe.EmitUInt32(LegacyFriendsOpcode);
        scribe.EmitUInt32(directive);
        scribe.EmitUInt16((ushort)dense.Length);
        scribe.EmitOctets(dense);
        scribe.LineTo4();
        return scribe.ToArray();
    }

    public static byte[] AssembleModifyToonSquelch(uint series, bool append, uint toonIdent, string label, uint msgKind)
    {
        return Open(series, ModifyToonSquelchOpcode).Bool32(append).U32(toonIdent).String16L(label).U32(msgKind).Bytes();
    }

    public static byte[] AssembleModifyAcctSquelch(uint series, bool append, string label)
    {
        return Open(series, ModifyAcctSquelchOpcode).Bool32(append).String16L(label).Bytes();
    }

    public static byte[] AssembleModifyGlobalSquelch(uint series, bool append, uint msgKind)
    {
        return Open(series, ModifyGlobalSquelchOpcode).Bool32(append).U32(msgKind).Bytes();
    }

    public static byte[] AssembleWipeConsent(uint series) => Bare(series, WipeConsentOpcode);

    public static byte[] AssembleReadoutConsent(uint series) => Bare(series, ReadoutConsentOpcode);

    public static byte[] AssembleDropConsent(uint series, string label) => Text(series, DropConsentOpcode, label);

    public static byte[] AssembleSetWantedModuleTier(uint series, uint moduleIdent, uint quantity)
    {
        return Open(series, SetWantedModuleTierOpcode).U32(moduleIdent).U32(quantity).Bytes();
    }

    public static byte[] AssembleAppendArcanumFavorite(uint series, uint arcanumIdent, int locus, int tabOrdinal)
    {
        return Open(series, AppendArcanumFavoriteOpcode).U32(arcanumIdent).I32(locus).I32(tabOrdinal).Bytes();
    }

    public static byte[] AssembleDropArcanumFavorite(uint series, uint arcanumIdent, int tabOrdinal)
    {
        return Open(series, DropArcanumFavoriteOpcode).U32(arcanumIdent).I32(tabOrdinal).Bytes();
    }

    public static byte[] AssembleGrimoireSift(uint series, uint filters) => Word(series, GrimoireSiftOpcode, filters);

    public static byte[] AssembleDropArcanum(uint series, uint arcanumIdent) => Word(series, DropArcanumOpcode, arcanumIdent);

    // @index - GameActionChannelIndex.Handle: no payload read
    public static byte[] AssembleOrdinalLanes(uint series) => Bare(series, OrdinalLanesOpcode);

    public static byte[] AssembleRosterLane(uint series, uint laneIdent) => Word(series, RosterLanesOpcode, laneIdent);

    public static byte[] AssembleOnLane(uint series, uint laneIdent) => Word(series, AppendLaneOpcode, laneIdent);

    public static byte[] AssembleOffLane(uint series, uint laneIdent) => Word(series, DropLaneOpcode, laneIdent);

    public static byte[] AssembleRecallAllegianceHometown(uint series) => Bare(series, RecallAllegianceHometownOpcode);

    public static byte[] AssembleAllegianceDetailsReq(uint series, string avatarLabel) => Text(series, AllegianceDetailsReqOpcode, avatarLabel);

    public static byte[] AssembleRosterOnHandHouses(uint series, uint houseKind) => Word(series, RosterOnHandHousesOpcode, houseKind);

    public static byte[] AssembleAppendAvatarPermission(uint series, string avatarLabel) => Text(series, AppendAvatarPermissionOpcode, avatarLabel);

    public static byte[] AssembleDropAvatarPermission(uint series, string avatarLabel) => Text(series, DropAvatarPermissionOpcode, avatarLabel);

    // "@house abandon" - GameActionHouseAbandon.Handle: no payload read.
    public static byte[] AssembleAbandonHouse(uint series) => Bare(series, AbandonHouseOpcode);

    public static byte[] AssembleBreakAllegianceBoot(uint series, string avatarLabel, bool acctBoot)
    {
        return PhraseWord(series, BreakAllegianceBootOpcode, avatarLabel, acctBoot ? 1u : 0u);
    }

    public static byte[] AssembleAllegianceCommsBoot(uint series, string avatarLabel, string cause) => PhrasePhrase(series, AllegianceCommsBootOpcode, avatarLabel, cause);

    public static byte[] AssembleAllegianceCommsGag(uint series, string avatarLabel, bool turnedOn)
    {
        return PhraseWord(series, AllegianceCommsGagOpcode, avatarLabel, turnedOn ? 1u : 0u);
    }

    public static byte[] AssembleAppendAllegianceBan(uint series, string avatarLabel) => Text(series, AppendAllegianceBanOpcode, avatarLabel);

    public static byte[] AssembleDropAllegianceBan(uint series, string avatarLabel) => Text(series, DropAllegianceBanOpcode, avatarLabel);

    public static byte[] AssembleRosterAllegianceBans(uint series) => Bare(series, RosterAllegianceBansOpcode);

    public static byte[] AssembleSetAllegianceOfficer(uint series, string avatarLabel, uint officerTier)
    {
        return PhraseWord(series, SetAllegianceOfficerOpcode, avatarLabel, officerTier);
    }

    public static byte[] AssembleDropAllegianceOfficer(uint series, string avatarLabel) => Text(series, DropAllegianceOfficerOpcode, avatarLabel);

    public static byte[] AssembleRosterAllegianceOfficers(uint series) => Bare(series, RosterAllegianceOfficersOpcode);

    public static byte[] AssembleWipeAllegianceOfficers(uint series) => Bare(series, WipeAllegianceOfficersOpcode);

    public static byte[] AssembleSetAllegianceOfficerBanner(uint series, uint officerTier, string banner)
    {
        return WordPhrase(series, SetAllegianceOfficerBannerOpcode, officerTier, banner);
    }

    public static byte[] AssembleRosterAllegianceOfficerBanners(uint series) => Bare(series, RosterAllegianceOfficerBannersOpcode);

    public static byte[] AssembleWipeAllegianceOfficerBanners(uint series) => Bare(series, WipeAllegianceOfficerBannersOpcode);

    public static byte[] AssembleAskAllegianceLabel(uint series) => Bare(series, AskAllegianceLabelOpcode);

    public static byte[] AssembleSetAllegianceLabel(uint series, string label) => Text(series, SetAllegianceLabelOpcode, label);

    public static byte[] AssembleWipeAllegianceLabel(uint series) => Bare(series, WipeAllegianceLabelOpcode);

    public static byte[] AssembleAllegianceLockAct(uint series, uint act) => Word(series, DoAllegianceLockActOpcode, act);

    public static byte[] AssembleSetAllegianceApprovedVassal(uint series, string avatarLabel) => Text(series, SetAllegianceApprovedVassalOpcode, avatarLabel);

    public static byte[] AssembleAllegianceHouseAct(uint series, uint act) => Word(series, DoAllegianceHouseActOpcode, act);

    public static byte[] AssembleAskMotd(uint series) => Bare(series, AskMotdOpcode);

    public static byte[] AssembleSetMotd(uint series, string motd) => Text(series, SetMotdOpcode, motd);

    public static byte[] AssembleWipeMotd(uint series) => Bare(series, WipeMotdOpcode);

    public static byte[] AssembleSetOpenHouseCondition(uint series, bool isOpen) => Word(series, SetOpenHouseConditionOpcode, isOpen ? 1u : 0u);

    public static byte[] AssembleAppendPermanentGuest(uint series, string avatarLabel) => Text(series, AppendPermanentGuestOpcode, avatarLabel);

    public static byte[] AssembleDropPermanentGuest(uint series, string avatarLabel) => Text(series, DropPermanentGuestOpcode, avatarLabel);

    public static byte[] AssembleDropAllPermanentGuests(uint series) => Bare(series, DropAllPermanentGuestsOpcode);

    public static byte[] AssembleEditDepotPermission(uint series, string avatarLabel, bool turnedOn)
    {
        return PhraseWord(series, EditDepotPermissionOpcode, avatarLabel, turnedOn ? 1u : 0u);
    }

    public static byte[] AssembleAppendAllDepotPermission(uint series) => Bare(series, AppendAllDepotPermissionOpcode);

    public static byte[] AssembleDropAllDepotPermission(uint series) => Bare(series, DropAllDepotPermissionOpcode);

    public static byte[] AssembleReqWholeGuestRoster(uint series) => Bare(series, ReqWholeGuestRosterOpcode);

    public static byte[] AssembleBootSpecificHouseGuest(uint series, string avatarLabel) => Text(series, BootSpecificHouseGuestOpcode, avatarLabel);

    public static byte[] AssembleBootEveryone(uint series) => Bare(series, BootEveryoneOpcode);

    public static byte[] AssembleSetTapsVis(uint series, bool shown) => Word(series, SetTapsVisOpcode, shown ? 1u : 0u);

    public static byte[] AssembleModifyAllegianceGuestPermission(uint series, bool turnedOn)
    {
        return Word(series, ModifyAllegianceGuestPermissionOpcode, turnedOn ? 1u : 0u);
    }

    public static byte[] AssembleModifyAllegianceDepotPermission(uint series, bool turnedOn)
    {
        return Word(series, ModifyAllegianceDepotPermissionOpcode, turnedOn ? 1u : 0u);
    }

    public static byte[] AssembleHouseAsk(uint series) => Bare(series, HouseAskOpcode);

    private static GameActionScribe Open(uint series, uint opcode) => new(series, opcode);

    private static byte[] Bare(uint series, uint opcode) => Open(series, opcode).Bytes();

    private static byte[] Word(uint series, uint opcode, uint val) => Open(series, opcode).U32(val).Bytes();

    private static byte[] Text(uint series, uint opcode, string val) => Open(series, opcode).String16L(val).Bytes();

    private static byte[] PhraseWord(uint series, uint opcode, string val, uint number) => Open(series, opcode).String16L(val).U32(number).Bytes();

    private static byte[] WordPhrase(uint series, uint opcode, uint number, string val) => Open(series, opcode).U32(number).String16L(val).Bytes();

    private static byte[] PhrasePhrase(uint series, uint opcode, string lead, string second) => Open(series, opcode).String16L(lead).String16L(second).Bytes();
}
