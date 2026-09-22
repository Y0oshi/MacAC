using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ArcanacastingWidgetDriver : IRetainedPaneDriver
{
    public const uint SheetIdent = 0x10000061u;

    public const uint ArcanumLabelIdent = 0x1000048Bu;

    public const uint EndowmentIdent = 0x100000B1u;

    public const uint CastingBtnIdent = 0x100000B2u;

    public const uint FavoriteScrollerIdent = 0x100000B5u;

    public const uint FavoriteRosterIdent = 0x100000B6u;

    private static readonly uint[] TabIdents =
    [
        0x100000A3u, 0x100000A4u, 0x100000A5u, 0x100000A6u,
        0x100000A7u, 0x100000A8u, 0x100000A9u, 0x100005C2u,
    ];

    private static readonly uint[] ClusterIdents =
    [
        0x100000AAu, 0x100000ABu, 0x100000ACu, 0x100000ADu,
        0x100000AEu, 0x100000AFu, 0x100000B0u, 0x100005C3u,
    ];

    private readonly Grimoire _grimoire;

    private readonly SimArcanaCastLedger _casting;

    private readonly PickPhase _pick;

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly Func<uint, uint> _locateArcanumGlyph;

    private readonly Func<ClientThing, uint> _locateGearPullGlyph;

    private readonly Action<uint> _useGear;

    private readonly Action<uint>? _examineArcanum;

    private readonly Action<int, int, uint>? _appendFavorite;

    private readonly Action<int, uint>? _dropFavorite;

    private readonly WidgetShortcutDigitGraphics? _shortcutDigits;

    private readonly uint _vacantSocketSprite;

    private readonly WidgetElem[] _tabs;

    private readonly WidgetElem[] _clusters;

    private readonly WidgetGearRoster?[] _rosters;

    private readonly WidgetScroller?[] _scrollbars;

    private readonly WidgetBtn _cast;

    private readonly WidgetPhrase? _arcanumLabel;

    private readonly WidgetElem _endowmentHub;

    private readonly WidgetRegistrySlot _endowmentSocket;
    private readonly uint?[] _chosen = new uint?[8];

    private readonly bool[] _endowmentChosen = new bool[8];

    private readonly float[] _favoriteViewRectWidths = new float[8];

    private uint _endowmentGearIdent;

    private uint _endowmentArcanumIdent;

    private bool _destroyed;

    private bool _favoritesStale;

    private bool _endowmentStale;

    private bool _favoritePullEngaged;

    private ArcanacastingWidgetDriver(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        SimArcanaCastLedger casting,
        ClientThingChart objects,
        Func<uint> avatarOid,
        Func<uint, uint> locateArcanumGlyph,
        Func<ClientThing, uint> locateGearPullGlyph,
        Action<uint> useGear,
        Action<uint>? examineArcanum,
        PickPhase pick,
        Action<int, int, uint>? appendFavorite,
        Action<int, uint>? dropFavorite,
        WidgetElem[] tabs,
        WidgetElem[] clusters,
        WidgetGearRoster?[] rosters,
        WidgetScroller?[] scrollbars,
        WidgetBtn cast,
        WidgetElem endowmentHub,
        WidgetShortcutDigitGraphics? shortcutDigits,
        uint vacantSocketSprite)
    {
        _grimoire = grimoire;
        _casting = casting;
        _pick = pick;
        _objects = objects;
        _avatarOid = avatarOid;
        _locateArcanumGlyph = locateArcanumGlyph;
        _locateGearPullGlyph = locateGearPullGlyph;
        _useGear = useGear;
        _examineArcanum = examineArcanum;
        _appendFavorite = appendFavorite;
        _dropFavorite = dropFavorite;
        _shortcutDigits = shortcutDigits;
        _vacantSocketSprite = vacantSocketSprite;
        _tabs = tabs;
        _clusters = clusters;
        _rosters = rosters;
        _scrollbars = scrollbars;
        _cast = cast;
        _arcanumLabel = arrangement.SeekElem(ArcanumLabelIdent) as WidgetPhrase;
        _endowmentHub = endowmentHub;
        foreach (WidgetElem descendant in _endowmentHub.Children) descendant.Visible = false;
        _endowmentSocket = new WidgetRegistrySlot
        {
            Left = 0f,
            Top = 0f,
            Width = endowmentHub.Width,
            Height = endowmentHub.Height,
            SpriteResolve = rosters.FirstOrDefault(roster => roster is not null)?.SpriteResolve,
            Clicked = PickEndowment,
            DoubleClicked = () => { PickEndowment(); InvokeChosen(); }
        };
        _endowmentHub.AddChild(_endowmentSocket);

        for (int idx = 0; idx < _rosters.Length; ++idx)
        {
            if (_rosters[idx] is not { } list)
                continue;
            list.PrimaryRegistryListingPressed = PickArcanum;
            list.ExamineRegistryListingAsked = _examineArcanum;
            if (_scrollbars[idx] is not { } scroller)
                continue;
            list.HorizontalRoll = true;
            scroller.Horizontal = true;
            scroller.Model = list.Scroll;
            scroller.SpriteResolve ??= list.SpriteResolve;
        }

        for (int idx = 0; idx < tabs.Length; ++idx)
        {
            int ordinal = idx;
            AssignPress(tabs[idx], () => PickTab(ordinal));
        }
        _cast.OnClick = InvokeChosen;
        ConfigureArcanumLabel();
        _grimoire.SpellbookChanged += OnGrimoireAltered;
        _pick.Changed += OnPickAltered;
        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.ObjectRemoved += OnObjectAltered;
        _objects.Cleared += OnObjectsCleared;
        RefreshEndowment();
        PickTab(0);
        Rebuild();
        for (int tab = 0; tab < _rosters.Length; ++tab)
            _favoriteViewRectWidths[tab] = _rosters[tab]?.Width ?? 0f;
    }
}

public sealed record ArcanaFavoriteDragPayload(int SourceTab, int SourcePosition, uint SpellId);

public sealed record ArcanabookShortcutDragPayload(uint SpellId);

internal static class FavoriteRosterExtensions
{
    public static int OrdinalOf(this IReadOnlyList<uint> vals, uint val)
    {
        for (int idx = 0; idx < vals.Count; ++idx) if (vals[idx] == val) return idx;
        return -1;
    }
}
