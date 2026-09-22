using System.Numerics;
using MacAC.Mechanics.Genesis;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed class ToonCreationHeritagePage : IDisposable
{
    private static readonly IReadOnlyDictionary<uint, uint> LineageByBtnIdent =
        new Dictionary<uint, uint>
        {
            [0x100003BFu] = (uint)GenesisHeritage.Aluvian,
            [0x100003C1u] = (uint)GenesisHeritage.Gharundim,
            [0x100003C2u] = (uint)GenesisHeritage.Sho,
            [0x100003C3u] = (uint)GenesisHeritage.Viamontian,
            [0x10000590u] = (uint)GenesisHeritage.Shadowbound,
            [0x100005A9u] = (uint)GenesisHeritage.Gearknight,
            [0x100005E8u] = (uint)GenesisHeritage.Tumerok,
            [0x100005F1u] = (uint)GenesisHeritage.Lugian,
            [0x100005C4u] = (uint)GenesisHeritage.Empyrean,
            [0x10000591u] = (uint)GenesisHeritage.Penumbraen,
            [0x100005BFu] = (uint)GenesisHeritage.Undead,
            [0x100005C7u] = (uint)GenesisHeritage.Olthoi,
            [0x100005C8u] = (uint)GenesisHeritage.OlthoiAcid,
        };

    private static readonly IReadOnlyDictionary<uint, string> BonusAptitudesTagByLineage =
        new Dictionary<uint, string>
        {
            [(uint)GenesisHeritage.Aluvian] = "ID_CharGen_AluvianText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Gharundim] = "ID_CharGen_GaruText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Sho] = "ID_CharGen_ShoText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Viamontian] = "ID_CharGen_ViaText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Shadowbound] = "ID_CharGen_ShadText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Penumbraen] = "ID_CharGen_ShadText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Gearknight] = "ID_CharGen_GearText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Tumerok] = "ID_CharGen_AunTText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Empyrean] = "ID_CharGen_EmpText_BonusSkills_Trained",
            [(uint)GenesisHeritage.Undead] = "ID_CharGen_UndText_BonusSkills_Trained",
        };

    private static readonly IReadOnlyDictionary<uint, uint> BackdropPhaseByLineage =
        new Dictionary<uint, uint>
        {
            [(uint)GenesisHeritage.Aluvian] = 0x10000021u,
            [(uint)GenesisHeritage.Gharundim] = 0x10000022u,
            [(uint)GenesisHeritage.Sho] = 0x10000023u,
            [(uint)GenesisHeritage.Viamontian] = 0x10000024u,
            [(uint)GenesisHeritage.Shadowbound] = 0x10000058u,
            [(uint)GenesisHeritage.Gearknight] = 0x1000005Au,
            [(uint)GenesisHeritage.Tumerok] = 0x1000005Fu,
            [(uint)GenesisHeritage.Lugian] = 0x10000060u,
            [(uint)GenesisHeritage.Empyrean] = 0x1000005Cu,
            [(uint)GenesisHeritage.Penumbraen] = 0x10000059u,
            [(uint)GenesisHeritage.Undead] = 0x1000005Bu,
            [(uint)GenesisHeritage.Olthoi] = 0x1000005Du,
            [(uint)GenesisHeritage.OlthoiAcid] = 0x1000005Eu,
        };

    private readonly ToonCreationEngineWiring _bindings;
    private readonly Action<uint> _onBtnClicked;
    private readonly Dictionary<WidgetBtn, uint> _btns = [];
    private readonly WidgetPhrase? _blurb;
    private readonly WidgetElem? _backdrop;
    private bool _destroyed;

    internal ToonCreationHeritagePage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings,
        Action<uint> onBtnClicked)
    {
        _bindings = mappings;
        _onBtnClicked = onBtnClicked;
        foreach ((uint btnIdent, uint lineageIdent) in LineageByBtnIdent)
        {
            if (WidgetElem.SeekDescendant(sheetTrunk, btnIdent) is not WidgetBtn btn)
                continue;
            _btns[btn] = lineageIdent;
            btn.OnClick = () =>
            {
                _onBtnClicked(btnIdent);
                Select(lineageIdent);
            };
        }

        _blurb = WidgetElem.SeekDescendant(sheetTrunk, 0x100003C4u) as WidgetPhrase;
        _backdrop = WidgetElem.SeekDescendant(sheetTrunk, 0x100003BEu);

        if (_blurb is not null
            && WidgetElem.SeekDescendant(_blurb, 0x100002E7u) is WidgetScroller blurbRoll)

            blurbRoll.Model = _blurb.Scroll;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        foreach (WidgetBtn btn in _btns.Keys)
            btn.OnClick = null;
        _btns.Clear();
    }

    internal void Refresh(
        ISimToonGenesisLens lens,
        SimToonGenesisCapture capture)
    {
        foreach ((WidgetBtn btn, uint lineageIdent) in _btns)
            btn.Selected = lineageIdent == capture.HeritageId;

        if (_backdrop is IWidgetDatStateful backdropStateful
            && BackdropPhaseByLineage.TryGetValue(capture.HeritageId, out uint backdropPhase))

            backdropStateful.TrySetCanonPhase(backdropPhase);

        if (_blurb is null)
            return;

        var segments = ConstructSegments(
            _blurb, lens, capture.HeritageId, _bindings.ResolveText);
        var composed = DatRichPhrase.Compose(_blurb, segments);
        _blurb.StrokesSupplier = () => composed;
    }

    internal void Randomize(SimToonGenesisCapture capture)
    {
        var lens = _bindings.View();
        if (lens is null || lens.Options.HeritagesById.Count is 0)
            return;
        uint[] idents = [.. lens.Options.HeritagesById.Keys];
        uint chosen = idents[Random.Shared.Next(idents.Length)];
        Select(chosen);
    }

    private void Select(uint lineageIdent)
    {
        if (_destroyed)
            return;
        _bindings.SelectHeritage(lineageIdent);
    }

    private static IReadOnlyList<DatRichPhrase.Piece> ConstructSegments(
        WidgetPhrase blurb,
        ISimToonGenesisLens lens,
        uint lineageIdent,
        Func<string, string?>? locatePhrase)
    {
        Vector4 preambleTint = DatRichPhrase.SwatchTint(blurb, 1, new Vector4(0f, 1f, 0f, 1f));
        Vector4 corpusTint = DatRichPhrase.SwatchTint(blurb, 0, Vector4.One);

        if (locatePhrase is null)
        {
            string label = lens.Options.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? named)
                ? named.Name
                : string.Empty;
            return [new DatRichPhrase.Piece(label, corpusTint)];
        }

        var segments = new List<DatRichPhrase.Piece>();
        if (locatePhrase("ID_CharGen_Heritage_StartingSkills_Header") is { } preamble)
            segments.Add(new(preamble, preambleTint));
        if (locatePhrase("ID_CharGen_Heritage_StartingSkills") is { } corpus)
            segments.Add(new(corpus, corpusTint));
        if (locatePhrase("ID_CharGen_Heritage_BonusSkills_Trained_Header") is { } bonusPreamble)
            segments.Add(new(bonusPreamble, preambleTint));
        if (lineageIdent is not 0
            && BonusAptitudesTagByLineage.TryGetValue(lineageIdent, out string? bonusTag)
            && locatePhrase(bonusTag) is { } bonusCorpus)

            segments.Add(new(bonusCorpus, corpusTint));
        return segments;
    }
}
