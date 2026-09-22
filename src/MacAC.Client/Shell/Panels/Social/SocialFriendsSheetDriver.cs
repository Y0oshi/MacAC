using System.Numerics;
using MacAC.Mechanics.Fellows;

namespace MacAC.Client.Shell.Panels;

public sealed class SocialFriendsSheetDriver
{
    private const uint RosterBboxIdent = 0x10000517u;
    private const uint AppendBtnIdent = 0x10000514u;
    private const uint DropBtnIdent = 0x10000515u;
    private const uint AppearOfflineTickboxIdent = 0x1000052Cu;
    private const uint LabelFieldIdent = 0x1000051Bu;

    public sealed record Acts(
        Action<string> AddFriend,
        Action<uint> RemoveFriend,
        Func<bool> CurrentAppearOffline,
        Action<bool> SetAppearOffline);

    private readonly WidgetBlueprintRosterBbox _rosterBbox;
    private readonly FriendsLedger _friends;
    private readonly Acts? _actions;
    private readonly WidgetField? _labelField;
    private readonly WidgetBtn? _appearOfflineTickbox;
    private long _previousRev = long.MinValue;
    private uint _chosenFriendOid;

    private SocialFriendsSheetDriver(
        WidgetBlueprintRosterBbox rosterBbox,
        FriendsLedger friends,
        Acts? acts,
        WidgetField? labelField,
        WidgetBtn? appearOfflineTickbox)
    {
        _rosterBbox = rosterBbox;
        _friends = friends;
        _actions = acts;
        _labelField = labelField;
        _appearOfflineTickbox = appearOfflineTickbox;
    }

    public static SocialFriendsSheetDriver? Bind(
        WidgetElem sheetTrunk,
        FriendsLedger friends,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Acts? acts = null)
    {
        ArgumentNullException.ThrowIfNull(sheetTrunk);
        ArgumentNullException.ThrowIfNull(friends);
        ArgumentNullException.ThrowIfNull(blueprintLocator);

        if (WidgetElem.SeekDescendant(sheetTrunk, RosterBboxIdent) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] SocialFriendsSheetDriver: ListBox 0x{RosterBboxIdent:X8} not "
                + "found - Friends page will not populate");
            return null;
        }
        rosterBbox.TemplateResolver = blueprintLocator;

        uint scrollerElemIdent = rosterBbox.ScrollbarElementId;
        WidgetElem? scrollerElem = scrollerElemIdent is 0
            ? null
            : WidgetElem.SeekDescendant(sheetTrunk, scrollerElemIdent);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] SocialFriendsSheetDriver: scrollbar 0x{scrollerElemIdent:X8} "
                + "not found - the Friends list will not scroll");

        WidgetField? labelField = WidgetElem.SeekDescendant(sheetTrunk, LabelFieldIdent) as WidgetField;
        WidgetBtn? appearOffline = WidgetElem.SeekDescendant(sheetTrunk, AppearOfflineTickboxIdent) as WidgetBtn;
        var driver = new SocialFriendsSheetDriver(
            rosterBbox, friends, acts, labelField, appearOffline);
        driver.WireActs(sheetTrunk);
        driver.Refresh();
        return driver;
    }

    public void Tick()
    {
        if (_actions is { } acts && _appearOfflineTickbox is { } tickbox)
            tickbox.Selected = acts.CurrentAppearOffline();

        long rev = _friends.Revision;
        if (rev == _previousRev) return;
        Refresh();
    }

    private void WireActs(WidgetElem sheetTrunk)
    {
        if (_actions is not { } acts) return;

        if (WidgetElem.SeekDescendant(sheetTrunk, AppendBtnIdent) is WidgetBtn append)
            append.OnClick = () =>
            {
                string label = _labelField?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label)) return;
                acts.AddFriend(label);
                _labelField?.AssignPhrase(string.Empty);
            };

        if (WidgetElem.SeekDescendant(sheetTrunk, DropBtnIdent) is WidgetBtn drop)
            drop.OnClick = () =>
            {
                if (_chosenFriendOid is not 0u)
                    acts.RemoveFriend(_chosenFriendOid);
            };

        if (_appearOfflineTickbox is { } tickbox)
        {
            tickbox.SuppressSelfFlip = true;
            tickbox.OnClick = () =>
                acts.SetAppearOffline(!acts.CurrentAppearOffline());
        }
    }

    private const uint RankLabelPhraseIdent = 0x1000051Au;
    private const uint OnlinePhaseIdent = 0x10000054u;
    private const uint OfflinePhaseIdent = 0x10000055u;

    private void Refresh()
    {
        long rev = _friends.Revision;
        _rosterBbox.Flush();
        bool allRanksSettled = true;
        bool chosenStillPresent = false;
        foreach (FriendRow friend in _friends.Snapshot())
        {
            WidgetElem? rank = _rosterBbox.AppendGearFromBlueprintRoster(0);
            if (rank is null) { allRanksSettled = false; continue; }
            if (friend.Id == _chosenFriendOid) chosenStillPresent = true;
            if (WidgetElem.SeekDescendant(rank, RankLabelPhraseIdent) is WidgetPhrase labelPhrase)
            {
                string label = friend.Name;
                uint oid = friend.Id;
                labelPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(label, Vector4.One)];
                labelPhrase.OnClick = () => _chosenFriendOid = oid;
                labelPhrase.TrySetCanonPhase(
                    friend.Online ? OnlinePhaseIdent : OfflinePhaseIdent);
            }
            else
            {
                allRanksSettled = false;
            }
        }
        if (!chosenStillPresent) _chosenFriendOid = 0u;
        if (allRanksSettled) _previousRev = rev;
    }
}
