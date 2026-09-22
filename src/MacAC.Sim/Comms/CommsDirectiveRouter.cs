using MacAC.Mechanics.Comms;

namespace MacAC.Sim.Comms;

public enum SubmitUpshot { Empty, ClientHandled, UnknownCommand, Sent, Dropped }

public static class CommsDirectiveRouter
{
    private static readonly char[] Whitespace = [' ', '\t'];

    public static SubmitUpshot Submit(string? raw, ICommsDirectiveFeedback feedback, IDirectiveBus bus, CommsChannelKind defaultLane, string? defaultTellMark = null)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(bus);
        string stroke = (raw ?? string.Empty).Trim();
        if (stroke.Length is 0)
            return SubmitUpshot.Empty;

        if (stroke[0] is ':' or ';')
            stroke = "@emote " + stroke[1..];

        if (CanonClientDirectiveRegistry.TryFit(stroke, out var client))
        {
            if (client.HasValidArguments)
                bus.Publish(new ExecuteClientDirectiveCmd(client.Command, client.Arguments));
            else
                RefuseArgs(client.InvalidArgumentsText, feedback);
            return SubmitUpshot.ClientHandled;
        }

        if (TryHelp(stroke, feedback))
            return SubmitUpshot.ClientHandled;

        if (bus is IPluginDirectiveBus extensions && extensions.TryHndExtensionDirective(stroke))
            return SubmitUpshot.ClientHandled;

        if (IsSlashed(stroke) && (stroke.Length is 1 || !char.IsLetter(stroke[1])))
        {
            feedback.ShowSystemMessage($"Unknown command: {CommsInputReader.FetchVerbTicket(stroke)}. Type /help for the list of supported commands.");
            return SubmitUpshot.UnknownCommand;
        }

        if (TryTransmitRawLane(stroke, bus))
            return SubmitUpshot.Sent;

        if (TrySrvDirective(stroke, out string srvDirective))
        {
            bus.Publish(new SendServerDirectiveCmd(srvDirective));
            return SubmitUpshot.Sent;
        }

        if (CommsInputReader.IsBareRegisteredLaneVerb(stroke))
        {
            feedback.ShowInterfaceText("You must specify the text you wish to say!");
            return SubmitUpshot.ClientHandled;
        }

        if (CommsInputReader.IsReplyAbsentPreviousTeller(stroke, feedback.LastIncomingTellSender))
        {
            feedback.ShowInterfaceText("Someone must @tell you first!");
            return SubmitUpshot.ClientHandled;
        }

        if (CommsInputReader.Parse(stroke, defaultLane, feedback.LastIncomingTellSender, feedback.LastOutgoingTellTarget, defaultTellMark) is { } comms)
        {
            bus.Publish(new SendCommsCmd(comms.Channel, comms.TargetName, comms.Text));
            return SubmitUpshot.Sent;
        }

        return SubmitUpshot.Dropped;
    }

    private static bool IsSlashed(string phrase) => phrase[0] is '/' or '@';

    // A client command with bad arguments shows its own refusal, or retail's generic "not a valid
    // command"
    private static void RefuseArgs(string? bespoke, ICommsDirectiveFeedback feedback)
    {
        if (bespoke is not null)
        {
            feedback.ShowInterfaceText(bespoke);
            return;
        }
        (string? phrase, CanonLogTextType kind) = WeenieErrorText.Resolve(0x026u, null);
        string msg = phrase ?? "That is not a valid command.";
        if (kind == CanonLogTextType.ClientLocal)
            feedback.ShowInterfaceText(msg);
        else
            feedback.ShowSystemMessage(msg);
    }

    // "/<tag> text" for a channel tag that is neither a client verb nor a chat verb goes straight to
    // that channel
    private static bool TryTransmitRawLane(string stroke, IDirectiveBus bus)
    {
        if (!IsSlashed(stroke))
            return false;

        string tag = CommsInputReader.FetchVerbTicket(stroke)[1..].TrimEnd(',');
        if (CanonClientDirectiveRegistry.RecognizedVerbs.Contains(tag, StringComparer.OrdinalIgnoreCase)
            || CommsInputReader.IsRecognizedVerb("/" + tag)
            || !CanonChannelTagChart.TryResolve(tag, out uint laneIdent))

            return false;

        int gap = stroke.IndexOfAny(Whitespace);
        string phrase = gap < 0 ? string.Empty : stroke[(gap + 1)..].Trim();
        if (phrase.Length is 0)
            return false;

        bus.Publish(new TransmitCrudeLaneCmd(laneIdent, phrase));
        return true;
    }

    // Any other slashed verb the chat parser does not own is forwarded to the server as an @command
    private static bool TrySrvDirective(string stroke, out string directive)
    {
        directive = string.Empty;
        if (!IsSlashed(stroke) || CommsInputReader.IsRecognizedVerb("/" + CommsInputReader.FetchVerbTicket(stroke)[1..]))
            return false;
        directive = "@" + stroke[1..];
        return true;
    }

    private static bool TryHelp(string stroke, ICommsDirectiveFeedback feedback)
    {
        if (EqualsAny(stroke, "/help", "/?", "@help", "@?"))
        {
            RevealHelpOrdinal(feedback);
            return true;
        }
        if (StartsWithAny(stroke, "/help ", "@help ", "/? ", "@? "))
        {
            RevealVerbHelp(stroke[(stroke.IndexOf(' ') + 1)..].Trim(), feedback);
            return true;
        }
        return false;
    }

    private static void RevealHelpOrdinal(ICommsDirectiveFeedback feedback)
    {
        feedback.ShowSystemMessage(CanonDirectiveHelpChart.HelpStemNote);
        feedback.ShowSystemMessage(CanonDirectiveHelpChart.OnHandHelpListing);
    }

    private static void RevealVerbHelp(string verb, ICommsDirectiveFeedback feedback)
    {
        if (verb.Length is 0)
        {
            RevealHelpOrdinal(feedback);
            return;
        }

        string label = verb.TrimStart('/', '@');
        if (CanonDirectiveHelpChart.RegistryVerbsWithNoCanonHelp.Contains(label.TrimEnd(',')))
        {
            feedback.ShowSystemMessage(CanonDirectiveHelpChart.UnknownCommand);
            return;
        }

        if (CanonDirectiveHelpChart.TryFetchRegistryVerbSpecificsPhrase(label, out string specifics)
            || CanonClientDirectiveRegistry.TryFetchHelpPhrase(label, out specifics)
            || CanonDirectiveHelpChart.TryFetchHelpWording(label, out specifics))
        {
            feedback.ShowSystemMessage(CanonDirectiveHelpChart.HelpStemNote);
            feedback.ShowSystemMessage(CanonDirectiveHelpChart.ForMoreInformationStem + specifics);
            return;
        }

        feedback.ShowSystemMessage(CanonDirectiveHelpChart.UnknownCommand);
    }

    private static bool EqualsAny(string val, params string[] knobs) => knobs.Any(o => val.Equals(o, StringComparison.OrdinalIgnoreCase));

    private static bool StartsWithAny(string val, params string[] knobs) => knobs.Any(o => val.StartsWith(o, StringComparison.OrdinalIgnoreCase));
}
