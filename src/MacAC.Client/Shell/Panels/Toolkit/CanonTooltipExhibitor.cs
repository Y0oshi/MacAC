namespace MacAC.Client.Shell.Panels;

public sealed class CanonTooltipExhibitor : IDisposable
{
    private readonly WidgetTrunk _hub;

    private readonly Func<uint, uint, ImportedArrangement?> _buildArrangement;

    private WidgetElem? _popupTrunk;
    private WidgetElem? _holder;
    private bool _destroyed;

    public CanonTooltipExhibitor(WidgetTrunk host, Func<uint, uint, ImportedArrangement?> createLayout)
    {
        _hub = host ?? throw new ArgumentNullException(nameof(host));
        _buildArrangement = createLayout ?? throw new ArgumentNullException(nameof(createLayout));
        _hub.TooltipShow += OnTooltipShow;
        _hub.TooltipHide += OnHintConceal;
    }

    public bool Enabled { get; set; } = true;

    public void ConcealLatest()
    {
        DropPopup();
        _realmHoverOid = 0u;
        _realmLinedPhrase = null;
        _realmRearmRequiresPointerRelocate = false;
    }

    public void Tick()
    {
        if (_popupTrunk is not null)
            _hub.BringToFront(_popupTrunk);

        RefreshRealmHoverHint();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _hub.TooltipShow -= OnTooltipShow;
        _hub.TooltipHide -= OnHintConceal;
        DropPopup();
    }

    private static string? LocateHintPhrase(WidgetElem widget, out bool fromCore)
    {
        string? core = widget.FetchHintPhrase();
        if (!string.IsNullOrEmpty(core))
        {
            fromCore = true;
            return core;
        }
        fromCore = false;
        return widget.AuthoredHintPhrase;
    }

    private void OnTooltipShow(WidgetElem widget)
    {
        DropPopup();

        if (!Enabled)
            return;

        string? hintPhrase = LocateHintPhrase(widget, out bool fromCore);
        if (string.IsNullOrEmpty(hintPhrase))
            return;

        if (!fromCore && !widget.AuthoredHintTurnedOn)
            return;

        if (widget.AuthoredHintTrunkElemIdent is 0u)
            return;

        uint arrangementDid = widget.AuthoredHintArrangementDid is not 0u
            ? widget.AuthoredHintArrangementDid
            : widget.SrcArrangementDid;
        if (arrangementDid is 0u)
            return;

        if (TryAssembleAndMountPopup(widget.AuthoredHintTrunkElemIdent, arrangementDid, hintPhrase!))
            _holder = widget;
    }

    public const uint SharedPopupSkinTrunkElemIdent = 0x10000395u;
    public const uint SharedPopupSkinArrangementDid = 0x21000041u;

    private uint _realmHoverOid;
    private bool _realmHintShowing;

    private string? _realmLinedPhrase;

    private long _realmHintShownMsec;

    private bool _realmRearmRequiresPointerRelocate;

    private int _realmPreviousObservedPointerX = int.MinValue;
    private int _realmPreviousObservedPointerY = int.MinValue;

    public Func<uint?>? RealmHoverOidSupplier { get; set; }

    public Func<uint, string?>? RealmHoverLabelLocator { get; set; }

    public Func<bool>? RealmHintsTurnedOn { get; set; }

    private bool TryAssembleAndMountPopup(uint trunkElemIdent, uint arrangementDid, string hintPhrase)
    {
        DropPopup();

        ImportedArrangement? arrangement;
        try
        {
            arrangement = _buildArrangement(arrangementDid, trunkElemIdent);
        }
        catch (Exception problem)
        {
            Console.WriteLine(
                $"[UI] tooltip popup layout=0x{arrangementDid:X8} "
                + $"root=0x{trunkElemIdent:X8} could not build: {problem.Message}");
            return false;
        }
        if (arrangement is null)
            return false;

        WidgetElem trunk = arrangement.Root;
        WidgetElem? phraseDescendant = trunk.AuthoredHintPhraseDescendantElemIdent is not 0u
            ? arrangement.SeekElem(trunk.AuthoredHintPhraseDescendantElemIdent)
            : null;
        if (phraseDescendant is not WidgetPhrase phrase)
            return false;

        trunk.ArrangementRule = null;
        trunk.Moorings = MooringRims.None;
        phrase.ArrangementRule = null;
        phrase.Moorings = MooringRims.None;

        ImposeHintPhrase(trunk, phrase, hintPhrase);

        AssignPressThroughRecursive(trunk);
        PlaceAtPointer(trunk);

        _hub.AddChild(trunk);
        _hub.BringToFront(trunk);
        _popupTrunk = trunk;
        return true;
    }

    private void OnHintConceal(WidgetElem widget)
    {
        if (ReferenceEquals(_holder, widget))
            DropPopup();
    }

    private void DropPopup()
    {
        if (_popupTrunk is null)
            return;
        _hub.DropDescendant(_popupTrunk);
        _popupTrunk = null;
        _holder = null;
        _realmHintShowing = false;
    }

    private const float PointerShiftPx = 32f;

    private void RefreshRealmHoverHint()
    {
        if (RealmHoverOidSupplier is null)
            return;

        if (_hub.PointerX != _realmPreviousObservedPointerX || _hub.PointerY != _realmPreviousObservedPointerY)
        {
            _realmPreviousObservedPointerX = _hub.PointerX;
            _realmPreviousObservedPointerY = _hub.PointerY;
            _realmRearmRequiresPointerRelocate = false;
        }

        uint located = _hub.Pick(_hub.PointerX, _hub.PointerY) is null
            ? RealmHoverOidSupplier() ?? 0u
            : 0u;

        if (located != _realmHoverOid)
        {
            _realmHoverOid = located;

            string? lined = _realmLinedPhrase;
            if (located is 0u || RealmHintsTurnedOn?.Invoke() != true)
            {
                lined = null;
            }
            else
            {
                string? label = RealmHoverLabelLocator?.Invoke(located);
                if (!string.IsNullOrEmpty(label))
                    lined = label;
            }

            if (!string.Equals(lined, _realmLinedPhrase, StringComparison.Ordinal))
            {
                _realmLinedPhrase = lined;
                if (_realmHintShowing)
                    DropPopup();

                if (!string.IsNullOrEmpty(lined) && _hub.PullSrc is not null)
                {
                    if (TryAssembleAndMountPopup(
                            SharedPopupSkinTrunkElemIdent, SharedPopupSkinArrangementDid, lined!))
                    {
                        _realmHintShowing = true;
                        _realmHintShownMsec = _hub.InstantMsec;
                    }
                    return;
                }
            }
        }

        if (_realmHintShowing)
        {
            if (_hub.InstantMsec - _realmHintShownMsec >= _hub.HintIntervalMsec)
            {
                DropPopup();
                _realmRearmRequiresPointerRelocate = true;
            }
            return;
        }

        if (string.IsNullOrEmpty(_realmLinedPhrase))
            return;

        if (_hub.Captured is not null)
            return;

        if (_realmRearmRequiresPointerRelocate)
            return;

        if (_hub.PointerIdleMsec < _hub.HintDelayMsec)
            return;

        if (!Enabled)
            return;

        if (TryAssembleAndMountPopup(
                SharedPopupSkinTrunkElemIdent, SharedPopupSkinArrangementDid, _realmLinedPhrase!))
        {
            _realmHintShowing = true;
            _realmHintShownMsec = _hub.InstantMsec;
        }
    }

    private void ImposeHintPhrase(WidgetElem trunk, WidgetPhrase phrase, string hintPhrase)
    {
        float authoredPhraseWidth = phrase.Width;
        float authoredPhraseHeight = phrase.Height;
        phrase.Padding = 0f;

        Func<string, float> gauge = phrase.DatFont is { } datTypeface
            ? datTypeface.MeasureWidth
            : phrase.Font is { } bitmapTypeface
                ? bitmapTypeface.MeasureWidth
                : static s => s.Length * 8f;

        float strokeHeight = phrase.DatFont?.LineHeight ?? phrase.Font?.LineHeight ?? 14f;

        float marginsX = phrase.MarginLeft + phrase.MarginRight;
        float marginsY = phrase.MarginTop + phrase.MarginBottom;

        float encloseTied = phrase.AuthoredRescaleUpperWidth is { } authoredUpperPhraseWidth
            ? MathF.Max(1f, authoredUpperPhraseWidth)
            : MathF.Max(1f, _hub.NetCanvasDims.X);
        float gaugeEncloseWidth = MathF.Max(1f, encloseTied - marginsX);
        var measured = WidgetPhrase.EncloseWords(hintPhrase, gauge, gaugeEncloseWidth);
        float measuredWidth =
            (measured.Count is 0 ? 0f : measured.Max(gauge)) + marginsX;
        float measuredHeight = measured.Count * strokeHeight + marginsY;

        float askedWidth = trunk.Width + (measuredWidth - authoredPhraseWidth);
        float askedHeight = trunk.Height + (measuredHeight - authoredPhraseHeight);

        if (trunk.AuthoredRescaleUpperHeight is { } upperHeight && askedHeight > upperHeight)
            askedHeight = upperHeight;
        if (trunk.AuthoredRescaleLowerHeight is { } lowerHeight && askedHeight < lowerHeight)
            askedHeight = lowerHeight;
        if (trunk.AuthoredRescaleUpperWidth is { } upperWidth && askedWidth > upperWidth)
            askedWidth = upperWidth;
        if (trunk.AuthoredRescaleLowerWidth is { } lowerWidth && askedWidth < lowerWidth)
            askedWidth = lowerWidth;

        float authoredTrunkWidth = trunk.Width;
        float authoredTrunkHeight = trunk.Height;
        trunk.Width = askedWidth;
        trunk.Height = askedHeight;

        float phraseFinalWidth = MathF.Max(
            1f, authoredPhraseWidth + (askedWidth - authoredTrunkWidth));
        if (phrase.AuthoredRescaleUpperWidth is { } phraseUpperWidth)
            phraseFinalWidth = MathF.Min(phraseFinalWidth, phraseUpperWidth);
        float phraseFinalHeight =
            authoredPhraseHeight + (askedHeight - authoredTrunkHeight);

        var wrapped = WidgetPhrase.EncloseWords(
            hintPhrase, gauge, MathF.Max(1f, phraseFinalWidth - marginsX));
        phrase.StrokesSupplier = () => wrapped
            .Select(stroke => new WidgetPhrase.Line(stroke, phrase.DefaultTint))
            .ToArray();
        phrase.Width = wrapped.Count is 0
            ? 0f
            : MathF.Min(phraseFinalWidth, wrapped.Max(gauge) + marginsX);
        float rewrappedHeight = wrapped.Count * strokeHeight + marginsY;

        if (rewrappedHeight > phraseFinalHeight)
        {
            float grownHeight = trunk.Height + (rewrappedHeight - phraseFinalHeight);
            if (trunk.AuthoredRescaleUpperHeight is { } upperH2 && grownHeight > upperH2)
                grownHeight = upperH2;
            if (trunk.AuthoredRescaleLowerHeight is { } lowerH2 && grownHeight < lowerH2)
                grownHeight = lowerH2;
            trunk.Height = grownHeight;
        }
        phrase.Height = rewrappedHeight;
    }

    private void PlaceAtPointer(WidgetElem trunk)
    {
        System.Numerics.Vector2 canvas = _hub.NetCanvasDims;
        float x = Math.Clamp(_hub.PointerX + PointerShiftPx, 0, MathF.Max(0f, canvas.X - trunk.Width));
        float y = Math.Clamp(_hub.PointerY + PointerShiftPx, 0, MathF.Max(0f, canvas.Y - trunk.Height));
        trunk.Left = x;
        trunk.Top = y;
    }

    private static void AssignPressThroughRecursive(WidgetElem elem)
    {
        elem.ClickThrough = true;
        foreach (WidgetElem descendant in elem.Children)
            AssignPressThroughRecursive(descendant);
    }
}
