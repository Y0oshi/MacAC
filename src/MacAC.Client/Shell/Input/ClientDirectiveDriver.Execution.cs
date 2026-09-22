using System.Globalization;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell;

public sealed partial class ClientDirectiveDriver
{
    /// <summary>
    /// The allegiance and housing commands, which are parsed further by the administration handler
    /// rather than acted on here.
    /// </summary>
    private static readonly ClientDirectiveId[] AdministrationDirectives =
    [
        ClientDirectiveId.AllegianceInfo,
        ClientDirectiveId.AllegianceBoot,
        ClientDirectiveId.AllegianceBan,
        ClientDirectiveId.AllegianceChat,
        ClientDirectiveId.AllegianceBroadcast,
        ClientDirectiveId.AllegianceOfficer,
        ClientDirectiveId.AllegianceOfficerTitle,
        ClientDirectiveId.AllegianceName,
        ClientDirectiveId.AllegianceLock,
        ClientDirectiveId.AllegianceHouse,
        ClientDirectiveId.AllegianceMotd,
        ClientDirectiveId.AllegianceUnrecognizedSubcommand,
        ClientDirectiveId.HouseOpenStatus,
        ClientDirectiveId.HouseStorage,
        ClientDirectiveId.HouseBoot,
        ClientDirectiveId.HouseBootAll,
        ClientDirectiveId.HouseGuests,
        ClientDirectiveId.HouseHooks,
        ClientDirectiveId.HouseUnrecognizedSubcommand,
    ];

    /// <summary>
    /// Every retail client command, and what it does.
    ///
    /// A table rather than a switch, so a command greps to one line and adding one is a line.
    /// Everything that only forwards to the administration handler shares a single entry.
    /// </summary>
    private static readonly Dictionary<ClientDirectiveId, Action<ClientDirectiveDriver, ExecuteClientDirectiveCmd>>
        Directives = BuildDirectives();

    public void Execute(ExecuteClientDirectiveCmd command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Directives.TryGetValue(command.Command, out var perform))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command), command.Command, "Unknown retail client command.");
        }

        perform(this, command);
    }

    private static Dictionary<ClientDirectiveId, Action<ClientDirectiveDriver, ExecuteClientDirectiveCmd>>
        BuildDirectives()
    {
        var table = new Dictionary<ClientDirectiveId, Action<ClientDirectiveDriver, ExecuteClientDirectiveCmd>>
        {
            // Travel. The arena recalls are refused unless the character carries the matching
            // player-killer bit, and entering PK-lite is refused if it already carries either.
            [ClientDirectiveId.LifestoneRecall] = static (me, _) => me._bindings.TeleportToLifestone(),
            [ClientDirectiveId.MarketplaceRecall] = static (me, _) => me._bindings.TeleportToMarketplace(),
            [ClientDirectiveId.HouseRecall] = static (me, _) => me._bindings.TeleportToHouse(),
            [ClientDirectiveId.MansionRecall] = static (me, _) => me._bindings.TeleportToMansion(),
            [ClientDirectiveId.AllegianceHometown] = static (me, _) => me._bindings.RecallAllegianceHometown(),
            [ClientDirectiveId.PkArenaRecall] = static (me, _) =>
            {
                if (me.HasAvatarBit(ActorImpactFlagSet.IsPK) == false)
                    me._bindings.ShowWeenieError(0x055Fu);
                else
                    me._bindings.TeleportToPkArena();
            },
            [ClientDirectiveId.PkLiteArenaRecall] = static (me, _) =>
            {
                if (me.HasAvatarBit(ActorImpactFlagSet.IsPKLite) == false)
                    me._bindings.ShowWeenieError(0x0560u);
                else
                    me._bindings.TeleportToPkLiteArena();
            },
            [ClientDirectiveId.EnterPkLite] = static (me, _) =>
            {
                if (me.HasAvatarBit(ActorImpactFlagSet.IsPK) == true
                    || me.HasAvatarBit(ActorImpactFlagSet.IsPKLite) == true)
                    me._bindings.ShowWeenieError(0x0507u);
                else
                    me._bindings.EnterPkLite();
            },

            // Character queries and client state.
            [ClientDirectiveId.QueryAge] = static (me, _) => me._bindings.QueryAge(),
            [ClientDirectiveId.QueryBirth] = static (me, _) => me._bindings.QueryBirth(),
            [ClientDirectiveId.ToggleFrameRate] = static (me, _) => me._bindings.ToggleFrameRate(),
            [ClientDirectiveId.ToggleUiLock] = static (me, _) => me._bindings.ToggleUiLock(),
            [ClientDirectiveId.Endurance] = static (me, _) => me._bindings.ShowSystemMessage(CanonEndurancePhrase),
            [ClientDirectiveId.ListEmotes] = static (me, _) => me._bindings.ShowSystemMessage(StandardEmotes),
            [ClientDirectiveId.Speaker] = static (me, _) => me._bindings.ShowSystemMessage(
                "This command is no longer in use, please see @allegiance officer."),
            [ClientDirectiveId.TogglePersistentDaylight] = static (me, _) =>
            {
                bool turnedOn = !me._bindings.IsPersistentDaylight();
                me._bindings.SetPersistentDaylight(turnedOn);
                me._bindings.ShowSystemMessage(turnedOn
                    ? "Let there be light!"
                    : "Normality has been restored.");
            },
            [ClientDirectiveId.ShowVersion] = static (me, _) => me._bindings.ShowSystemMessage(
                me._bindings.IsUsingTurbineChat()
                    ? $"Client version {me._bindings.ClientVersion()}\nUsing Turbine Chat."
                    : $"Client version {me._bindings.ClientVersion()}"),
            [ClientDirectiveId.ShowLocation] = static (me, _) =>
            {
                Locus? locus = me._bindings.CurrentPosition();
                me._bindings.ShowSystemMessage(locus is { ObjCellId: not 0u }
                    ? $"Your location is: {CanonPositionText.Format(locus.Value)}"
                    : "Not in valid cell!");
            },
            [ClientDirectiveId.ShowLastCorpseLocation] = static (me, _) =>
            {
                Locus? corpse = me._bindings.LastOutsideCorpsePosition();
                string? coordinates = corpse is null
                    ? null
                    : CanonPositionText.ComposeExteriorChamber(corpse.Value.ObjCellId);
                me._bindings.ShowSystemMessage(coordinates is null
                    ? "We're sorry, but we have no record of your last outside corpse location."
                    : $"The last time you died outside, your corpse was located at ({coordinates}).");
            },
            [ClientDirectiveId.Die] = static (me, _) => me._bindings.ShowConfirmation(
                "Do you really want to kill your character? You may drop items and accrue a vitae penalty.",
                approved =>
                {
                    if (approved)
                        me._bindings.Suicide();
                }),

            // Interface layout.
            [ClientDirectiveId.RenderOption] = static (me, cmd) => me.PerformRasterizeKnob(cmd.Arguments),
            [ClientDirectiveId.SaveUi] = static (me, cmd) => me.PerformWidgetProfile(cmd.Arguments, persist: true),
            [ClientDirectiveId.LoadUi] = static (me, cmd) => me.PerformWidgetProfile(cmd.Arguments, persist: false),
            [ClientDirectiveId.SaveAutoUi] = static (me, cmd) =>
            {
                if (me.DemandNoArgs(cmd.Arguments, "/saveautoui"))
                    me._bindings.SaveAutoUi();
            },
            [ClientDirectiveId.LoadAutoUi] = static (me, cmd) =>
            {
                if (me.DemandNoArgs(cmd.Arguments, "/loadautoui"))
                    me._bindings.LoadAutoUi();
            },

            // Chat, and who may reach you through it.
            [ClientDirectiveId.ClearChat] = static (me, cmd) => me._bindings.ClearChat(
                LeadArg(cmd.Arguments).Equals("all", StringComparison.OrdinalIgnoreCase)),
            [ClientDirectiveId.ChatLogFile] = static (me, cmd) => me.PerformCommsTraceFile(cmd.Arguments),
            [ClientDirectiveId.Away] = static (me, cmd) => me.PerformAway(cmd.Arguments),
            [ClientDirectiveId.Consent] = static (me, cmd) => me.PerformConsent(cmd.Arguments),
            [ClientDirectiveId.Permit] = static (me, cmd) => me.PerformPermit(cmd.Arguments),
            [ClientDirectiveId.Friends] = static (me, cmd) => me.PerformFriends(cmd.Arguments),
            [ClientDirectiveId.FriendsAdd] = static (me, cmd) => me.AppendFriend(cmd.Arguments),
            [ClientDirectiveId.FriendsRemove] = static (me, cmd) => me.DropFriend(cmd.Arguments),
            [ClientDirectiveId.Squelch] = static (me, cmd) => me.PerformSquelch(cmd.Arguments, append: true),
            [ClientDirectiveId.Unsquelch] = static (me, cmd) => me.PerformSquelch(cmd.Arguments, append: false),
            [ClientDirectiveId.Filter] = static (me, cmd) => me.PerformGlobalSift(cmd.Arguments, append: true),
            [ClientDirectiveId.Unfilter] = static (me, cmd) => me.PerformGlobalSift(cmd.Arguments, append: false),
            [ClientDirectiveId.FillComponents] = static (me, cmd) => me.PerformPopulateModules(cmd.Arguments),
            [ClientDirectiveId.SetChatTitle] = static (me, cmd) => me._bindings.SetChatTitle(cmd.Arguments.Trim()),
            [ClientDirectiveId.ListMessageTypes] = static (me, _) => me._bindings.ShowSystemMessage(
                "Squelch channels are as follows:\n  " + string.Join(", ", MsgKinds.Values)),
            [ClientDirectiveId.Emote] = static (me, cmd) =>
            {
                if (!string.IsNullOrWhiteSpace(cmd.Arguments))
                    me._bindings.SendEmote(cmd.Arguments.Trim());
            },
            [ClientDirectiveId.ChatToggle] = static (me, cmd) => me._bindings.ModifyGlobalSquelch(
                cmd.Arguments.Equals("off", StringComparison.OrdinalIgnoreCase), 2u),
            [ClientDirectiveId.NoTellToggle] = static (me, cmd) => me._bindings.ModifyGlobalSquelch(
                cmd.Arguments.Equals("on", StringComparison.OrdinalIgnoreCase), 3u),

            // Channels. An unrecognised channel name is reported the same way for all three.
            [ClientDirectiveId.IndexChannels] = static (me, _) => me._bindings.RequestChannelIndex(),
            [ClientDirectiveId.JoinChannel] = static (me, cmd) =>
            {
                if (CanonClientDirectiveRegistry.TryLocateEnterDepartKnob(cmd.Arguments, out uint enterKnob))
                    me._bindings.SetSingleCharacterOption(enterKnob, true);
            },
            [ClientDirectiveId.LeaveChannel] = static (me, cmd) =>
            {
                if (CanonClientDirectiveRegistry.TryLocateEnterDepartKnob(cmd.Arguments, out uint departKnob))
                    me._bindings.SetSingleCharacterOption(departKnob, false);
            },
            [ClientDirectiveId.ListChannel] = static (me, cmd) =>
                me.OnNamedChannel(cmd.Arguments, lane => me._bindings.RequestChannelList(lane)),
            [ClientDirectiveId.OnChannel] = static (me, cmd) =>
                me.OnNamedChannel(cmd.Arguments, lane => me._bindings.JoinGmChannel(lane)),
            [ClientDirectiveId.OffChannel] = static (me, cmd) =>
                me.OnNamedChannel(cmd.Arguments, lane => me._bindings.LeaveGmChannel(lane)),

            // Housing.
            [ClientDirectiveId.HouseAvailableList] = static (me, cmd) =>
            {
                if (CanonClientDirectiveRegistry.TryLocateHouseKind(cmd.Arguments, out uint houseKind))
                    me._bindings.RequestAvailableHouses(houseKind);
            },
            [ClientDirectiveId.HouseAbandon] = static (me, _) => me._bindings.ShowConfirmation(
                "Do you really want to abandon your house? Any items in the house (on hooks or in storage) will stay with the house, and you will lose access to them.",
                leadApproved =>
                {
                    if (!leadApproved)
                        return;

                    // Asked twice on purpose: abandoning is not recoverable.
                    me._bindings.ShowConfirmation(
                        "Are you absolutely certain you wish to abandon your house? Click yes only if you are sure!",
                        secondApproved =>
                        {
                            if (secondApproved)
                                me._bindings.AbandonHouse();
                        });
                }),
        };

        foreach (ClientDirectiveId directive in AdministrationDirectives)
        {
            table[directive] = static (me, cmd) =>
                _ = me._administration.TryPerform(cmd.Command, cmd.Arguments);
        }

        return table;
    }

    /// <summary>
    /// Resolves a channel name and acts on it, reporting the one retail error when it is not a
    /// channel. Listing, joining and leaving all did this, each spelling out the same failure.
    /// </summary>
    private void OnNamedChannel(string arguments, Action<uint> act)
    {
        if (CanonChannelTagChart.TryResolve(arguments.Trim(), out uint laneIdent))
            act(laneIdent);
        else
            _bindings.ShowWeenieError(0x0422u);
    }

    private void PerformWidgetProfile(string arguments, bool persist)
    {
        string[] pieces = DivideArgs(arguments);
        string directive = persist ? "saveui" : "loadui";
        if (pieces.Length > 1)
        {
            _bindings.ShowSystemMessage($"Please use @help {directive} for proper usage.");
            return;
        }

        string label = pieces.Length is 0 ? string.Empty : pieces[0];
        if (label.Length > 16)
        {
            _bindings.ShowSystemMessage("The file name must be 16 characters or less.");
            return;
        }

        if (persist) _bindings.SaveUi(label);
        else _bindings.LoadUi(label);
    }

    private void PerformRasterizeKnob(string arguments)
    {
        string[] pieces = DivideArgs(arguments);
        if (pieces.Length is 0
            || pieces[0].Equals("usage", StringComparison.OrdinalIgnoreCase))
        {
            _bindings.ShowSystemMessage(CanonDirectiveHelpChart.Render.TrimEnd('\n'));
            return;
        }

        if (pieces[0].Equals("radius", StringComparison.OrdinalIgnoreCase))
        {
            if (pieces.Length < 2)
            {
                _bindings.ShowSystemMessage("Must specify a radius");
                return;
            }

            int radius = CanonAtoi(pieces[1]);
            if (radius is < 5 or > 25)
            {
                _bindings.ShowSystemMessage("Radius must be between 5 and 25");
                return;
            }

            _bindings.SetLandscapeRadius(radius);
            _bindings.ShowSystemMessage("Landscape radius set");
            return;
        }

        if (!pieces[0].Equals("fov", StringComparison.OrdinalIgnoreCase))
            return;

        if (pieces.Length < 2)
        {
            _bindings.ShowSystemMessage("Must specify a field of view");
            return;
        }

        int fieldOfLens = CanonAtoi(pieces[1]);
        if (fieldOfLens is < 10 or > 160)
        {
            _bindings.ShowSystemMessage(
                "Field of view must be between 10 and 160");
            return;
        }

        _bindings.SetFieldOfView(fieldOfLens);
        _bindings.ShowSystemMessage("Field of view set");
    }

    private void PerformCommsTraceFile(string arguments)
    {
        string label = arguments.Trim();
        var outcome = _bindings.SetChatLogFile(label);

        if (outcome.Closed)
            _bindings.ShowSystemMessage($"Chat log {outcome.ClosedName} closed.");

        if (label.Length is 0)
        {
            _bindings.ShowSystemMessage(outcome.Closed
                ? "Chat output now directed only to the screen."
                : "Please specify a file to append chat messages to.");
            return;
        }

        _bindings.ShowSystemMessage(outcome.Opened
            ? $"Copying chat to {outcome.Name}.  Run command again with no arguments to turn off logging."
            : $"Failed to redirect to file {outcome.Name}!");
    }

    private void PerformAway(string arguments)
    {
        string lead = LeadArg(arguments);
        if (lead.Length is 0 || lead.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            if (!_bindings.IsAway()) _bindings.SetAway(true);
            return;
        }

        if (lead.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (_bindings.IsAway()) _bindings.SetAway(false);
            return;
        }

        if (lead.Equals("msg", StringComparison.OrdinalIgnoreCase))
        {
            string msg = RemainderFollowingLeadArg(arguments).Trim(' ');
            if (msg.Length > 191) msg = msg[..191];
            if (msg.Length > 0 && !msg.Contains('\n')) msg += "\n";
            _bindings.SetAwayMessage(msg);
            _bindings.ShowSystemMessage(msg.Length is 0
                ? "New AFK message set: I am currently away from the keyboard."
                : $"New AFK message set: {msg}");
            return;
        }

        _bindings.ShowSystemMessage(AwayHelp);
    }

    private void PerformConsent(string arguments)
    {
        string lead = LeadArg(arguments);
        if (lead.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            if (!_bindings.AcceptLootPermits()) _bindings.SetAcceptLootPermits(true);
            _bindings.ShowSystemMessage(
                "You can now accept corpse looting permissions from other players.");
        }
        else if (lead.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (_bindings.AcceptLootPermits()) _bindings.SetAcceptLootPermits(false);
            _bindings.ShowSystemMessage(
                "You are no longer accepting corpse looting permissions from other players.");
        }
        else if (lead.Equals("who", StringComparison.OrdinalIgnoreCase))
        {
            _bindings.DisplayConsent();
        }
        else if (lead.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _bindings.ClearConsent();
        }
        else if (lead.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            string label = RemainderFollowingLeadArg(arguments).Trim();
            if (label.Length is 0)
                _bindings.ShowSystemMessage(
                    "Please specify a person to remove from your consent list.");
            else
                _bindings.RemoveConsent(label);
        }
        else
        {
            _bindings.ShowSystemMessage("Please specify a valid consent command.");
        }
    }

    private void PerformFriends(string arguments)
    {
        string op = LeadArg(arguments);
        if (op.Length is 0)
        {
            ShowFriends(onlineSole: false);
            return;
        }

        if (op.Equals("online", StringComparison.OrdinalIgnoreCase))
        {
            ShowFriends(onlineSole: true);
            return;
        }

        string remainder = RemainderFollowingLeadArg(arguments);
        if (op.Equals("add", StringComparison.OrdinalIgnoreCase))
            AppendFriend(remainder);
        else if (op.Equals("remove", StringComparison.OrdinalIgnoreCase))
            DropFriend(remainder);
        else if (op.Equals("old", StringComparison.OrdinalIgnoreCase)
                 && string.IsNullOrWhiteSpace(remainder))
            _bindings.RequestLegacyFriends();
        else
            _bindings.ShowSystemMessage("Invalid friends command specified.");
    }

    private void PerformSquelch(string arguments, bool append)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            ShowToonSquelches();
            return;
        }

        if (!TryDecodeSquelch(arguments, out MuteArguments decoded, out string problem))
        {
            _bindings.ShowSystemMessage(problem);
            return;
        }

        if (decoded.AccountWide)
            _bindings.ModifyAccountSquelch(append, decoded.Name);
        else
            _bindings.ModifyCharacterSquelch(append, 0u, decoded.Name, decoded.MessageType);
    }

    private void PerformGlobalSift(string arguments, bool append)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            ShowGlobalFilters();
            return;
        }

        string[] pieces = DivideArgs(arguments);
        if (pieces.Length is not 1 || !pieces[0].StartsWith('-'))
        {
            _bindings.ShowSystemMessage("Incorrect usage, use @help for proper usage.");
            return;
        }

        if (!TryFetchMsgKind(pieces[0][1..], out uint kind) || kind is 1u)
        {
            _bindings.ShowSystemMessage("You must specify a valid message type.");
            return;
        }

        _bindings.ModifyGlobalSquelch(append, kind);
    }

    private void PerformPermit(string arguments)
    {
        string[] pieces = DivideArgs(arguments);
        string label = string.Join(' ', pieces, 1, pieces.Length - 1);
        if (pieces[0].Equals("add", StringComparison.OrdinalIgnoreCase))
            _bindings.AddPlayerPermission(label);
        else
            _bindings.RemovePlayerPermission(label);
    }

    private void PerformPopulateModules(string arguments)
    {
        string[] pieces = DivideArgs(arguments);
        if (pieces.Length > 2)
        {
            _bindings.ShowSystemMessage("Please use @help fillcomps for proper usage.");
            return;
        }

        if (pieces.Length > 0 && pieces[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _bindings.ClearDesiredComponents();
            _bindings.ShowSystemMessage("Component list cleared.");
            return;
        }

        uint? bucket = null;
        uint ceilingPrice = 0u;
        foreach (string piece in pieces)
        {
            if (uint.TryParse(
                    piece, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint price))
            {
                if (price is 0)
                {
                    _bindings.ShowSystemMessage("Please specify a value greater than zero.");
                    return;
                }
                ceilingPrice = price;
            }
            else if (TryFetchModuleBucket(piece, out uint decodedBucket))
            {
                bucket = decodedBucket;
            }
            else
            {
                _bindings.ShowSystemMessage("Invalid component type specified.");
                return;
            }
        }

        if (!_bindings.HasOpenVendor())
        {
            _bindings.ShowSystemMessage("You need an open vendor.");
            return;
        }

        _bindings.FillComponentBuyList(bucket, ceilingPrice);
    }
}
