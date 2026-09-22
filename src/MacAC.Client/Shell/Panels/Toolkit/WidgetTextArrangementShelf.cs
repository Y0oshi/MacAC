using System.Numerics;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell.Panels;

internal sealed class WidgetTextArrangementShelf<T>
{
    private readonly WidgetPhrase _mark;
    private readonly Func<WidgetPhrase, T, IReadOnlyList<WidgetPhrase.Line>> _form;
    private readonly IEqualityComparer<T> _comparer;
    private readonly Func<T>? _src;
    private T _val = default!;
    private bool _hasVal;
    private bool _shaped;
    private IReadOnlyList<WidgetPhrase.Line> _strokes = Array.Empty<WidgetPhrase.Line>();
    private float _width;
    private float _padding;
    private Vector4 _defaultTint;
    private WidgetDatFont? _datTypeface;
    private BitmapFont? _bitmapTypeface;
    private IReadOnlyList<Vector4>? _typefaceTintSwatch;

    public WidgetTextArrangementShelf(
        WidgetPhrase mark,
        Func<WidgetPhrase, T, IReadOnlyList<WidgetPhrase.Line>> form,
        T startingVal,
        IEqualityComparer<T>? comparer = null)
        : this(mark, form, src: null, comparer, bootstrap: true)
    {
        SetValue(startingVal);
    }

    public WidgetTextArrangementShelf(
        WidgetPhrase mark,
        Func<WidgetPhrase, T, IReadOnlyList<WidgetPhrase.Line>> form,
        Func<T> source,
        IEqualityComparer<T>? comparer = null)
        : this(
            mark,
            form,
            source ?? throw new ArgumentNullException(nameof(source)),
            comparer,
            bootstrap: true)
    {
    }

    private WidgetTextArrangementShelf(
        WidgetPhrase target,
        Func<WidgetPhrase, T, IReadOnlyList<WidgetPhrase.Line>> shape,
        Func<T>? src,
        IEqualityComparer<T>? comparer,
        bool bootstrap)
    {
        _ = bootstrap;
        _mark = target ?? throw new ArgumentNullException(nameof(target));
        _form = shape ?? throw new ArgumentNullException(nameof(shape));
        _src = src;
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public Func<IReadOnlyList<WidgetPhrase.Line>> Provider => FetchStrokes;

    public void SetValue(T val)
    {
        if (_hasVal && _comparer.Equals(_val, val))
            return;

        _val = val;
        _hasVal = true;
        _shaped = false;
    }

    public void Invalidate() => _shaped = false;

    public IReadOnlyList<WidgetPhrase.Line> FetchStrokes()
    {
        if (_src is not null)
            SetValue(_src());

        if (!_hasVal)
            return Array.Empty<WidgetPhrase.Line>();

        if (!_shaped || ArrangementAltered())
        {
            GrabArrangement();
            _strokes = _form(_mark, _val);
            _shaped = true;
        }

        return _strokes;
    }

    private bool ArrangementAltered()
    {
        return _width != _mark.Width
               || _padding != _mark.Padding
               || _defaultTint != _mark.DefaultTint
               || !ReferenceEquals(_datTypeface, _mark.DatFont)
               || !ReferenceEquals(_bitmapTypeface, _mark.Font)
               || !ReferenceEquals(_typefaceTintSwatch, _mark.TypefaceTintSwatch);
    }

    private void GrabArrangement()
    {
        _width = _mark.Width;
        _padding = _mark.Padding;
        _defaultTint = _mark.DefaultTint;
        _datTypeface = _mark.DatFont;
        _bitmapTypeface = _mark.Font;
        _typefaceTintSwatch = _mark.TypefaceTintSwatch;
    }
}
