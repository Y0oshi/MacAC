using System.Numerics;
using MacAC.Extensibility.Panels;

namespace MacAC.Client.Shell;

public sealed partial class PluginSidePane : WidgetBoard, IDisposable, IRetainedWindowStateDriver, IRetainedPaneDriver
{
    private const float OuterPadding = 4f;

    private const float BtnReach = 28f;

    private const float BtnGap = 4f;

    private const float DefaultTop = 116f;

    private const float DefaultLeft = 10f;

    private const float GripHeight = 12f;

    private const float FlipWidth = 16f;

    private const float CollapsedWidth = FlipWidth + OuterPadding * 2f;

    private static readonly Vector4 FlipGlyphTint = new(0.86f, 0.72f, 0.32f, 1f);

    private readonly CanonWindowKeeper _panes;

    private readonly Func<uint, (uint tex, int width, int height)> _locate;

    private readonly WidgetDatFont? _typeface;

    private readonly Dictionary<CanonWindowHandle, ShelfListing> _listings = [];

    private readonly ShelfGripPane _grip;

    private readonly WidgetSimpleButton _flip;

    private bool _destroyed;

    private float _previousArrangementHeight = -1f;

    private bool _collapsed;

    private bool _askedShown = true;

    private bool _userPositioned;

    private bool _startingDockImposed;

    private float _dockLeft;

    private float _dockTop;

    private CanonWindowHandle? _hnd;

    public PluginSidePane(
        CanonWindowKeeper windows,
        Func<uint, (uint tex, int width, int height)> resolve,
        WidgetDatFont? typeface)
    {
        _panes = windows ?? throw new ArgumentNullException(nameof(windows));
        _locate = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _typeface = typeface;

        Width = BtnReach + OuterPadding * 2f;
        Height = ExpandedGripBandHeight + OuterPadding * 2f;
        Top = DefaultTop;
        Moorings = MooringRims.None;
        Draggable = false;
        ConstrainPullToParent = true;
        Resizable = false;
        ResizeX = false;
        ResizeY = false;
        BackgroundColor = new Vector4(0f, 0f, 0f, 0.88f);
        BorderTint = new Vector4(0.62f, 0.48f, 0.16f, 1f);
        BorderThickness = 1f;
        Visible = false;

        _grip = new ShelfGripPane
        {
            PaneRelocateHnd = true,
            BackgroundColor = Vector4.Zero,
            BorderTint = Vector4.Zero,
            Moorings = MooringRims.None,
        };
        _flip = new WidgetSimpleButton
        {
            BackgroundColor = Vector4.Zero,
            BorderTint = Vector4.Zero,
            TextTint = FlipGlyphTint,
            DatFont = _typeface,
            Outline = true,
            WordingSrc = () => _collapsed ? "<" : ">",
            Moorings = MooringRims.None,
        };
        _flip.Click += FlipCollapsed;
        AddChild(_grip);
        AddChild(_flip);
        ArrangementChrome();

        _panes.WindowUnregistered += OnPaneUnregistered;
        _panes.WindowRegistered += OnPaneRegistered;
    }

    private readonly record struct ShelfListing(
        ExtensionShelfBtn Button,
        ExtensionMinimizeBtn Minimize);

    private sealed class ShelfGripPane : WidgetBoard
    {
        private static readonly Vector4 DashTint = new(0.62f, 0.48f, 0.16f, 1f);

        protected override void OnPaint(WidgetRenderScope cx)
        {
            base.OnPaint(cx);

            const float dashWidth = 5f;
            const float dashGap = 4f;
            float sumDashWidth = dashWidth * 3f + dashGap * 2f;
            float dashX = MathF.Max(2f, (Width - sumDashWidth) * 0.5f);
            float dashY = Height * 0.5f - 1f;
            for (int idx = 0; idx < 3; ++idx)
                cx.SketchPopulate(dashX + idx * (dashWidth + dashGap), dashY, dashWidth, 2f, DashTint);
        }
    }

    internal sealed class ExtensionShelfBtn : WidgetSimpleButton
    {
        private static readonly Vector4 ConcealedBackground =
            new(0.025f, 0.025f, 0.02f, 0.96f);
        private static readonly Vector4 ShownBackground =
            new(0.09f, 0.19f, 0.055f, 0.96f);
        private static readonly Vector4 ConcealedBorder =
            new(0.48f, 0.38f, 0.14f, 1f);
        private static readonly Vector4 ShownBorder =
            new(0.76f, 0.64f, 0.25f, 1f);

        private readonly CanonWindowHandle _hnd;
        private readonly Func<uint, (uint tex, int width, int height)> _locate;
        private readonly uint _glyphCanvasIdent;
        private readonly string _hint;
        private readonly string _initialsBackup;

        private bool _glyphLocateAttempted;
        private bool _glyphOnHand;

        internal ExtensionShelfBtn(
            PanelBlueprint descriptor,
            string holderReadoutLabel,
            CanonWindowHandle hnd,
            Func<uint, (uint tex, int width, int height)> locate,
            WidgetDatFont? typeface)
        {
            _hnd = hnd;
            _locate = locate;
            _glyphCanvasIdent = IconIdRules.Standardize(descriptor.GlyphCanvasIdent);
            _hint = string.Equals(descriptor.Title, holderReadoutLabel,
                    StringComparison.Ordinal)
                ? descriptor.Title
                : $"{holderReadoutLabel} — {descriptor.Title}";
            _initialsBackup = Initials(descriptor.GlyphPhrase, descriptor.Title);
            Text = _glyphCanvasIdent is 0 ? _initialsBackup : string.Empty;
            DatFont = typeface;
            Outline = true;
            BorderThickness = 1f;
            Moorings = MooringRims.None;
            _hnd.Shown += OnVisAltered;
            _hnd.Hidden += OnVisAltered;
            RenewExhibit();
        }

        public override string? FetchHintPhrase() => _hint;

        protected override void OnBeat(double diffSecs)
        {
            base.OnBeat(diffSecs);
            RenewExhibit();
        }

        protected override void OnPaint(WidgetRenderScope cx)
        {
            if (!_glyphLocateAttempted && _glyphCanvasIdent is not 0)
            {
                _glyphLocateAttempted = true;
                (uint bmp, int w, int h) = _locate(_glyphCanvasIdent);
                _glyphOnHand = bmp is not 0 && w > 0 && h > 0;
                if (!_glyphOnHand)
                    Text = _initialsBackup;
            }

            base.OnPaint(cx);

            if (_glyphCanvasIdent is 0 || !_glyphOnHand)
                return;

            (uint texture, int width, int height) = _locate(_glyphCanvasIdent);
            if (texture is 0 || width <= 0 || height <= 0)
                return;
            float reach = MathF.Min(Width - 6f, Height - 6f);
            cx.SketchSprite(
                texture,
                (Width - reach) * 0.5f,
                (Height - reach) * 0.5f,
                reach,
                reach,
                0f,
                0f,
                1f,
                1f,
                Vector4.One);
        }

        internal void TeardownSubscriptions()
        {
            _hnd.Shown -= OnVisAltered;
            _hnd.Hidden -= OnVisAltered;
        }

        private void OnVisAltered(CanonWindowHandle _) =>
            RenewExhibit();

        private void RenewExhibit()
        {
            BackgroundColor = _hnd.IsVisible
                ? ShownBackground
                : ConcealedBackground;
            BorderTint = _hnd.IsVisible ? ShownBorder : ConcealedBorder;
        }

        private static string Initials(string? asked, string banner)
        {
            if (!string.IsNullOrWhiteSpace(asked))
                return asked.Trim()[..Math.Min(3, asked.Trim().Length)];

            string[] words = banner.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length is 0)
                return "?";
            return words.Length is 1
                ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                : string.Concat(words.Take(2).Select(static word =>
                char.ToUpperInvariant(word[0])));
        }
    }

    private sealed class ExtensionMinimizeBtn : WidgetSimpleButton
    {
        private readonly CanonWindowHandle _hnd;

        internal ExtensionMinimizeBtn(CanonWindowHandle hnd, WidgetDatFont? typeface)
        {
            _hnd = hnd;
            Text = "–";
            DatFont = typeface;
            Outline = true;
            BackgroundColor = new Vector4(0.02f, 0.02f, 0.015f, 0.94f);
            BorderTint = new Vector4(0.58f, 0.46f, 0.17f, 1f);
            BorderThickness = 1f;
            Click += () => _hnd.Hide();
        }

        public override string? FetchHintPhrase() => "Minimize to plugin sidepanel";
    }
}
