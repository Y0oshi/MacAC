using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetMarkupList : WidgetElem
{
    public Func<IReadOnlyList<string>> GearListSrc { get; set; } =
        static () => Array.Empty<string>();

    public Func<IReadOnlyList<uint>> GearTintsSrc { get; set; } =
        static () => Array.Empty<uint>();

    public Func<int> ChosenOrdinalSrc { get; set; } = static () => -1;

    public float RowHeight { get; set; } = 18f;

    public float Padding { get; set; } = 3f;

    public Vector4 BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.92f);

    public Vector4 BorderColor { get; set; } = new(0.46f, 0.37f, 0.16f, 1f);

    public Vector4 TextColor { get; set; } = new(0.91f, 0.87f, 0.76f, 1f);

    public Vector4 SelectedColor { get; set; } = new(0.28f, 0.23f, 0.08f, 0.95f);

    private const float ScrollerWidth = 16f;

    private const float RollBtnReach = 16f;

    private int _topRank;
    private int _previousRevealedChosen = int.MinValue;

    private readonly WidgetScrollable _roll = new();

    private bool _draggingThumb;

    private float _thumbPullShift;

    private IReadOnlyList<string>?[] _stashedPhraseRanks = [];

    private IReadOnlyList<uint>?[] _stashedTintRanks = [];

    private IReadOnlyList<bool>?[] _stashedVerifyRanks = [];

    private IReadOnlyList<uint>?[] _stashedGlyphRanks = [];

    private (float x, float w)[] _cachedLayout = [];

    private bool[] _tempIsAuto = [];

    private float[] _tempFixedWidth = [];

    private int _stashedRankTally;
}
