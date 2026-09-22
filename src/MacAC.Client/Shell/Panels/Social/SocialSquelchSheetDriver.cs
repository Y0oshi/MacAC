using System.Numerics;
using MacAC.Mechanics.Fellows;

namespace MacAC.Client.Shell.Panels;

public sealed class SocialSquelchSheetDriver
{
    private const uint RosterBboxIdent = 0x1000053Eu;
    private const uint LabelFieldIdent = 0x10000540u;
    private const uint DropBtnIdent = 0x10000547u;
    private const uint SquelchToonBtnIdent = 0x1000054Bu;
    private const uint SquelchAcctBtnIdent = 0x1000054Cu;

    public sealed record ClientActions(
        Action<string> SquelchCharacter,
        Action<string> SquelchAccount,
        Action<uint, string> RemoveCharacterSquelch,
        Action<string> RemoveAccountSquelch);

    private readonly WidgetBlueprintRosterBbox _rosterBbox;
    private readonly SquelchLedger _squelch;
    private readonly ClientActions? _actions;
    private readonly WidgetField? _labelField;
    private long _previousRev = long.MinValue;
    private (uint Guid, string Name, bool IsAccount)? _chosen;

    private SocialSquelchSheetDriver(
        WidgetBlueprintRosterBbox rosterBbox,
        SquelchLedger squelch,
        ClientActions? acts,
        WidgetField? labelField)
    {
        _rosterBbox = rosterBbox;
        _squelch = squelch;
        _actions = acts;
        _labelField = labelField;
    }

    public static SocialSquelchSheetDriver? Bind(
        WidgetElem sheetTrunk,
        SquelchLedger squelch,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        ClientActions? acts = null)
    {
        ArgumentNullException.ThrowIfNull(sheetTrunk);
        ArgumentNullException.ThrowIfNull(squelch);
        ArgumentNullException.ThrowIfNull(blueprintLocator);

        if (WidgetElem.SeekDescendant(sheetTrunk, RosterBboxIdent) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] SocialSquelchSheetDriver: ListBox 0x{RosterBboxIdent:X8} not "
                + "found - Squelch page will not populate");
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
                $"[UI] SocialSquelchSheetDriver: scrollbar 0x{scrollerElemIdent:X8} "
                + "not found - the Squelch list will not scroll");

        WidgetField? labelField = WidgetElem.SeekDescendant(sheetTrunk, LabelFieldIdent) as WidgetField;
        var driver = new SocialSquelchSheetDriver(rosterBbox, squelch, acts, labelField);
        driver.WireActs(sheetTrunk);
        driver.Refresh();
        return driver;
    }

    public void Tick()
    {
        long rev = _squelch.Revision;
        if (rev == _previousRev) return;
        Refresh();
    }

    private void WireActs(WidgetElem sheetTrunk)
    {
        if (_actions is not { } acts) return;

        if (WidgetElem.SeekDescendant(sheetTrunk, SquelchToonBtnIdent) is WidgetBtn appendToon)
            appendToon.OnClick = () =>
            {
                string label = _labelField?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label)) return;
                acts.SquelchCharacter(label);
                _labelField?.AssignPhrase(string.Empty);
            };

        if (WidgetElem.SeekDescendant(sheetTrunk, SquelchAcctBtnIdent) is WidgetBtn appendAcct)
            appendAcct.OnClick = () =>
            {
                string label = _labelField?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label)) return;
                acts.SquelchAccount(label);
                _labelField?.AssignPhrase(string.Empty);
            };

        if (WidgetElem.SeekDescendant(sheetTrunk, DropBtnIdent) is WidgetBtn drop)
            drop.OnClick = () =>
            {
                if (_chosen is not { } chosen) return;
                if (chosen.IsAccount)
                    acts.RemoveAccountSquelch(chosen.Name);
                else
                    acts.RemoveCharacterSquelch(chosen.Guid, chosen.Name);
            };
    }

    private void Refresh()
    {
        long rev = _squelch.Revision;
        _rosterBbox.Flush();
        SquelchBook database = _squelch.Snapshot();

        bool allRanksSettled = true;
        bool chosenStillPresent = false;
        foreach ((uint oid, SquelchFacts toon) in database.Characters)
        {
            allRanksSettled &= AppendRank(toon.Name, oid, isAcct: false);
            if (_chosen is { IsAccount: false } s && s.Guid == oid)
                chosenStillPresent = true;
        }
        foreach (string acctLabel in database.Accounts.Keys)
        {
            allRanksSettled &= AppendRank(acctLabel, 0u, isAcct: true);
            if (_chosen is { IsAccount: true } s && s.Name == acctLabel)
                chosenStillPresent = true;
        }
        if (!chosenStillPresent) _chosen = null;

        if (allRanksSettled) _previousRev = rev;
    }

    private bool AppendRank(string label, uint oid, bool isAcct)
    {
        WidgetElem? rank = _rosterBbox.AppendGearFromBlueprintRoster(0);
        if (rank is null) return false;
        if (SocialPaneRowText.SeekDeepest(rank) is { } phrase)
        {
            phrase.StrokesSupplier = () => [new WidgetPhrase.Line(label, Vector4.One)];
            phrase.OnClick = () => _chosen = (oid, label, isAcct);
        }
        return true;
    }
}
