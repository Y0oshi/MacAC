using MacAC.Cockpit.Input;
using MacAC.Mechanics.Controls;

namespace MacAC.Client.Shell.Panels;

public sealed partial class KeyboardSettingsDriver
{
    public const uint LayoutId = 0x21000009u;

    public const uint PaneTrunkElemIdent = 0x1000001Fu;

    private const uint PullBtnIdent = 0x10000027u;

    private const uint FilenameCaptionIdent = 0x10000028u;

    private const uint PersistAsBtnIdent = 0x10000029u;

    private const uint DefaultsBtnIdent = 0x1000002Au;

    private const uint UndoBtnIdent = 0x1000002Bu;

    private const uint OkBtnIdent = 0x1000002Cu;

    private const uint AbortBtnIdent = 0x1000002Du;

    private const uint RosterBboxElemIdent = 0x10000025u;

    private const uint ScrollbarElementId = 0x10000026u;

    private const int PreambleBlueprintOrdinal = 0;

    private const int RankBlueprintOrdinal = 1;

    private const uint TabHubElemIdent = 0x1000049Bu;

    private static readonly uint[] TagBtnIdents = [0x10000030u, 0x10000031u, 0x10000032u];

    private static readonly (uint PageContainerId, CanonActionClass Class)[] Sheets =
    [
        (0x1000049Du, CanonActionClass.Movement),
        (0x1000049Fu, CanonActionClass.Camera),
        (0x100004A1u, CanonActionClass.Combat),
        (0x100004A3u, CanonActionClass.Ui),
        (0x10000211u, CanonActionClass.CharacterSettings),
        (0x100004A5u, CanonActionClass.Emote),
    ];

    public sealed record RankLens(
        uint InputMapId,
        uint ActionId,
        FeedAct? MappedAction,
        string? Label,
        ActionKeyMapKnobRow Model,
        IReadOnlyList<WidgetBtn> KeyButtons);

    public sealed record Bindings(
        Func<FeedAct, IReadOnlyList<CockpitBinding>> CurrentForAction,
        Action<FeedAct, IReadOnlyList<CockpitBinding>> SetForAction,
        Func<(uint InputMapId, uint ActionId), IReadOnlyList<KeyStroke>> CurrentForUnmapped,
        Action<(uint InputMapId, uint ActionId), IReadOnlyList<KeyStroke>> SetForUnmapped,
        Action<Action<KeyStroke?>> BeginCapture,
        Action Save,
        Action Toggle,
        Func<string, IReadOnlyDictionary<uint, string>, string?> ResolveTemplate,
        Action<string> ShowMessage,
        Action<string, Action<bool>> ConfirmOverwrite,
        Func<string, uint>? OpenCaptureInstructions = null,
        Action<uint>? CloseCaptureInstructions = null,
        Func<string>? CurrentKeymapFilename = null,
        Action<Action>? OpenLoadKeymap = null,
        Action<Action>? OpenSaveKeymap = null);

    public KnobPage Page { get; } = new();

    private readonly List<RankLens> _ranks = [];

    private readonly Dictionary<(uint LayoutId, uint ElementId), WidgetDatFont?> _blueprintTypefaceStash = [];

    private CanonActionMapFrame? _capture;

    private Bindings? _bindings;

    private Func<KeyStroke, string> _depict = DepictChord;

    private Func<uint, uint, WidgetDatFont?>? _locateBlueprintTypeface;

    private static readonly uint ActVariable = DatStringPicker.CalculateDigest("ACTION");

    private static readonly uint MappingsVariable = DatStringPicker.CalculateDigest("BINDINGS");

    private static readonly uint TagVariable = DatStringPicker.CalculateDigest("KEY");

    private static readonly uint CaptionVariable = DatStringPicker.CalculateDigest("LABEL");

    private static readonly uint ValVariable = DatStringPicker.CalculateDigest("VALUE");

    private static readonly IReadOnlyDictionary<uint, string> VacantBlueprintVariables =
        new Dictionary<uint, string>();

    private enum ConflictVerdict { None, NonBindable, Rows }
}
