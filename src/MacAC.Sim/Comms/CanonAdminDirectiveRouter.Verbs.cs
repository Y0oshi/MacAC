using MacAC.Wire.Messages;

namespace MacAC.Sim.Comms;

public sealed partial class CanonAdminDirectiveRouter
{
    private void AllegianceDetails(Args a)
    {
        string label = a.Whole.Trim();
        if (Need(label, LabelWanted))
            _feedback.RequestAllegianceInfo(label);
    }

    private void AllegianceBoot(Args a)
    {
        string label = a.Whole.Trim();
        if (!Need(label, LabelWanted))
            return;

        int bit = label.IndexOf("-account", StringComparison.OrdinalIgnoreCase);
        bool wholeAcct = bit >= 0;
        if (wholeAcct)
            label = label.Remove(bit, "-account".Length).Trim();

        _feedback.ShowSystemMessage(
            $"Attempting to boot {label}{(wholeAcct ? " (Account)" : string.Empty)}...");
        _actions.BreakAllegianceBoot(label, wholeAcct);
    }

    private void AllegianceBan(Args a)
    {
        string op = a.Head;
        if (Is(op, "list"))
        {
            _actions.ListAllegianceBans();
            return;
        }

        string label = a.Rear.Trim();
        if (!Need(label, LabelWanted))
            return;

        if (Is(op, "add"))
            _actions.AddAllegianceBan(label);
        else if (Is(op, "remove"))
            _actions.RemoveAllegianceBan(label);
        else
            Local(AllegianceHelp);
    }

    private void AllegianceChat(Args a)
    {
        string op = a.Head;
        if (Is(op, "on") || Is(op, "off"))
        {
            _feedback.SetSingleCharacterOption(
                (uint)CharacterOptionId.ListenToAllegianceChat,
                Is(op, "on"));
            return;
        }

        string rest = a.Rear.Trim();
        if (Is(op, "kick"))
        {
            int comma = rest.IndexOf(',');
            _actions.AllegianceChatBoot(
                comma < 0 ? rest : rest[..comma].Trim(),
                comma < 0 ? "No reason given." : rest[(comma + 1)..].Trim());
            return;
        }

        bool gag = Is(op, "gag");
        if (!gag && !Is(op, "ungag"))
        {
            Local(AllegianceHelp);
            return;
        }

        if (Need(rest, LabelWanted))
            _actions.AllegianceChatGag(rest, gag);
    }

    private void AllegianceBroadcast(Args a)
    {
        string msg = a.Whole.Trim();
        if (msg.Length is 0)
            Local(AllegianceHelp);
        else
            _actions.AllegianceBroadcast(msg);
    }

    private void AllegianceOfficer(Args a)
    {
        string op = a.Head;
        if (op.Length is 0 || Is(op, "list"))
        {
            _actions.ListAllegianceOfficers();
            return;
        }

        if (Is(op, "clear"))
        {
            _actions.ClearAllegianceOfficers();
            return;
        }

        Args rest = a.Rest;
        if (Is(op, "remove"))
        {
            string label = rest.Whole.Trim();
            if (Need(label, ParticipantWanted))
                _actions.RemoveAllegianceOfficer(label);
            return;
        }

        if (!Is(op, "add") && !Is(op, "set"))
        {
            Local(AllegianceHelp);
            return;
        }

        int tier = Strtol.BaseZero(rest.Head);
        if (tier is < 1 or > 3)
        {
            Local(
                "Please specify a valid officer level as a number between 1 and 3. "
                + "Check the game help files for more information on officer levels.");
            return;
        }

        string officer = rest.Rear.Trim();
        if (Need(officer, ParticipantWanted))
            _actions.SetAllegianceOfficer(officer, (uint)tier);
    }

    private void AllegianceOfficerBanner(Args a)
    {
        string op = a.Head;
        if (op.Length is 0 || Is(op, "list"))
        {
            _actions.ListAllegianceOfficerTitles();
            return;
        }

        if (Is(op, "clear"))
        {
            _actions.ClearAllegianceOfficerTitles();
            return;
        }

        if (!Is(op, "set"))
        {
            Local(AllegianceHelp);
            return;
        }

        Args rest = a.Rest;
        int tier = Strtol.BaseZero(rest.Head);
        if (tier is < 1 or > 3)
        {
            Local("Please specify a valid officer level as a number between 1 and 3.");
            return;
        }

        _actions.SetAllegianceOfficerTitle((uint)tier, rest.Rear.Trim());
    }

    private void AllegianceName(Args a)
    {
        string op = a.Head;
        if (op.Length is 0)
            _actions.QueryAllegianceName();
        else if (Is(op, "set"))
            _actions.SetAllegianceName(a.Rear.Trim());
        else if (Is(op, "clear"))
            _actions.ClearAllegianceName();
        else
            Local(AllegianceHelp);
    }

    private void AllegianceMutex(Args a)
    {
        string op = a.Head;
        uint? code = op.ToLowerInvariant() switch
        {
            "" or "check" => 4u,
            "off" => 1u,
            "on" => 2u,
            "toggle" => 3u,
            _ => null,
        };
        if (code is { } straight)
        {
            _actions.AllegianceLockAction(straight);
            return;
        }

        if (!Is(op, "bypass"))
        {
            Local(AllegianceHelp);
            return;
        }

        string vassal = a.Rear.Trim();
        if (vassal.Length is 0)
            _actions.AllegianceLockAction(5u);
        else if (Is(vassal, "clear"))
            _actions.AllegianceLockAction(6u);
        else
            _actions.SetAllegianceApprovedVassal(vassal);
    }

    private void AllegianceHouse(Args a)
    {
        string bucket = a.Head;
        if (bucket.Length is 0)
        {
            _actions.AllegianceHouseAction(1u);
            return;
        }

        uint code = (bucket.ToLowerInvariant(), a.Rest.Head.ToLowerInvariant()) switch
        {
            ("guest", "open") => 2u,
            ("guest", "close") => 3u,
            ("storage", "open") => 4u,
            ("storage", "close") => 5u,
            _ => 0u,
        };
        if (code is 0u)
            Local(AllegianceHelp);
        else
            _actions.AllegianceHouseAction(code);
    }

    private void AllegianceMotd(Args a)
    {
        string op = a.Head;
        if (op.Length is 0)
            _actions.QueryMotd();
        else if (Is(op, "set"))
            _actions.SetMotd(a.Rear.Trim());
        else if (Is(op, "clear"))
            _actions.ClearMotd();
        else
            Local(AllegianceHelp);
    }

    private void HouseGuests(Args a)
    {
        string op = a.Head;
        string label = a.Rear.Trim();
        if (Is(op, "add") || Is(op, "remove"))
        {
            if (!Need(label, GuestWanted))
                return;
            if (Is(op, "add"))
                _actions.AddPermanentGuest(label);
            else
                _actions.RemovePermanentGuest(label);
            return;
        }

        if (Is(op, "remove_all"))
            _actions.RemoveAllPermanentGuests();
        else if (Is(op, "list") || Is(op, "show"))
            _actions.RequestFullGuestList();
        else if (Is(op, "add_allegiance"))
            _actions.ModifyAllegianceGuestPermission(true);
        else if (Is(op, "remove_allegiance"))
            _actions.ModifyAllegianceGuestPermission(false);
        else
            Local(HouseHelp);
    }

    private void HouseDepot(Args a)
    {
        string op = a.Head;
        string label = a.Rear.Trim();
        if (Is(op, "add") || Is(op, "remove"))
        {
            if (!Need(label, LabelWanted))
                return;

            bool grant = Is(op, "add");
            if (!Is(label, "-all"))
                _actions.ChangeStoragePermission(label, grant);
            else if (grant)
                _actions.AddAllStoragePermission();
            else
                _actions.RemoveAllStoragePermission();
            return;
        }

        if (Is(op, "remove_all"))
            _actions.RemoveAllStoragePermission();
        else if (Is(op, "list") || Is(op, "show"))
            _actions.RequestFullGuestList();
        else if (Is(op, "add_allegiance"))
            _actions.ModifyAllegianceStoragePermission(true);
        else if (Is(op, "remove_allegiance"))
            _actions.ModifyAllegianceStoragePermission(false);
        else
            Local(HouseHelp);
    }

    private void HouseBoot(Args a)
    {
        string label = a.Whole.Trim();
        if (label.Length is 0)
            Local(HouseHelp);
        else if (Is(label, "-all"))
            _actions.BootEveryone();
        else
            _actions.BootSpecificHouseGuest(label);
    }

    private void HouseTaps(Args a)
    {
        string phase = a.Head;
        if (Is(phase, "on"))
            _actions.SetHooksVisibility(true);
        else if (Is(phase, "off"))
            _actions.SetHooksVisibility(false);
        else
            Local(HouseHelp);
    }

    private static class Strtol
    {
        public static int BaseZero(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return 0;

            int at = 0;
            int sign = 1;
            if (phrase[at] is '+' or '-')
            {
                if (phrase[at] == '-')
                    sign = -1;
                if (++at == phrase.Length)
                    return 0;
            }

            int radix = 10;
            if (phrase[at] == '0')
            {
                radix = 8;
                if (at + 2 < phrase.Length
                    && phrase[at + 1] is 'x' or 'X'
                    && Digit(phrase[at + 2]) >= 0)
                {
                    radix = 16;
                    at += 2;
                }
            }

            long ceiling = (long)int.MaxValue + (sign < 0 ? 1L : 0L);
            long magnitude = 0;
            bool any = false;
            for (; at < phrase.Length; ++at)
            {
                int d = Digit(phrase[at]);
                if (d < 0 || d >= radix)
                    break;
                any = true;
                magnitude = Math.Min(ceiling, magnitude * radix + d);
            }

            if (!any)
                return 0;
            return (int)Math.Clamp(sign < 0 ? -magnitude : magnitude, int.MinValue, int.MaxValue);
        }

        private static int Digit(char c)
        {
            return c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
        }
    }
}
