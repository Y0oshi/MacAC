using MacAC.Sim;

namespace MacAC.Client.Shell.Panels;

public sealed class ToonBannersDriver : IDisposable
{
    public const uint LatestReadoutBannerPhraseIdent = 0x1000052Fu;
    public const uint BannerRosterBboxIdent = 0x10000532u;
    public const uint SetReadoutBtnIdent = 0x10000535u;

    private const uint RankPhraseIdent = 0x10000537u;

    private const string UnknownBannerPhrase = "Unknown";

    private readonly record struct Rank(WidgetElem Root, uint TitleId);

    private readonly SimToonTitleLedger _banners;
    private readonly Func<uint, string?> _locateBanner;
    private readonly Func<uint, SimDirectiveResult> _transmitSetBanner;
    private readonly WidgetBlueprintRosterBbox _rosterBbox;
    private readonly WidgetPhrase? _readoutPhrase;
    private readonly WidgetBtn? _setReadoutBtn;
    private readonly List<Rank> _ranks = [];
    private uint? _chosenBannerIdent;
    private bool _destroyed;

    private ToonBannersDriver(
        SimToonTitleLedger banners,
        Func<uint, string?> locateBanner,
        Func<uint, SimDirectiveResult> transmitSetBanner,
        WidgetBlueprintRosterBbox rosterBbox,
        WidgetPhrase? readoutPhrase,
        WidgetBtn? setReadoutBtn)
    {
        _banners = banners;
        _locateBanner = locateBanner;
        _transmitSetBanner = transmitSetBanner;
        _rosterBbox = rosterBbox;
        _readoutPhrase = readoutPhrase;
        _setReadoutBtn = setReadoutBtn;
    }

    public static ToonBannersDriver? Bind(
        WidgetElem arrangementTrunk,
        SimToonTitleLedger banners,
        Func<uint, string?> locateBanner,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, SimDirectiveResult> transmitSetBanner)
    {
        ArgumentNullException.ThrowIfNull(arrangementTrunk);
        ArgumentNullException.ThrowIfNull(banners);
        ArgumentNullException.ThrowIfNull(locateBanner);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(transmitSetBanner);

        if (WidgetElem.SeekDescendant(arrangementTrunk, BannerRosterBboxIdent) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] ToonBannersDriver: ListBox 0x{BannerRosterBboxIdent:X8} not " +
                "found - the Titles page will not populate");
            return null;
        }
        rosterBbox.TemplateResolver = blueprintLocator;
        rosterBbox.LineHeight = 24;

        uint scrollerElemIdent = rosterBbox.ScrollbarElementId;
        WidgetElem? scrollerElem = scrollerElemIdent is 0
            ? null
            : WidgetElem.SeekDescendant(arrangementTrunk, scrollerElemIdent);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] ToonBannersDriver: scrollbar 0x{scrollerElemIdent:X8} " +
                "not found - the Titles list will not scroll");

        WidgetPhrase? readoutPhrase =
            WidgetElem.SeekDescendant(arrangementTrunk, LatestReadoutBannerPhraseIdent) as WidgetPhrase;
        WidgetBtn? setReadoutBtn =
            WidgetElem.SeekDescendant(arrangementTrunk, SetReadoutBtnIdent) as WidgetBtn;

        var driver = new ToonBannersDriver(
            banners, locateBanner, transmitSetBanner, rosterBbox, readoutPhrase, setReadoutBtn);
        driver.WireBtn();

        banners.TableReplaced += driver.OnChartReplaced;
        banners.TitleAdded += driver.OnBannerAdded;
        banners.DisplayTitleChanged += driver.OnReadoutBannerAltered;

        driver.ReassembleRanks();
        driver.RenewReadoutPhrase();
        driver.RenewBtnGhost();
        return driver;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _banners.TableReplaced -= OnChartReplaced;
        _banners.TitleAdded -= OnBannerAdded;
        _banners.DisplayTitleChanged -= OnReadoutBannerAltered;
    }

    private void WireBtn()
    {
        if (_setReadoutBtn is null) return;
        _setReadoutBtn.OnClick = () =>
        {
            if (_chosenBannerIdent is not uint ident || ident == _banners.ReadoutBannerIdent)
                return;
            _transmitSetBanner(ident);
        };
    }

    private void OnChartReplaced()
    {
        WipePick();
        ReassembleRanks();
        RenewReadoutPhrase();
        RenewBtnGhost();
    }

    private void OnBannerAdded(uint bannerIdent)
    {
        ReassembleRanks();
        RenewBtnGhost();
    }

    private void OnReadoutBannerAltered(uint bannerIdent)
    {
        WipePick();
        ImposeRankHighlights();
        RenewReadoutPhrase();
        RenewBtnGhost();
    }

    private void WipePick() => _chosenBannerIdent = null;

    private void ReassembleRanks()
    {
        _rosterBbox.DrainPreservingRoll();
        _ranks.Clear();

        var contenders = new List<(uint Id, string Text)>();
        foreach (uint ident in _banners.EarnedBannerIdents)
        {
            if (ident is 0) continue;
            string? phrase = _locateBanner(ident);
            if (phrase is null) continue;
            contenders.Add((ident, phrase));
        }
        var sorted = contenders
            .OrderBy(static c => c.Text, StringComparer.Ordinal)
            .ThenBy(static c => c.Id)
            .ToList();

        foreach ((uint ident, string phrase) in sorted)
        {
            WidgetElem? rank = _rosterBbox.AppendGearFromBlueprintRoster(0);
            if (rank is null) continue;

            if (rank is WidgetDatElement datRank)
            {
                datRank.ClickThrough = false;
                uint grabbedIdent = ident;
                datRank.OnClick = () => SelectRow(grabbedIdent);
            }

            if (WidgetElem.SeekDescendant(rank, RankPhraseIdent) is WidgetPhrase rankPhrase)
            {
                WidgetPhrase.Line[] strokes = [new WidgetPhrase.Line(phrase, rankPhrase.DefaultTint)];
                rankPhrase.StrokesSupplier = () => strokes;
            }

            _ranks.Add(new Rank(rank, ident));
        }

        if (_chosenBannerIdent is uint chosen && !_ranks.Exists(r => r.TitleId == chosen))
            _chosenBannerIdent = null;

        ImposeRankHighlights();
    }

    private void SelectRow(uint bannerIdent)
    {
        if (_destroyed) return;
        _chosenBannerIdent = bannerIdent;
        ImposeRankHighlights();
        RenewBtnGhost();
    }

    private void ImposeRankHighlights()
    {
        foreach (Rank rank in _ranks)
        {
            if (rank.Root is IWidgetDatStateful stateful)
            {
                stateful.TrySetCanonPhase(
                    rank.TitleId == _chosenBannerIdent
                        ? WidgetButtonStateMachine.Highlight
                        : WidgetButtonStateMachine.Normal);
            }
        }
    }

    private void RenewReadoutPhrase()
    {
        if (_readoutPhrase is null) return;
        string phrase = _locateBanner(_banners.ReadoutBannerIdent) ?? UnknownBannerPhrase;
        WidgetPhrase.Line[] strokes = [new WidgetPhrase.Line(phrase, _readoutPhrase.DefaultTint)];
        _readoutPhrase.StrokesSupplier = () => strokes;
    }

    private void RenewBtnGhost()
    {
        if (_setReadoutBtn is null) return;
        bool shouldGhost = _chosenBannerIdent is not uint ident || ident == _banners.ReadoutBannerIdent;
        _setReadoutBtn.TrySetCanonPhase(
            shouldGhost ? WidgetButtonStateMachine.Ghosted : WidgetButtonStateMachine.Normal);
    }
}
