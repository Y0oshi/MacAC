namespace MacAC.Extensibility.Automation;

public readonly record struct ChatLine(
    ulong Sequence,
    uint SenderObjectId,
    int Kind,
    string Sender,
    string Text,
    string ChannelName);

public interface IChatControls
{
    IReadOnlyList<ChatLine> GrabMsgs(ulong followingSeries) => Array.Empty<ChatLine>();

    void PostSysMsg(string phrase);

    bool Submit(string phrase) => false;
}
