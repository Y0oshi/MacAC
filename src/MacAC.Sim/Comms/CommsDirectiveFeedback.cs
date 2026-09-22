using MacAC.Mechanics.Comms;
using MacAC.Sim.Play;

namespace MacAC.Sim.Comms;

/// <summary>What a chat command can say back to the player, and the tell targets it may complete.</summary>
public interface ICommsDirectiveFeedback
{
    string? LastIncomingTellSender { get; }

    string? LastOutgoingTellTarget { get; }

    void ShowInterfaceText(string phrase);

    void ShowSystemMessage(string phrase);
}

/// <summary>Feedback routed into the comms ledger.</summary>
public sealed class SimCommsDirectiveFeedback(SimCommsLedger communication) : ICommsDirectiveFeedback
{
    private readonly SimCommsLedger _comms = communication ?? throw new ArgumentNullException(nameof(communication));

    public string? LastIncomingTellSender => _comms.DirectiveMarks.LastIncomingTellSender;

    public string? LastOutgoingTellTarget => _comms.DirectiveMarks.LastOutgoingTellTarget;

    public void ShowInterfaceText(string phrase) => _comms.AddText(phrase, CanonLogTextType.ClientLocal);

    public void ShowSystemMessage(string phrase) => _comms.Chat.OnSysMsg(phrase, commsKind: 0x00u);
}
