using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Client.Shell.Panels;
using MacAC.Cockpit.Panels.SpewBox;

namespace MacAC.Client.Shell;

internal sealed class SpewBoxDriver : IDisposable
{
    internal const uint CanonTypefaceIdent = 0x40000001u;

    private const float TopShift = 0f;

    private const float SpewBboxWidth = 450f;
    private const float SpewBboxHeight = 72f;

    private static readonly Vector4 SpewBboxTint = new(1f, 1f, 0.247f, 1f);

    private readonly WidgetTrunk _trunk;
    private readonly WidgetPhrase _phrase;
    private readonly SpewBoxModel _vm;
    private readonly GlobalMomentDrain _momentDrain;
    private readonly Func<bool> _isGameplayEngaged;
    private WidgetPhrase.Line[] _strokes = [];
    private bool _destroyed;

    public SpewBoxDriver(
        WidgetTrunk root, SpewBoxModel vm, WidgetDatFont? typeface = null, BitmapFont? diagTypeface = null,
        Func<bool>? isGameplayEngaged = null)
    {
        _trunk = root ?? throw new ArgumentNullException(nameof(root));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _isGameplayEngaged = isGameplayEngaged ?? (static () => true);
        _phrase = new WidgetPhrase
        {
            Name = "SpewBox",
            Left = (root.Width - SpewBboxWidth) / 2f,
            Top = TopShift,
            Width = SpewBboxWidth,
            Height = SpewBboxHeight,
            Moorings = MooringRims.None,
            Centered = true,
            DatFont = typeface,
            Font = diagTypeface,
            OneLine = false,
            VerticalJustify = ClientVJustify.Top,
            HonorVerticalJustification = true,
            ClickThrough = true,
            ZOrder = int.MaxValue,
            DefaultTint = SpewBboxTint,
            Outline = true,
            Visible = false,
            StrokesSupplier = () => _strokes
        };
        _trunk.AddChild(_phrase);

        _momentDrain = new GlobalMomentDrain(Tick);
        _trunk.AddChild(_momentDrain);
    }

    private void Tick(double instantSecs)
    {
        _phrase.Left = (_trunk.Width - SpewBboxWidth) / 2f;

        var strokes = _vm.Strokes(instantSecs);
        bool gameplay = _isGameplayEngaged();
        if (!gameplay && strokes.Count > 0)
            _vm.Clear();
        _phrase.Visible = gameplay && strokes.Count > 0;
        if (!_phrase.Visible)
        {
            _strokes = [];
            return;
        }

        WidgetPhrase.Line[] outcome = new WidgetPhrase.Line[strokes.Count];
        for (int idx = 0; idx < strokes.Count; ++idx)
            outcome[idx] = new WidgetPhrase.Line(strokes[idx].Text, SpewBboxTint);
        _strokes = outcome;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        _trunk.DropDescendant(_phrase);
        _trunk.DropDescendant(_momentDrain);
        _destroyed = true;
    }

    private sealed class GlobalMomentDrain(Action<double> onGlobalWidgetMoment) : WidgetElem, IWidgetGlobalTimeListener
    {
        private readonly Action<double> _onGlobalWidgetMoment = onGlobalWidgetMoment;

        public void OnGlobalWidgetMoment(double instantSecs) => _onGlobalWidgetMoment(instantSecs);
    }
}
