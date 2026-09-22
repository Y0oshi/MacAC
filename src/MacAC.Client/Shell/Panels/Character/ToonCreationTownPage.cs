using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed class ToonCreationTownPage : IDisposable
{
    private static readonly IReadOnlyDictionary<uint, int> BeginAreaByBtnIdent =
        new Dictionary<uint, int>
        {
            [0x1000040Du] = 0, // Holtburg
            [0x1000040Fu] = 1, // Shoushi
            [0x1000040Eu] = 2, // Yaraq
            [0x1000040Bu] = 3,
        };

    private static readonly IReadOnlyDictionary<int, string> TownPhraseTagByBeginArea =
        new Dictionary<int, string>
        {
            [0] = "ID_CharGen_HoltText",
            [1] = "ID_CharGen_ShoushiText",
            [2] = "ID_CharGen_YaraqText",
            [3] = "ID_CharGen_SanamarText",
        };

    private static readonly IReadOnlyDictionary<int, uint> SheetPhaseByBeginArea =
        new Dictionary<int, uint>
        {
            [0] = 0x10000034u, // Holtburg
            [1] = 0x10000037u, // Shoushi
            [2] = 0x10000036u, // Yaraq
            [3] = 0x10000035u, // Sanamar
        };

    private readonly ToonCreationEngineWiring _bindings;
    private readonly WidgetElem _sheetTrunk;
    private readonly Dictionary<WidgetBtn, int> _btns = [];
    private readonly WidgetPhrase? _blurb;
    private bool _destroyed;

    internal ToonCreationTownPage(
        WidgetElem sheetTrunk,
        ToonCreationEngineWiring mappings)
    {
        _bindings = mappings;
        _sheetTrunk = sheetTrunk;
        foreach ((uint btnIdent, int beginArea) in BeginAreaByBtnIdent)
        {
            if (WidgetElem.SeekDescendant(sheetTrunk, btnIdent) is not WidgetBtn btn)
                continue;
            _btns[btn] = beginArea;
            btn.OnClick = () => Select(beginArea);
        }

        _blurb = WidgetElem.SeekDescendant(sheetTrunk, 0x10000409u) as WidgetPhrase;
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
        foreach ((WidgetBtn btn, int beginArea) in _btns)
            btn.Selected = beginArea == capture.StartArea;

        if (SheetPhaseByBeginArea.TryGetValue(capture.StartArea, out uint sheetPhaseIdent)
            && _sheetTrunk is IWidgetDatStateful stateful)

            stateful.TrySetCanonPhase(sheetPhaseIdent);

        if (_blurb is null)
            return;

        string composed = ConstructBlurb(capture.StartArea, _bindings.ResolveText);
        DatRichPhrase.Piece[] segments = new[] { new DatRichPhrase.Piece(composed, _blurb.DefaultTint) };
        var composedStrokes = DatRichPhrase.Compose(_blurb, segments);
        _blurb.StrokesSupplier = () => composedStrokes;
    }

    internal void Randomize(ISimToonGenesisLens lens)
    {
        int tied = Math.Min(4, lens.Options.StarterAreas.Count);
        if (tied <= 0)
            return;
        Select(Random.Shared.Next(tied));
    }

    private void Select(int beginArea)
    {
        if (_destroyed)
            return;
        _bindings.SelectStartArea(beginArea);
    }

    private static string ConstructBlurb(int beginArea, Func<string, string?>? locatePhrase)
    {
        if (locatePhrase is null)
            return string.Empty;
        string? howTo = locatePhrase("ID_CharGen_TownHowTo");
        string? townPhrase = TownPhraseTagByBeginArea.TryGetValue(beginArea, out string? tag)
            ? locatePhrase(tag)
            : null;
        return howTo is null && townPhrase is null ? string.Empty : $"{howTo}\n\n{townPhrase}\n";
    }
}
