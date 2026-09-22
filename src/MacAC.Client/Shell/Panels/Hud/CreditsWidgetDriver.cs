using System.Numerics;

namespace MacAC.Client.Shell.Panels;

internal sealed record CreditsWidgetResources(
    uint LayoutId,
    ImportedArrangement PictureLayout,
    ImportedArrangement TextLayout,
    IReadOnlyList<string> TextFragments,
    IReadOnlyList<uint> PictureIds,
    float SectionSeconds,
    string PleaseWait);

internal sealed class CreditsWidgetDriver : IDisposable
{
    internal const uint TrunkEnum = 0x10000004u;
    internal const uint PictureTrunkElemIdent = 0x10000413u;
    internal const uint PhraseTrunkElemIdent = 0x10000410u;
    internal const uint PhraseAreaElemIdent = 0x10000411u;
    internal const uint DynamicPictureElemIdent = 0x10000415u;

    private readonly WidgetTrunk _hub;
    private readonly ImportedArrangement _pictureArrangement;
    private readonly ImportedArrangement _phraseArrangement;
    private readonly IReadOnlyList<string> _phraseFragments;
    private readonly IReadOnlyList<uint> _pictureIdents;
    private readonly float _sectionSecs;
    private readonly CanonPromptMint _popups;
    private readonly string _pleasePause;
    private readonly Func<double> _instantSecs;
    private readonly Func<uint, (uint tex, int w, int h)> _locateSprite;
    private readonly Action _returnToToonManagement;
    private readonly CreditsActCanvas _actCanvas;
    private readonly List<WidgetBoard> _images = [];
    private readonly Vector2 _authoredCanvas;

    private WidgetPhrase.Line[] _strokes = [];
    private float _phraseHeight;
    private double _beginMoment;
    private float _previousHeadway;
    private int _upcomingPicture;
    private long _beatSeries;
    private long _returnAtBeat = long.MaxValue;
    private uint _pauseCtx;
    private bool _returnQueued;
    private bool _destroyed;

    private CreditsWidgetDriver(
        WidgetTrunk hub,
        ImportedArrangement pictureArrangement,
        ImportedArrangement phraseArrangement,
        WidgetPhrase phraseArea,
        IReadOnlyList<string> phraseFragments,
        IReadOnlyList<uint> pictureIdents,
        float sectionSecs,
        CanonPromptMint popups,
        string pleasePause,
        Func<double> instantSecs,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        Action returnToToonManagement)
    {
        _hub = hub;
        _pictureArrangement = pictureArrangement;
        _phraseArrangement = phraseArrangement;
        _phraseArea = phraseArea;
        _phraseFragments = phraseFragments;
        _pictureIdents = pictureIdents;
        _sectionSecs = sectionSecs;
        _popups = popups;
        _pleasePause = pleasePause;
        _instantSecs = instantSecs;
        _locateSprite = locateSprite;
        _returnToToonManagement = returnToToonManagement;

        float width = MathF.Max(
            PictureTrunk.Left + PictureTrunk.Width,
            PhraseTrunk.Left + PhraseTrunk.Width);
        float height = MathF.Max(
            PictureTrunk.Top + PictureTrunk.Height,
            PhraseTrunk.Top + PhraseTrunk.Height);
        _authoredCanvas = new Vector2(
            width > 0f ? width : 800f,
            height > 0f ? height : 600f);

        PictureTrunk.Visible = false;
        PhraseTrunk.Visible = false;
        _actCanvas = new CreditsActCanvas(CommenceReturn)
        {
            Width = _authoredCanvas.X,
            Height = _authoredCanvas.Y,
            Visible = false,
            ZOrder = int.MaxValue,
        };
    }

    internal WidgetElem PictureTrunk => _pictureArrangement.Root;
    internal WidgetElem PhraseTrunk => _phraseArrangement.Root;
    private readonly WidgetPhrase _phraseArea;

    internal WidgetPhrase PhraseArea => _phraseArea;
    internal IReadOnlyList<WidgetBoard> Pictures => _images;
    internal bool IsEngaged { get; private set; }
    internal double IntervalSecs { get; private set; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        bool revertToonManagement = IsEngaged;
        _destroyed = true;
        Disengage();
        _hub.DropDescendant(PictureTrunk);
        _hub.DropDescendant(PhraseTrunk);
        _hub.DropDescendant(_actCanvas);
        if (revertToonManagement)
            _returnToToonManagement();
    }

    internal static CreditsWidgetDriver? BuildDetached(
        WidgetTrunk hub,
        CreditsWidgetResources assetList,
        CanonPromptMint popups,
        Func<double> instantSecs,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        Action returnToToonManagement)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(assetList);
        ArgumentNullException.ThrowIfNull(popups);
        ArgumentNullException.ThrowIfNull(instantSecs);
        ArgumentNullException.ThrowIfNull(locateSprite);
        ArgumentNullException.ThrowIfNull(returnToToonManagement);

        if (assetList.PictureLayout.Root.DatElemIdent != PictureTrunkElemIdent
            || assetList.TextLayout.Root.DatElemIdent != PhraseTrunkElemIdent
            || assetList.TextLayout.SeekElem(PhraseAreaElemIdent) is not WidgetPhrase phraseArea
            || assetList.TextFragments.Count is 0
            || assetList.PictureIds.Count is 0
            || !float.IsFinite(assetList.SectionSeconds)
            || assetList.SectionSeconds <= 0f)
        {
            Console.WriteLine(
                "[UI] credits: authored root/text/picture contract is incomplete");
            return null;
        }

        return new CreditsWidgetDriver(
            hub,
            assetList.PictureLayout,
            assetList.TextLayout,
            phraseArea,
            assetList.TextFragments,
            assetList.PictureIds,
            assetList.SectionSeconds,
            popups,
            assetList.PleaseWait,
            instantSecs,
            locateSprite,
            returnToToonManagement);
    }

    internal void Engage()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (IsEngaged)
            return;

        FastenTrunks();
        RestartExec();
        IsEngaged = true;
        PictureTrunk.Visible = true;
        PhraseTrunk.Visible = true;
        _actCanvas.Visible = true;
        _hub.DeclareFixedCanvas(this, _authoredCanvas);
        _hub.BringToFront(PictureTrunk);
        _hub.BringToFront(PhraseTrunk);
        _hub.BringToFront(_actCanvas);
        _hub.AssignKeyboardFocus(_actCanvas);

        Tick();
    }

    internal void Tick()
    {
        if (_destroyed || !IsEngaged)
            return;

        ++_beatSeries;
        if (_returnQueued)
        {
            if (_beatSeries >= _returnAtBeat)
                ConcludeReturn();
            return;
        }

        double passed = Math.Max(0d, _instantSecs() - _beginMoment);
        float headway = IntervalSecs <= 0d
            ? 1f
            : Math.Clamp((float)(passed / IntervalSecs), 0f, 1f);
        headway = MathF.Max(headway, _previousHeadway);
        _previousHeadway = headway;

        float fieldHeight = PhraseTrunk.Height;
        int formerTop = (int)MathF.Round(_phraseArea.Top);
        int travel = (int)MathF.Round(
            (fieldHeight + _phraseHeight) * headway,
            MidpointRounding.ToEven);
        int newTop = (int)MathF.Round(fieldHeight) - travel;
        _phraseArea.Left = 0f;
        _phraseArea.Top = newTop;
        RollPictures(formerTop - newTop);

        if (headway >= 1f)
            CommenceReturn();
    }

    internal void RestartSess()
    {
        if (_destroyed || !IsEngaged)
            return;
        Disengage();
        _returnToToonManagement();
    }

    private void FastenTrunks()
    {
        if (PictureTrunk.Ancestor is null)
            _hub.AddChild(PictureTrunk);
        if (PhraseTrunk.Ancestor is null)
            _hub.AddChild(PhraseTrunk);
        if (_actCanvas.Ancestor is null)
            _hub.AddChild(_actCanvas);
    }

    private void RestartExec()
    {
        ShutPause();
        WipePictures();
        _returnQueued = false;
        _returnAtBeat = long.MaxValue;
        _previousHeadway = 0f;
        _upcomingPicture = 0;

        _phraseArea.Width = PhraseTrunk.Width;
        float ceilingWidth = Math.Max(
            1f,
            _phraseArea.Width
                - (_phraseArea.Padding + _phraseArea.MarginLeft)
                - (_phraseArea.Padding + _phraseArea.MarginRight));
        Func<string, float> gauge = _phraseArea.DatFont is { } typeface
            ? typeface.MeasureWidth
            : static val => val.Length * 8f;
        string allPhrase = string.Concat(_phraseFragments);
        IReadOnlyList<string> wrapped = WidgetPhrase.EncloseWords(
            allPhrase,
            gauge,
            ceilingWidth);
        if (wrapped.Count is 0)
            wrapped = [string.Empty];
        _strokes = [.. wrapped.Select(
            stroke => new WidgetPhrase.Line(stroke, _phraseArea.DefaultTint))];
        _phraseArea.StrokesSupplier = () => _strokes;

        float strokeHeight = _phraseArea.DatFont?.LineHeight ?? 16f;
        _phraseHeight = Math.Max(strokeHeight, strokeHeight * _strokes.Length);
        _phraseArea.Left = 0f;
        _phraseArea.Top = PhraseTrunk.Height;
        _phraseArea.Height = _phraseHeight;

        float terminatorOrdinal = _phraseFragments.Count + 1f;
        IntervalSecs = _sectionSecs
            * (PhraseTrunk.Height + _phraseHeight)
            / (PhraseTrunk.Height + _phraseHeight / terminatorOrdinal);
        _beginMoment = _instantSecs();
    }

    private void RollPictures(int diffPx)
    {
        if (diffPx is not 0)
            foreach (WidgetBoard image in _images)
                image.Top -= diffPx;

        if (_images.Count > 0
            && _images[0].Top + _images[0].Height < 0f)
        {
            WidgetBoard expired = _images[0];
            _images.RemoveAt(0);
            PictureTrunk.DropDescendant(expired);
        }

        if (_images.Count is 0)
            AppendPicture();

        if (_images.Count > 0
            && _images[^1].Top < PictureTrunk.Height)

            AppendPicture();
    }

    private void AppendPicture()
    {
        if (_pictureIdents.Count is 0)
            return;

        uint pictureIdent = _pictureIdents[_upcomingPicture];
        _upcomingPicture = (_upcomingPicture + 1) % _pictureIdents.Count;
        (uint texture, int width, int height) = _locateSprite(pictureIdent);
        if (texture is 0u || width <= 0 || height <= 0)
            return;

        float top = _images.Count is 0
            ? PictureTrunk.Height + 1f
            : _images[^1].Top + _images[^1].Height + 1f;
        WidgetBoard image = new WidgetBoard
        {
            DatElemIdent = DynamicPictureElemIdent,
            Left = 0f,
            Top = top,
            Width = width,
            Height = height,
            BackgroundColor = Vector4.Zero,
            BorderTint = Vector4.Zero,
            BorderThickness = 0f,
            BackgroundSprite = pictureIdent,
            SpriteResolve = _locateSprite,
            ClickThrough = true,
        };
        PictureTrunk.AddChild(image);
        _images.Add(image);
    }

    private void CommenceReturn()
    {
        if (_destroyed || !IsEngaged || _returnQueued)
            return;

        _returnQueued = true;
        _pauseCtx = _popups.CraftPause(_pleasePause);
        _returnAtBeat = _beatSeries + 2;
    }

    private void ConcludeReturn()
    {
        if (!IsEngaged)
            return;
        Disengage();
        _returnToToonManagement();
    }

    private void Disengage()
    {
        _returnQueued = false;
        _returnAtBeat = long.MaxValue;
        IsEngaged = false;
        PictureTrunk.Visible = false;
        PhraseTrunk.Visible = false;
        _actCanvas.Visible = false;
        if (ReferenceEquals(_hub.KeyboardFocus, _actCanvas))
            _hub.AssignKeyboardFocus(null);
        _hub.RevokeFixedCanvas(this);
        ShutPause();
        WipePictures();
    }

    private void ShutPause()
    {
        uint ctx = _pauseCtx;
        _pauseCtx = 0u;
        if (ctx is not 0u)
            _popups.ShutPopup(ctx);
    }

    private void WipePictures()
    {
        foreach (WidgetBoard image in _images)
            PictureTrunk.DropDescendant(image);
        _images.Clear();
    }

    private sealed class CreditsActCanvas : WidgetElem
    {
        private readonly Action _onAct;

        public override bool HndsPress => true;

        public CreditsActCanvas(Action onAction)
        {
            _onAct = onAction ?? throw new ArgumentNullException(nameof(onAction));
            AcceptsFocus = true;
            ClickThrough = false;
        }

        public override bool OnSignal(in WidgetSignal e)
        {
            if (!Enabled || !Visible)
                return false;
            if (e.Type is WidgetEventType.TagDown
                or WidgetEventType.PointerDown
                or WidgetEventType.RightDown
                or WidgetEventType.MiddleDown
                or WidgetEventType.Roll)
            {
                _onAct();
                return true;
            }
            return e.Type is WidgetEventType.TagUp
                or WidgetEventType.PointerUp
                or WidgetEventType.RightUp
                or WidgetEventType.MiddleUp
                or WidgetEventType.Click
                or WidgetEventType.RightPress;
        }
    }
}
