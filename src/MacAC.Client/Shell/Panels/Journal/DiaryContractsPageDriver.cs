using System.Numerics;
using MacAC.Mechanics.Contracts;
using MacAC.Mechanics.Shell;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed class DiaryContractsPageDriver
{
    public const uint RankBlueprintArrangementIdent = 0x21000069u;

    public const uint RankBlueprintElemIdent = 0x100005D7u;

    private const uint RosterIdent = 0x100005CFu;
    private const uint RankLabelIdent = 0x100005D1u;
    private const uint RankConditionIdent = 0x100005D2u;

    private const uint ConditionValIdent = 0x100005DFu;
    private const uint LinkValIdent = 0x100005E0u;
    private const uint LinkLocaleValIdent = 0x100005E1u;
    private const uint QuestLocaleValIdent = 0x100005E2u;
    private const uint BlurbIdent = 0x100005DEu;
    private const uint TimedValIdent = 0x100005E3u;
    private const uint AbandonBtnIdent = 0x100005DCu;

    public sealed record Bindings(
        ISimContractLens Contracts,
        Func<QuestCatalogue> Catalog,
        Func<DateTime> Now,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        Action<uint>? Abandon = null);

    private readonly Bindings _bindings;
    private readonly WidgetBlueprintRosterBbox? _roster;
    private readonly WidgetPhrase? _conditionVal;
    private readonly WidgetPhrase? _linkVal;
    private readonly WidgetPhrase? _linkLocaleVal;
    private readonly WidgetPhrase? _questLocaleVal;
    private readonly WidgetPhrase? _blurb;
    private readonly WidgetPhrase? _timedVal;

    private static readonly Vector4 ChosenLabelTint = Vector4.One;

    private readonly List<uint> _rankContractIdents = [];
    private readonly List<(uint ContractId, WidgetPhrase? Name, Vector4 Unselected)> _ranks = [];

    private long _renderedRev = -1;

    public DiaryContractsPageDriver(WidgetElem sheet, Bindings bindings)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

        _roster = WidgetElem.SeekDescendant(sheet, RosterIdent) as WidgetBlueprintRosterBbox;
        _roster?.TemplateResolver = bindings.TemplateResolver;

        _conditionVal = WidgetElem.SeekDescendant(sheet, ConditionValIdent) as WidgetPhrase;
        _linkVal = WidgetElem.SeekDescendant(sheet, LinkValIdent) as WidgetPhrase;
        _linkLocaleVal =
            WidgetElem.SeekDescendant(sheet, LinkLocaleValIdent) as WidgetPhrase;
        _questLocaleVal =
            WidgetElem.SeekDescendant(sheet, QuestLocaleValIdent) as WidgetPhrase;
        _blurb = WidgetElem.SeekDescendant(sheet, BlurbIdent) as WidgetPhrase;
        _timedVal = WidgetElem.SeekDescendant(sheet, TimedValIdent) as WidgetPhrase;

        if (WidgetElem.SeekDescendant(sheet, AbandonBtnIdent) is WidgetBtn abandon)
            abandon.OnClick = AbandonChosen;

        Refresh();
    }

    public uint ChosenContractIdent { get; private set; }

    public IReadOnlyList<uint> RankContractIdents => _rankContractIdents;

    public void Tick()
    {
        if (_bindings.Contracts.Snapshot.Revision != _renderedRev)
            Refresh();
        else
            RenewSpecifics();
    }

    public void Refresh()
    {
        var capture = _bindings.Contracts.Snapshot;
        _renderedRev = capture.Revision;

        var contracts = _bindings.Contracts.FetchContracts();
        var registry = _bindings.Catalog();
        DateTime instant = _bindings.Now();

        if (capture.DisplayContractId is not 0u)
            ChosenContractIdent = capture.DisplayContractId;
        if (ChosenContractIdent is 0u && contracts.Count is not 0)
            ChosenContractIdent = contracts[0].ContractId;
        if (contracts.Count is 0)
            ChosenContractIdent = 0u;

        _rankContractIdents.Clear();
        _ranks.Clear();
        _roster?.DrainPreservingRoll();

        foreach (QuestTracker tracker in contracts)
        {
            _rankContractIdents.Add(tracker.ContractId);
            if (_roster is null)
                continue;

            WidgetElem? rank = _roster.AppendGearFromBlueprintRoster(0);
            if (rank is null)
                continue;

            QuestRow listing = registry.Consult(tracker.ContractId);

            WidgetPhrase? label = WidgetElem.SeekDescendant(rank, RankLabelIdent) as WidgetPhrase;
            if (label is not null)
                AssignPhrase(label, listing.ContractName);
            if (WidgetElem.SeekDescendant(rank, RankConditionIdent) is WidgetPhrase condition)
            {
                AssignPhrase(condition, QuestProgressText.Build(
                    (uint)tracker.Stage, tracker.TimeWhenRepeats,
                    tracker.ReceivedAt, listing, instant));
            }

            _ranks.Add((tracker.ContractId, label, label?.DefaultTint ?? Vector4.One));

            uint grabbed = tracker.ContractId;
            if (rank is WidgetDatElement clickable)
            {
                clickable.ClickThrough = false;
                clickable.OnClick = () => Select(grabbed);
            }
        }

        ImposePickHighlight();
        RenewSpecifics();
    }

    public void AbandonChosen()
    {
        if (ChosenContractIdent is 0u)
            return;

        _bindings.Abandon?.Invoke(ChosenContractIdent);
    }

    public void Select(uint contractIdent)
    {
        ChosenContractIdent = contractIdent;
        ImposePickHighlight();
        RenewSpecifics();
    }

    private void ImposePickHighlight()
    {
        foreach ((uint contractIdent, WidgetPhrase? label, Vector4 unselected) in _ranks)
        {
            label?.DefaultTint =
                    contractIdent == ChosenContractIdent ? ChosenLabelTint : unselected;
        }
    }

    private void RenewSpecifics()
    {
        var registry = _bindings.Catalog();
        DateTime instant = _bindings.Now();

        if (ChosenContractIdent is 0u
            || !_bindings.Contracts.TryFetchContract(ChosenContractIdent, out QuestTracker tracker))
        {
            AssignPhrase(_conditionVal, string.Empty);
            AssignPhrase(_linkVal, string.Empty);
            AssignPhrase(_linkLocaleVal, string.Empty);
            AssignPhrase(_questLocaleVal, string.Empty);
            AssignPhrase(_blurb, string.Empty);
            AssignPhrase(_timedVal, string.Empty);
            return;
        }

        QuestRow listing = registry.Consult(ChosenContractIdent);

        AssignPhrase(_conditionVal, QuestProgressText.Build(
            (uint)tracker.Stage, tracker.TimeWhenRepeats, tracker.ReceivedAt, listing, instant));
        AssignPhrase(_linkVal, listing.NameNpcStart);
        AssignPhrase(_linkLocaleVal, LocalePhrase(listing.LocationNpcStartCell));
        AssignPhrase(_questLocaleVal, LocalePhrase(listing.LocationQuestAreaCell));
        AssignPhrase(_blurb, listing.Description);

        AssignPhrase(_timedVal, tracker.TimeWhenDone > 0d
            ? CanonDurationText.Format(
                Math.Max(0d, tracker.TimeWhenDone - (instant - tracker.ReceivedAt).TotalSeconds))
            : string.Empty);
    }

    private static string LocalePhrase(uint chamberIdent)
    {
        return chamberIdent is 0u ? string.Empty : CanonPositionText.ComposeExteriorChamber(chamberIdent) ?? "Indoors";
    }

    private static void AssignPhrase(WidgetPhrase? phrase, string val)
    {
        if (phrase is null) return;
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(val, phrase.DefaultTint)];
    }
}
