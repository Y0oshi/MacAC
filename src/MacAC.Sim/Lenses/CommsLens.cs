namespace MacAC.Sim;

public interface ISimCommsLens
{
    long Revision { get; }

    int Count { get; }
}

public readonly record struct SimSocialCapture(
    long FriendsRevision,
    int FriendCount,
    long SquelchRevision,
    int SquelchedAccountCount,
    int SquelchedCharacterCount,
    int GlobalSquelchTypeCount,
    int NegotiatedChatRoomCount);

public readonly record struct SimBuddyCapture(uint Id, string Name, bool Online, bool AppearOffline);

public interface ISimSocialLens
{
    SimSocialCapture Snapshot { get; }

    bool TryFetchFriend(uint toonIdent, out SimBuddyCapture friend);
}
