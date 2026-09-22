using MacAC.Mechanics.Fellows;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Shell;

public sealed partial class ClientDirectiveDriver
{
    private static int CanonAtoi(string val)
    {
        if (string.IsNullOrEmpty(val))
            return 0;

        int ordinal = 0;
        int sign = 1;
        if (val[0] is '+' or '-')
        {
            if (val[0] == '-')
                sign = -1;
            ++ordinal;
        }

        long outcome = 0;
        bool sawDigit = false;
        while (ordinal < val.Length && val[ordinal] is >= '0' and <= '9')
        {
            sawDigit = true;
            outcome = Math.Min(
                (long)int.MaxValue + (sign < 0 ? 1L : 0L),
                outcome * 10L + (val[ordinal] - '0'));
            ++ordinal;
        }

        if (!sawDigit)
            return 0;
        long signed = sign < 0 ? -outcome : outcome;
        return (int)Math.Clamp(signed, int.MinValue, int.MaxValue);
    }

    private bool DemandNoArgs(string arguments, string usage)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return true;
        _bindings.ShowSystemMessage($"Usage: {usage}");
        return false;
    }

    private void ShowFriends(bool onlineSole)
    {
        var listings = _bindings.Friends.Snapshot();
        if (listings.Count is 0)
        {
            _bindings.ShowSystemMessage("Your friends list is empty!\n");
            return;
        }

        string[] strokes = listings
            .Where(listing => !onlineSole || listing.Online)
            .Select(listing => $"  {listing.Name}{(listing.Online ? " (Online)" : string.Empty)}")
            .ToArray();
        _bindings.ShowSystemMessage(strokes.Length is 0
            ? "Your friends:\n  You have no friends that are online.\n"
            : "Your friends:\n" + string.Join("\n", strokes) + "\n");
    }

    private void ShowToonSquelches()
    {
        SquelchBook database = _bindings.Squelch.Snapshot();
        string[] strokes = database.Characters.Values
            .Where(details => details.MessageTypes.Count > 0)
            .Select(ComposeSquelchDetails)
            .ToArray();
        _bindings.ShowSystemMessage(
            "(account) denotes a character whose account has also been squelched.\n"
            + "Format: Name : List of squelched message types.\n--------\n"
            + (strokes.Length is 0 ? "none\n" : string.Join("\n", strokes) + "\n"));
    }

    private void ShowGlobalFilters()
    {
        var global = _bindings.Squelch.Snapshot().Global;
        string roster = global.MessageTypes.Count is 0
            ? "none"
            : ComposeMsgKinds(global.MessageTypes);
        _bindings.ShowSystemMessage(
            "The following types of messages are currently being filtered globally:\n"
            + roster + "\n(For a list of filter options, type @help filter)\n");
    }

    private static string LeadArg(string arguments)
    {
        string trimmed = arguments.Trim();
        int separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0 ? trimmed : trimmed[..separator];
    }

    private static string RemainderFollowingLeadArg(string arguments)
    {
        string trimmed = arguments.Trim();
        int separator = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0 ? string.Empty : trimmed[(separator + 1)..].TrimStart();
    }

    private static string[] DivideArgs(string arguments) =>
        arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private void AppendFriend(string arguments)
    {
        string label = arguments.Trim();
        if (label.Length is 0)
        {
            _bindings.ShowSystemMessage(
                "You must specify the name of the friend you wish to add.");
            return;
        }

        if (_bindings.Friends.Snapshot().Count >= 50)
        {
            _bindings.ShowWeenieError(0x0561u);
            return;
        }

        _bindings.AddFriend(label);
    }

    private void DropFriend(string arguments)
    {
        string label = arguments.Trim();
        if (label.Length is 0)
        {
            _bindings.ShowSystemMessage(
                "You must specify the name of the friend you wish to remove.");
            return;
        }

        if (label.Equals("-all", StringComparison.OrdinalIgnoreCase))
        {
            _bindings.ClearFriends();
            _bindings.Friends.Clear();
            _bindings.ShowSystemMessage("Your friends list has been cleared.\n");
            return;
        }

        FriendRow? friend = _bindings.Friends.Snapshot().FirstOrDefault(
            listing => listing.Name.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (friend is null)
            _bindings.ShowWeenieError(0x0563u);
        else
            _bindings.RemoveFriend(friend.Id);
    }

    private bool TryDecodeSquelch(
        string arguments,
        out MuteArguments decoded,
        out string problem)
    {
        decoded = default;
        problem = string.Empty;
        string[] pieces = DivideArgs(arguments);
        bool acct = false;
        uint msgKind = 1u;
        string? replyLabel = null;
        int ordinal = 0;
        for (; ordinal < pieces.Length && pieces[ordinal].StartsWith('-'); ++ordinal)
        {
            string knob = pieces[ordinal][1..];
            if (knob.Equals("account", StringComparison.OrdinalIgnoreCase))
                acct = true;
            else if (knob.Equals("reply", StringComparison.OrdinalIgnoreCase))
            {
                replyLabel = _bindings.LastTeller();
                if (string.IsNullOrWhiteSpace(replyLabel))
                {
                    problem = "A player must @tell you before you can use the -reply option.";
                    return false;
                }
            }
            else if (!TryFetchMsgKind(knob, out msgKind))
            {
                problem = $"\"{knob}\" is not a valid squelch category.";
                return false;
            }
        }

        string label = replyLabel ?? string.Join(' ', pieces.Skip(ordinal));
        if (label.Length is 0)
        {
            problem = "You have not specified a squelch target.";
            return false;
        }

        decoded = new MuteArguments(acct, msgKind, label);
        return true;
    }

    private static bool TryFetchModuleBucket(string val, out uint bucket)
    {
        bucket = val.ToLowerInvariant() switch
        {
            "scarab" or "scarabs" => 0u,
            "herb" or "herbs" => 1u,
            "powderedgem" or "powderedgems" or "powder" or "powders" => 2u,
            "alchemicalsubstance" or "alchemicalsubstances" or "potion" or "potions" => 3u,
            "talisman" or "talismans" => 4u,
            "taper" or "tapers" => 5u,
            "pea" or "peas" => 6u,
            _ => uint.MaxValue,
        };
        return bucket != uint.MaxValue;
    }

    private static bool TryFetchMsgKind(string val, out uint kind)
    {
        foreach ((uint tag, string label) in MsgKinds)
        {
            if (label.Equals(val, StringComparison.OrdinalIgnoreCase))
            {
                kind = tag;
                return true;
            }
        }
        kind = 0u;
        return false;
    }

    private static string ComposeSquelchDetails(SquelchFacts details)
    {
        return $"Name: {details.Name}{(details.AccountWide ? " (account) " : " ")}"
        + ComposeMsgKinds(details.MessageTypes);
    }

    private static string ComposeMsgKinds(IReadOnlySet<uint> kinds)
    {
        return kinds.Contains(1u)
            ? "All message types"
            : string.Join(", ", MsgKinds
            .Where(duo => duo.Key is not 1u && kinds.Contains(duo.Key))
            .Select(duo => duo.Value));
    }

    private bool? HasAvatarBit(ActorImpactFlagSet bit)
    {
        uint? bitfield = _bindings.PlayerPublicWeenieBitfield();
        return bitfield is null
            ? null
            : (EntityContactFlagsExt.FromPwdBitfield(bitfield.Value) & bit) != 0;
    }
}
