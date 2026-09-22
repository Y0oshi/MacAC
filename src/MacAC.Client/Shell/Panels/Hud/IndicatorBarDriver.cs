using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Wire;

namespace MacAC.Client.Shell.Panels;

public sealed class IndicatorBarDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x21000071u;

    public const uint BurdenClassIdent = 0x10000001u;
    public const uint FxListClassIdent = 0x10000002u;
    public const uint ConnectClassIdent = 0x10000003u;
    public const uint MiniPlayClassIdent = 0x10000004u;
    public const uint VitaeClassIdent = 0x10000006u;

    public const uint MiniPlayBtnIdent = 0x100000F3u;
    public const uint VitaeBtnIdent = 0x100000F4u;
    public const uint HelpfulBtnIdent = 0x100000F5u;
    public const uint HarmfulBtnIdent = 0x100000F6u;
    public const uint BurdenBtnIdent = 0x100000F7u;
    public const uint ConnectBtnIdent = 0x100000F8u;
    public const uint FinishToonSessBtnIdent = 0x100000FAu;

    public const uint PressHighlightIdent = 0x100000F2u;

    public const uint UnencumberedPhase = 14u;
    public const uint EncumberedPhase = 15u;
    public const uint HeavilyEncumberedPhase = 16u;
    public const uint ConnectionGoodPhase = 17u;
    public const uint ConnectionUncertainPhase = 18u;
    public const uint ConnectionBadPhase = 19u;
    public const uint ConnectionDisconnectedPhase = 20u;

    private const uint EncumbranceValueProp = 5u;
    private const uint EncumbranceAugProp = 0xE6u;
    private const double ConnectRefreshSecs = 4.0;
    private const double ConnectFlashSecs = 0.75;

    private readonly IndicatorBarWiring _bindings;
    private readonly WidgetBtn _connect;
    private readonly WidgetBtn _helpful;
    private readonly WidgetBtn _harmful;
    private readonly WidgetBtn _vitae;
    private readonly WidgetBtn _burden;
    private readonly WidgetBtn _miniPlay;
    private readonly WidgetBtn _finishToonSess;
    private LinkSemanticPhase _connectPhase = LinkSemanticPhase.Good;
    private double _previousConnectRefresh;
    private double _previousConnectFlash;
    private bool _destroyed;

    private enum LinkSemanticPhase
    {
        Good = 1,
        Uncertain = 2,
        Bad = 3,
        Disconnected = 4,
    }

    public void FastenPressHighlights(
        ElemDetails arrangementTrunk, Func<ElemDetails, WidgetElem?> assemble)
    {
        foreach ((WidgetBtn btn, uint ident) in Indicators())
        {
            if (SeekDetails(arrangementTrunk, ident) is not { } details)
                continue;
            foreach (ElemDetails descendant in details.Children)
            {
                if (descendant.Id != PressHighlightIdent)
                    continue;
                if (assemble(descendant) is { } highlight)
                    btn.AddChild(highlight);
            }
        }
    }

    public static IndicatorBarDriver? Bind(
        ImportedArrangement arrangement,
        IndicatorBarWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(mappings);

        return arrangement.SeekElem(ConnectBtnIdent) is not WidgetBtn connect
            || arrangement.SeekElem(HelpfulBtnIdent) is not WidgetBtn helpful
            || arrangement.SeekElem(HarmfulBtnIdent) is not WidgetBtn harmful
            || arrangement.SeekElem(VitaeBtnIdent) is not WidgetBtn vitae
            || arrangement.SeekElem(BurdenBtnIdent) is not WidgetBtn burden
            || arrangement.SeekElem(MiniPlayBtnIdent) is not WidgetBtn miniPlay
            || arrangement.SeekElem(FinishToonSessBtnIdent) is not WidgetBtn finishSess
            ? null
            : new IndicatorBarDriver(
            mappings, connect, helpful, harmful, vitae, burden, miniPlay, finishSess);
    }

    public void Tick()
    {
        double instant = _bindings.CurrentTime();
        if (instant - _previousConnectRefresh >= ConnectRefreshSecs)
        {
            RefreshConnectPhase(instant, _bindings.LinkStatus());
            _previousConnectRefresh = instant;
        }

        if (_connectPhase == LinkSemanticPhase.Bad
            && instant - _previousConnectFlash >= ConnectFlashSecs)
        {
            _connect.TrySetCanonPhase(
                _connect.EngagedCanonPhaseIdent == ConnectionUncertainPhase
                    ? ConnectionBadPhase
                    : ConnectionUncertainPhase);
            _previousConnectFlash = instant;
        }
    }

    private IndicatorBarDriver(
        IndicatorBarWiring mappings,
        WidgetBtn connect,
        WidgetBtn helpful,
        WidgetBtn harmful,
        WidgetBtn vitae,
        WidgetBtn burden,
        WidgetBtn miniPlay,
        WidgetBtn finishToonSess)
    {
        _bindings = mappings;
        _connect = connect;
        _helpful = helpful;
        _harmful = harmful;
        _vitae = vitae;
        _burden = burden;
        _miniPlay = miniPlay;
        _finishToonSess = finishToonSess;

        _helpful.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.PositiveFxList);
        _harmful.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.NegativeFxList);
        _connect.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.ConnectStatus);
        _vitae.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.Vitae);
        _burden.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.ToonInformation);
        _miniPlay.OnClick = () => mappings.TogglePanel(CanonPaneRegistry.MiniPlay);
        _finishToonSess.OnClick = mappings.RequestEndCharacterSession;

        mappings.Spellbook.EnchantmentsChanged += OnEnchantmentsAltered;
        mappings.Objects.ObjectAdded += OnObjectAltered;
        mappings.Objects.ObjectUpdated += OnObjectAltered;
        mappings.Objects.ObjectRemoved += OnObjectAltered;
        mappings.Objects.ObjectMoved += OnObjectMoved;
        mappings.Objects.ContainerContentsReplaced += OnVesselInsidesReplaced;
        mappings.Objects.Cleared += OnObjectsCleared;

        double instant = mappings.CurrentTime();
        _previousConnectRefresh = instant;
        _previousConnectFlash = instant;
        _connect.TrySetCanonPhase(ConnectionGoodPhase);
        _miniPlay.TrySetCanonPhase(WidgetButtonStateMachine.Ghosted);
        _finishToonSess.TrySetCanonPhase(WidgetButtonStateMachine.Normal);
        RefreshEnchantments();
        RefreshBurden();
    }

    public void AssignMiniPlayEngaged(bool engaged)
    {
        _miniPlay.TrySetCanonPhase(
                engaged ? WidgetButtonStateMachine.Normal : WidgetButtonStateMachine.Ghosted);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _bindings.Spellbook.EnchantmentsChanged -= OnEnchantmentsAltered;
        _bindings.Objects.ObjectAdded -= OnObjectAltered;
        _bindings.Objects.ObjectUpdated -= OnObjectAltered;
        _bindings.Objects.ObjectRemoved -= OnObjectAltered;
        _bindings.Objects.ObjectMoved -= OnObjectMoved;
        _bindings.Objects.ContainerContentsReplaced -= OnVesselInsidesReplaced;
        _bindings.Objects.Cleared -= OnObjectsCleared;
        _connect.OnClick = null;
        _helpful.OnClick = null;
        _harmful.OnClick = null;
        _vitae.OnClick = null;
        _burden.OnClick = null;
        _miniPlay.OnClick = null;
        _finishToonSess.OnClick = null;
    }

    private (WidgetBtn Button, uint Id)[] Indicators()
    {
        return [
        (_connect, ConnectBtnIdent),
        (_helpful, HelpfulBtnIdent),
        (_harmful, HarmfulBtnIdent),
        (_vitae, VitaeBtnIdent),
        (_burden, BurdenBtnIdent),
        (_miniPlay, MiniPlayBtnIdent),
        (_finishToonSess, FinishToonSessBtnIdent),
    ];
    }

    private static ElemDetails? SeekDetails(ElemDetails trunk, uint ident)
    {
        if (trunk.Id == ident)
            return trunk;
        foreach (ElemDetails descendant in trunk.Children)
            if (SeekDetails(descendant, ident) is { } located)
                return located;
        return null;
    }

    private void RefreshConnectPhase(double instant, LinkStatusFrame capture)
    {
        LinkSemanticPhase phase = !capture.Connected || capture.SecondsSinceLastPacket >= 40d
            ? LinkSemanticPhase.Disconnected
            : capture.SecondsSinceLastPacket >= 20d
                ? LinkSemanticPhase.Bad
                : capture.SecondsSinceLastPacket >= 5d
                    ? LinkSemanticPhase.Uncertain
                    : LinkSemanticPhase.Good;
        AssignConnectPhase(phase, instant);
    }

    private void AssignConnectPhase(LinkSemanticPhase phase, double instant)
    {
        if (_connectPhase == phase) return;
        _connectPhase = phase;
        uint visual = phase switch
        {
            LinkSemanticPhase.Good => ConnectionGoodPhase,
            LinkSemanticPhase.Uncertain => ConnectionUncertainPhase,
            LinkSemanticPhase.Bad => ConnectionBadPhase,
            LinkSemanticPhase.Disconnected => ConnectionDisconnectedPhase,
            _ => throw new InvalidOperationException($"Unrecognized link state {phase}."),
        };
        _connect.TrySetCanonPhase(visual);
        if (phase == LinkSemanticPhase.Bad)
            _previousConnectFlash = instant;
    }

    private void RefreshEnchantments()
    {
        _helpful.TrySetCanonPhase(HasMatchingFx(beneficial: true)
            ? WidgetButtonStateMachine.Normal
            : WidgetButtonStateMachine.Ghosted);
        _harmful.TrySetCanonPhase(HasMatchingFx(beneficial: false)
            ? WidgetButtonStateMachine.Normal
            : WidgetButtonStateMachine.Ghosted);

        bool hasVitae = _bindings.Spellbook.EngagedEnchantmentCapture.Any(capture =>
            capture.Bucket is 4u
            && capture.StatModValue is float modifier
            && modifier < 1f);
        _vitae.TrySetCanonPhase(hasVitae
            ? WidgetButtonStateMachine.Normal
            : WidgetButtonStateMachine.Ghosted);
    }

    private bool HasMatchingFx(bool beneficial)
    {
        return _bindings.Spellbook.EngagedEnchantmentCapture.Any(capture =>
                capture.Bucket is 1u or 2u
                && _bindings.Spellbook.TryFetchMetadata(
                    capture.SpellId, out SpellMeta metadata)
                && metadata.IsBeneficial == beneficial);
    }

    private void OnObjectAltered(ClientThing _) => RefreshBurden();
    private void OnObjectMoved(ObjectRelocation _) => RefreshBurden();
    private void OnVesselInsidesReplaced(uint _) => RefreshBurden();
    private void OnObjectsCleared() => RefreshBurden();
    private void OnEnchantmentsAltered()
    {
        RefreshEnchantments();
        RefreshBurden();
    }

    private void RefreshBurden()
    {
        uint avatar = _bindings.PlayerGuid();
        var avatarObject = _bindings.Objects.Get(avatar);
        int strength = _bindings.Strength() ?? 10;
        int augmentation = avatarObject?.Properties.FetchInt(EncumbranceAugProp) ?? 0;
        int cap = BurdenRules.EncumbranceCapacity(strength, augmentation);
        int burden = avatarObject?.Properties.Ints.TryGetValue(
                EncumbranceValueProp, out int wireBurden) == true
            ? wireBurden
            : _bindings.Objects.TotalCarriedBurden(avatar);
        float pull = BurdenRules.PullRatio(cap, burden);

        _burden.TrySetCanonPhase(pull < 1f
            ? UnencumberedPhase
            : pull < 2f ? EncumberedPhase : HeavilyEncumberedPhase);
    }
}

public sealed record IndicatorBarWiring(
    Grimoire Spellbook,
    ClientThingChart Objects,
    Func<uint> PlayerGuid,
    Func<int?> Strength,
    Func<LinkStatusFrame> LinkStatus,
    Func<double> CurrentTime,
    Action<uint> TogglePanel,
    Action RequestEndCharacterSession);
