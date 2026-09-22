using System.Numerics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class WidgetBtn : WidgetElem, IWidgetGlobalTimeListener, IWidgetDatStateful
{
    private readonly ElemDetails _details;

    private readonly ElemDetails _mediaDetails;

    private readonly FacetPiece[] _faceSegments;

    private readonly Func<uint, (uint tex, int w, int h)> _locate;

    private readonly string[] _segmentMediaPhases;

    private string _faceMediaPhase = "";

    private string? _previousMediaSealPhase;

    private readonly bool _hasCustomPickDuo;

    private IReadOnlyDictionary<uint, Vector4>? _phaseCaptionTints;

    private IReadOnlyDictionary<uint, bool>? _phaseCaptionOutlines;

    private bool _pressed;

    private bool _ptrOver;

    private bool _chosen;

    private bool _hotClicking;

    private bool _suppressUpcomingPress;

    private int _ptrX;

    private int _ptrY;

    private double _upcomingHotPressMoment = double.NaN;

    public Vector4 CaptionColor { get; set; } = Vector4.One;

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public Vector4 Tint { get; set; } = Vector4.One;

    public float CaptionShiftX { get; set; } = 3f;

    public CaptionAlignment CaptionAlign { get; set; } = CaptionAlignment.Center;

    public Vector4 ValTint { get; set; } = Vector4.One;

    public CaptionAlignment ValAlign { get; set; } = CaptionAlignment.Center;

    public enum CaptionAlignment { Center, Left, Right }

    private double _engagedPhaseBegunAt;

    public WidgetBtn(
        ElemDetails details,
        Func<uint, (uint tex, int w, int h)> locate,
        ElemDetails? mediaDetails = null,
        IReadOnlyList<ElemDetails>? faceSegments = null)
    {
        _details = details;
        _mediaDetails = mediaDetails ?? details;
        _faceSegments = faceSegments is null
            ? []
            : [.. faceSegments.Select(static segment => new FacetPiece(segment))];
        _segmentMediaPhases = new string[_faceSegments.Length];
        Array.Fill(_segmentMediaPhases, "");
        _locate = locate;
        ClickThrough = false;

        _hasCustomPickDuo = HasPhaseMedia(CanonWidgetStateIds.PhaseLabel(CanonWidgetStateIds.Unselected))
            && HasPhaseMedia(CanonWidgetStateIds.PhaseLabel(CanonWidgetStateIds.Selected));

        ToggleBehavior = details.TryFetchNetBool(0x0Bu, out bool flip) && flip;
        RolloverTurnedOn = details.TryFetchNetBool(0x13u, out bool rollover) && rollover;
        HotPressTurnedOn = details.TryFetchNetBool(0x0Fu, out bool hotPress) && hotPress;
        HotPressStartingDelay = details.TryFetchNetFloat(0x10u, out float startingDelay)
            ? startingDelay
            : 0f;
        HotPressRepeatInterval = details.TryFetchNetFloat(0x11u, out float repeatInterval)
            ? repeatInterval
            : 0f;
        _chosen = details.TryFetchNetBool(0x0Eu, out bool chosen) && chosen;

        if (!string.IsNullOrEmpty(details.DefaultStateName))
            EngagedCondition = details.DefaultStateName;
        else if (HasPhaseMedia("Normal"))
            EngagedCondition = "Normal";

        FaceWidth = mediaDetails?.Width ?? details.Width;
        FaceHeight = mediaDetails?.Height ?? details.Height;
        RefreshVisualPhase();
    }

    internal static IReadOnlyList<(string Text, float X, float Y)> EncloseChunkStrokes(
        string phrase,
        Func<string, float> gaugeWidth,
        float strokeHeight,
        float bboxX,
        float bboxY,
        float bboxWidth,
        float bboxHeight,
        CaptionAlignment align,
        float leftShift)
    {
        string[] strokes = phrase.Split('\n');

        float sumHeight = strokes.Length * strokeHeight;
        float beginY = bboxY + (bboxHeight - sumHeight) * 0.5f;

        var outcome = new List<(string, float, float)>(strokes.Length);
        for (int idx = 0; idx < strokes.Length; ++idx)
        {
            string stroke = strokes[idx];
            float tx = align == CaptionAlignment.Left
                ? bboxX + leftShift
                : bboxX + (bboxWidth - gaugeWidth(stroke)) * 0.5f;
            float ty = beginY + idx * strokeHeight;
            outcome.Add((stroke, tx, ty));
        }
        return outcome;
    }

    private sealed class FacetPiece
    {
        private readonly WidgetPixelRect _original;
        private readonly WidgetArrangementRule? _arrangement;

        public FacetPiece(ElemDetails details)
        {
            Info = details;
            _original = WidgetPixelRect.FromLocusAndDims(
                (int)details.X, (int)details.Y, (int)details.Width, (int)details.Height);
            if (details.HasOriginalParentSize)
                _arrangement = new WidgetArrangementRule(
                    details.Left, details.Top, details.Right, details.Bottom,
                    _original,
                    WidgetPixelRect.FromLocusAndDims(
                        0, 0, (int)details.OriginalParentWidth, (int)details.OriginalParentHeight));
        }

        public ElemDetails Info { get; }

        public WidgetPixelRect Rect(float ancestorWidth, float ancestorHeight)
        {
            return _arrangement is null
                ? _original
                : _arrangement.Apply(
                _original,
                WidgetPixelRect.FromLocusAndDims(0, 0, (int)ancestorWidth, (int)ancestorHeight));
        }
    }
}
