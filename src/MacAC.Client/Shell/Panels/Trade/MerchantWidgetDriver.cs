using MacAC.Client.Graphics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class MerchantWidgetDriver : IRetainedPaneDriver, IGearListDragHandler
{
    public const uint LayoutId = 0x21000012u;

    public const uint RootId = 0x100000B7u;

    public const uint CloseId = 0x100000D6u;

    public const uint BoardClusterIdent = 0x100000B8u;

    public const uint GearListTabIdent = 0x100000B9u;

    public const uint BuyingTabIdent = 0x100000BAu;

    public const uint SellingTabIdent = 0x100000BBu;

    public const uint GearListSheetIdent = 0x100000BCu;

    public const uint GearRosterIdent = 0x100000BDu;

    public const uint GearScrollerTag = 0x100000BEu;

    public const uint KindSiftMenuIdent = 0x100000BFu;

    public const uint GearLabelPhraseIdent = 0x100000C0u;

    public const uint GearPricePhraseIdent = 0x100000C1u;

    public const uint PurchaseBtnIdent = 0x100000C2u;

    public const uint AppendBtnIdent = 0x100000C3u;

    public const uint BuyingSheetIdent = 0x100000C4u;

    public const uint SellingSheetIdent = 0x100000CDu;

    public const uint BuyingRosterIdent = 0x100000C5u;

    public const uint BuyingScrollerIdent = 0x100000C6u;

    public const uint SellingRosterIdent = 0x100000CEu;

    public const uint SellingScrollerIdent = 0x100000CFu;

    public const uint BuyingRosterPhraseIdent = 0x100000C7u;

    public const uint BuyingPursePhraseIdent = 0x100000C8u;

    public const uint SellingRosterPhraseIdent = 0x100000D0u;

    public const uint SellingPursePhraseIdent = 0x100000D1u;

    public const uint PurchaseGearBtnIdent = 0x100000C9u;

    public const uint PurchaseAllBtnIdent = 0x100000CAu;

    public const uint PurchaseWipeGearBtnIdent = 0x100000CBu;

    public const uint PurchaseWipeRosterBtnIdent = 0x100000CCu;

    public const uint VendGearBtnIdent = 0x100000D2u;

    public const uint VendAllBtnIdent = 0x100000D3u;

    public const uint VendWipeGearBtnIdent = 0x100000D4u;

    public const uint VendWipeRosterBtnIdent = 0x100000D5u;

    private const int KindMenuRanksPerColumn = 6;

    private const float KindMenuRankHeight = 18f;

    private const float KindMenuColumnWidth = 100f;

    private const uint KindMenuGearNormSprite = 0x060012B3u;

    private const uint KindMenuGearHighlightSprite = 0x060012B4u;

    private const uint KindMenuNormSprite = 0x060012B3u;

    private const uint KindMenuPressedSprite = 0x060012B4u;

    private const uint KindMenuArrowCapClosedSprite = 0x060012B1u;

    private const uint KindMenuArrowCapOpenSprite = 0x060012B2u;

    private const float KindMenuScrollerWidth = 16f;

    private const float KindMenuRollBtnReach = 16f;

    private const uint KindMenuRollFollowSprite = 0x06004C5Fu;

    private const uint KindMenuRollThumbTopSprite = 0x06004C60u;

    private const uint KindMenuRollThumbSprite = 0x06004C63u;

    private const uint KindMenuRollThumbBottomSprite = 0x06004C66u;

    private const uint KindMenuRollUpSprite = CanonScrollbarChrome.UpNorm;

    private const uint KindMenuRollDownSprite = CanonScrollbarChrome.DownNorm;

    private static readonly (string Label, GearKind Mask)[] BucketFilters =
    [
        ("Armor", GearKind.Armor),                                                       // 0x2
        ("Books, Paper", GearKind.Writable),                                              // 0x2000
        ("Clothing", GearKind.Clothing),                                                  // 0x4
        ("Containers", GearKind.Container),                                               // 0x200
        ("Food", GearKind.Food),                                                          // 0x20
        ("Gems", GearKind.Gem),                                                           // 0x800
        ("Jewelry", GearKind.Jewelry),                                                    // 0x8
        ("Keys, Tools", GearKind.TinkeringTool | GearKind.Key),
        ("Miscellaneous", GearKind.Useless | GearKind.Misc | GearKind.Creature),          // 0x490
        ("Services", GearKind.Service),
        ("Spell Components", GearKind.SpellComponents),                                   // 0x1000
        ("Trade Notes", GearKind.PromissoryNote),
        ("Weapons", GearKind.Weapon),                                                     // 0x101
        ("Mana Stones", GearKind.ManaStone),
        ("Magic Items", GearKind.Caster),                                                 // 0x8000
        ("Alchemical Items", GearKind.CraftAlchemyIntermediate | GearKind.CraftAlchemyBase),
        ("Cooking Items", GearKind.CraftCookingBase),
        ("Fletching Items", GearKind.CraftFletchingIntermediate | GearKind.CraftFletchingBase),
    ];

    private readonly MerchantPhase _merchant;

    private readonly CanonWindowHandle _window;

    private readonly Func<GearKind, uint, uint, uint, uint, uint> _locateGlyph;

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly GearDealingDriver _gearDealing;

    private readonly PickPhase _pick;

    private readonly StackSplitGauge _divideQty;

    private readonly WidgetElem _gearListSheet;

    private readonly WidgetElem _buyingSheet;

    private readonly WidgetElem _sellingSheet;

    private readonly WidgetElem _gearListTab;

    private readonly WidgetElem _buyingTab;

    private readonly WidgetElem _sellingTab;

    private readonly WidgetGearRoster _gearRoster;

    private readonly WidgetGearRoster? _buyingRoster;

    private readonly WidgetGearRoster? _sellingRoster;

    private readonly WidgetMenu _kindMenu;

    private readonly WidgetPhrase _gearLabelPhrase;

    private readonly WidgetPhrase _gearPricePhrase;

    private readonly WidgetPhrase? _purchaseRosterPhrase;

    private readonly WidgetPhrase? _purchasePursePhrase;

    private readonly WidgetPhrase? _vendRosterPhrase;

    private readonly WidgetPhrase? _vendPursePhrase;

    private readonly WidgetBtn? _shut;

    private readonly WidgetBtn? _purchaseBtn;

    private readonly WidgetBtn? _appendBtn;

    private readonly WidgetBtn? _purchaseGearBtn;

    private readonly WidgetBtn? _purchaseAllBtn;

    private readonly WidgetBtn? _purchaseWipeGearBtn;

    private readonly WidgetBtn? _purchaseWipeRosterBtn;

    private readonly WidgetBtn? _vendGearBtn;

    private readonly WidgetBtn? _vendAllBtn;

    private readonly WidgetBtn? _vendWipeGearBtn;

    private readonly WidgetBtn? _vendWipeRosterBtn;

    private readonly VendorTray _purchaseLoading = new();

    private readonly VendorTray _vendLoading = new();

    private readonly CanonPromptMint? _popups;

    private readonly Action<string>? _sysMsg;

    private readonly List<(string Label, GearKind Mask)> _presentBuckets = [];

    private int _chosenBucketOrdinal = -1;

    private bool _purchaseTurnedOnByPick;

    private uint _shutConfirmCtx;

    private int _previousAlternateCurrencyPurchase;

    private bool _alternateCurrencySatchelObserved;

    private PendingMerchantSplit? _queuedMerchantDivide;

    private readonly PullOverGlobalMomentDrain _pullOverDrain;

    private bool _destroyed;

    private readonly record struct PendingMerchantSplit(
        uint SourceGuid,
        uint WeenieClassId,
        int Quantity);

    private MerchantWidgetDriver(
        MerchantPhase merchant,
        CanonWindowHandle pane,
        Func<GearKind, uint, uint, uint, uint, uint> locateGlyph,
        ClientThingChart objects,
        Func<uint> avatarOid,
        GearDealingDriver gearDealing,
        PickPhase pick,
        StackSplitGauge divideQty,
        WidgetElem gearListSheet,
        WidgetElem buyingSheet,
        WidgetElem sellingSheet,
        WidgetElem gearListTab,
        WidgetElem buyingTab,
        WidgetElem sellingTab,
        WidgetGearRoster gearRoster,
        WidgetScroller? gearScroller,
        WidgetGearRoster? buyingRoster,
        WidgetScroller? buyingScroller,
        WidgetGearRoster? sellingRoster,
        WidgetScroller? sellingScroller,
        WidgetMenu kindMenu,
        WidgetPhrase gearLabelPhrase,
        WidgetPhrase gearPricePhrase,
        WidgetPhrase? purchaseRosterPhrase,
        WidgetPhrase? purchasePursePhrase,
        WidgetPhrase? vendRosterPhrase,
        WidgetPhrase? vendPursePhrase,
        WidgetBtn? shut,
        WidgetBtn? purchaseBtn,
        WidgetBtn? appendBtn,
        WidgetBtn? purchaseGearBtn,
        WidgetBtn? purchaseAllBtn,
        WidgetBtn? purchaseWipeGearBtn,
        WidgetBtn? purchaseWipeRosterBtn,
        WidgetBtn? vendGearBtn,
        WidgetBtn? vendAllBtn,
        WidgetBtn? vendWipeGearBtn,
        WidgetBtn? vendWipeRosterBtn,
        CanonPromptMint? popups,
        Action<string>? sysMsg,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        uint vacantSocketSprite,
        uint buyingVacantSocketSprite,
        uint sellingVacantSocketSprite)
    {
        _merchant = merchant;
        _window = pane;
        _locateGlyph = locateGlyph;
        _objects = objects;
        _avatarOid = avatarOid;
        _gearDealing = gearDealing;
        _pick = pick;
        _divideQty = divideQty;
        _gearListSheet = gearListSheet;
        _buyingSheet = buyingSheet;
        _sellingSheet = sellingSheet;
        _gearListTab = gearListTab;
        _buyingTab = buyingTab;
        _sellingTab = sellingTab;
        _gearRoster = gearRoster;
        _buyingRoster = buyingRoster;
        _sellingRoster = sellingRoster;
        _kindMenu = kindMenu;
        _gearLabelPhrase = gearLabelPhrase;
        _gearPricePhrase = gearPricePhrase;
        _purchaseRosterPhrase = purchaseRosterPhrase;
        _purchasePursePhrase = purchasePursePhrase;
        _vendRosterPhrase = vendRosterPhrase;
        _vendPursePhrase = vendPursePhrase;
        _shut = shut;
        _purchaseBtn = purchaseBtn;
        _appendBtn = appendBtn;
        _purchaseGearBtn = purchaseGearBtn;
        _purchaseAllBtn = purchaseAllBtn;
        _purchaseWipeGearBtn = purchaseWipeGearBtn;
        _purchaseWipeRosterBtn = purchaseWipeRosterBtn;
        _vendGearBtn = vendGearBtn;
        _vendAllBtn = vendAllBtn;
        _vendWipeGearBtn = vendWipeGearBtn;
        _vendWipeRosterBtn = vendWipeRosterBtn;
        _popups = popups;
        _sysMsg = sysMsg;

        _gearRoster.Columns = 1;
        _gearRoster.SingleRank = true;
        _gearRoster.HorizontalRoll = true;
        _gearRoster.ChamberWidth = 32f;
        _gearRoster.ChamberHeight = 32f;
        _gearRoster.PopulateShownVacantSockets = true;
        if (vacantSocketSprite is not 0u)
            _gearRoster.ChamberVacantSprite = vacantSocketSprite;
        _gearRoster.VacantSocketMaker = () => new WidgetGearSlot
        {
            SpriteResolve = _gearRoster.SpriteResolve,
            AllowPullSrc = false,
        };
        _gearRoster.ExamineGearAsked = StudyGear;
        _gearRoster.PrimaryGearPressed = PressMerchantGear;
        if (gearScroller is not null)
        {
            gearScroller.Model = _gearRoster.Scroll;
            gearScroller.Horizontal = true;
        }

        ConfigureVacantStrip(_buyingRoster, buyingVacantSocketSprite);
        if (buyingScroller is not null && _buyingRoster is not null)
        {
            buyingScroller.Model = _buyingRoster.Scroll;
            buyingScroller.Horizontal = true;
        }
        ConfigureVacantStrip(_sellingRoster, sellingVacantSocketSprite);
        if (sellingScroller is not null && _sellingRoster is not null)
        {
            sellingScroller.Model = _sellingRoster.Scroll;
            sellingScroller.Horizontal = true;
        }
        _sellingRoster?.EnrollPullHandler(this);
        if (_buyingRoster is not null)
        {
            _buyingRoster.PrimaryGearPressed = PressMerchantGear;
            _buyingRoster.ExamineGearAsked = StudyGear;
        }
        if (_sellingRoster is not null)
        {
            _sellingRoster.PrimaryGearPressed = PressMerchantGear;
            _sellingRoster.ExamineGearAsked = StudyGear;
        }

        _pullOverDrain = new PullOverGlobalMomentDrain(SamplePullOver);
        _window.SubstanceTrunk.AddChild(_pullOverDrain);

        _kindMenu.SpriteResolve = locateSprite;
        _kindMenu.DatFont = datTypeface;
        _kindMenu.Font = diagTypeface;
        _kindMenu.NormSprite = KindMenuNormSprite;
        _kindMenu.PressedSprite = KindMenuPressedSprite;
        _kindMenu.GearNormSprite = KindMenuGearNormSprite;
        _kindMenu.GearHighlightSprite = KindMenuGearHighlightSprite;
        _kindMenu.RowsPerColumn = KindMenuRanksPerColumn;
        _kindMenu.RowHeight = KindMenuRankHeight;
        _kindMenu.ColumnWidth = KindMenuColumnWidth;
        _kindMenu.Scrollable = true;
        _kindMenu.PopupDimsToSubstance = true;
        _kindMenu.PopupScrollerConcealWhenDisabled = true;
        _kindMenu.ScrollbarWidth = KindMenuScrollerWidth;
        _kindMenu.RollButtonExtent = KindMenuRollBtnReach;
        _kindMenu.RollFollowSprite = KindMenuRollFollowSprite;
        _kindMenu.RollThumbTopSprite = KindMenuRollThumbTopSprite;
        _kindMenu.RollThumbSprite = KindMenuRollThumbSprite;
        _kindMenu.RollThumbBottomSprite = KindMenuRollThumbBottomSprite;
        _kindMenu.RollUpSprite = KindMenuRollUpSprite;
        _kindMenu.RollDownSprite = KindMenuRollDownSprite;
        _kindMenu.ArrowCapClosedSprite = KindMenuArrowCapClosedSprite;
        _kindMenu.ArrowCapOpenSprite = KindMenuArrowCapOpenSprite;
        _kindMenu.OpenUpward = false;
        _kindMenu.PhraseIndent = 0f;
        _kindMenu.BtnPhraseIndent = 0f;
        _kindMenu.OnSelect = cargo =>
        {
            if (cargo is uint bitmask) PickBucket(bitmask);
        };
        _kindMenu.BtnCaptionSupplier = () =>
            _chosenBucketOrdinal >= 0 && _chosenBucketOrdinal < _presentBuckets.Count
                ? _presentBuckets[_chosenBucketOrdinal].Label
                : string.Empty;

        CanonTabWiring.AssignPress(_gearListTab, () => RevealTab(MerchantPaneTab.Items));
        CanonTabWiring.AssignPress(_buyingTab, () => RevealTab(MerchantPaneTab.Buying));
        CanonTabWiring.AssignPress(_sellingTab, () => RevealTab(MerchantPaneTab.Selling));
        _shut?.OnClick = ShutBtnPressed;
        _purchaseBtn?.OnClick = PurchaseChosenGear;
        _appendBtn?.OnClick = AppendChosenToPurchaseRoster;
        _purchaseGearBtn?.OnClick = PurchaseGearBtnPressed;
        _purchaseAllBtn?.OnClick = PurchaseAllBtnPressed;
        _purchaseWipeGearBtn?.OnClick = PurchaseWipeGearBtnPressed;
        _purchaseWipeRosterBtn?.OnClick = () => _purchaseLoading.Clear();
        _vendGearBtn?.OnClick = VendGearBtnPressed;
        _vendAllBtn?.OnClick = VendAllBtnPressed;
        _vendWipeGearBtn?.OnClick = VendWipeGearBtnPressed;
        _vendWipeRosterBtn?.OnClick = () => _vendLoading.Clear();

        _purchaseLoading.Changed += ReassembleBuyingRoster;
        _purchaseLoading.Changed += RenewGearListTabReadiness;
        _vendLoading.Changed += ReassembleSellingRoster;
        _purchaseLoading.Changed += RefreshPurchaseTransactionPhrase;
        _vendLoading.Changed += RefreshVendTransactionPhrase;
        _objects.ObjectAdded += OnObjectAdded;
        _objects.ObjectUpdated += OnObjectMoneyAltered;
        _objects.StackSizeUpdated += OnPileDimsUpdated;
        _objects.ObjectMoved += OnObjectMoved;

        RevealTab(MerchantPaneTab.Items);
        WipeSubstance();

        _merchant.Changed += OnMerchantAltered;
        _pick.Changed += OnPickChangeover;
        _objects.ObjectRemoved += OnObjectRemoved;
        _gearDealing.CoreTransactions.Inventory.RequestFailed += OnSatchelReqFailed;
        _gearDealing.StateChanged += OnDealingPhaseAltered;
        _divideQty.Changed += OnDivideQtyAltered;
    }

    public static MerchantWidgetDriver? Bind(
        ImportedArrangement arrangement,
        MerchantPhase merchant,
        CanonWindowHandle pane,
        Func<GearKind, uint, uint, uint, uint, uint> locateGlyph,
        ClientThingChart objects,
        Func<uint> avatarOid,
        GearDealingDriver gearDealing,
        PickPhase pick,
        StackSplitGauge divideQty,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        uint vacantSocketSprite = 0u,
        uint buyingVacantSocketSprite = 0u,
        uint sellingVacantSocketSprite = 0u,
        CanonPromptMint? popups = null,
        Action<string>? sysMsg = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(merchant);
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(locateGlyph);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(avatarOid);
        ArgumentNullException.ThrowIfNull(gearDealing);
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(divideQty);
        ArgumentNullException.ThrowIfNull(locateSprite);

        if (arrangement.SeekElem(GearListSheetIdent) is not { } gearListSheet
            || arrangement.SeekElem(BuyingSheetIdent) is not { } buyingSheet
            || arrangement.SeekElem(SellingSheetIdent) is not { } sellingSheet
            || arrangement.SeekElem(GearListTabIdent) is not { } gearListTab
            || arrangement.SeekElem(BuyingTabIdent) is not { } buyingTab
            || arrangement.SeekElem(SellingTabIdent) is not { } sellingTab
            || arrangement.SeekElem(GearRosterIdent) is not WidgetGearRoster gearRoster
            || arrangement.SeekElem(KindSiftMenuIdent) is not WidgetMenu kindMenu
            || arrangement.SeekElem(GearLabelPhraseIdent) is not WidgetPhrase gearLabelPhrase
            || arrangement.SeekElem(GearPricePhraseIdent) is not WidgetPhrase gearPricePhrase)

            return null;

        WidgetPhrase? purchaseRosterPhrase = arrangement.SeekElem(BuyingRosterPhraseIdent) as WidgetPhrase;
        WidgetPhrase? purchasePursePhrase = arrangement.SeekElem(BuyingPursePhraseIdent) as WidgetPhrase;
        WidgetPhrase? vendRosterPhrase = arrangement.SeekElem(SellingRosterPhraseIdent) as WidgetPhrase;
        WidgetPhrase? vendPursePhrase = arrangement.SeekElem(SellingPursePhraseIdent) as WidgetPhrase;

        WidgetBtn? shut = arrangement.SeekElem(CloseId) as WidgetBtn;
        WidgetScroller? gearScroller = arrangement.SeekElem(GearScrollerTag) as WidgetScroller;
        WidgetBtn? purchaseBtn = arrangement.SeekElem(PurchaseBtnIdent) as WidgetBtn;
        WidgetBtn? appendBtn = arrangement.SeekElem(AppendBtnIdent) as WidgetBtn;
        WidgetGearRoster? buyingRoster = arrangement.SeekElem(BuyingRosterIdent) as WidgetGearRoster;
        WidgetScroller? buyingScroller = arrangement.SeekElem(BuyingScrollerIdent) as WidgetScroller;
        WidgetGearRoster? sellingRoster = arrangement.SeekElem(SellingRosterIdent) as WidgetGearRoster;
        WidgetScroller? sellingScroller = arrangement.SeekElem(SellingScrollerIdent) as WidgetScroller;
        WidgetBtn? purchaseGearBtn = arrangement.SeekElem(PurchaseGearBtnIdent) as WidgetBtn;
        WidgetBtn? purchaseAllBtn = arrangement.SeekElem(PurchaseAllBtnIdent) as WidgetBtn;
        WidgetBtn? purchaseWipeGearBtn = arrangement.SeekElem(PurchaseWipeGearBtnIdent) as WidgetBtn;
        WidgetBtn? purchaseWipeRosterBtn = arrangement.SeekElem(PurchaseWipeRosterBtnIdent) as WidgetBtn;
        WidgetBtn? vendGearBtn = arrangement.SeekElem(VendGearBtnIdent) as WidgetBtn;
        WidgetBtn? vendAllBtn = arrangement.SeekElem(VendAllBtnIdent) as WidgetBtn;
        WidgetBtn? vendWipeGearBtn = arrangement.SeekElem(VendWipeGearBtnIdent) as WidgetBtn;
        WidgetBtn? vendWipeRosterBtn = arrangement.SeekElem(VendWipeRosterBtnIdent) as WidgetBtn;

        return new MerchantWidgetDriver(
            merchant,
            pane,
            locateGlyph,
            objects,
            avatarOid,
            gearDealing,
            pick,
            divideQty,
            gearListSheet,
            buyingSheet,
            sellingSheet,
            gearListTab,
            buyingTab,
            sellingTab,
            gearRoster,
            gearScroller,
            buyingRoster,
            buyingScroller,
            sellingRoster,
            sellingScroller,
            kindMenu,
            gearLabelPhrase,
            gearPricePhrase,
            purchaseRosterPhrase,
            purchasePursePhrase,
            vendRosterPhrase,
            vendPursePhrase,
            shut,
            purchaseBtn,
            appendBtn,
            purchaseGearBtn,
            purchaseAllBtn,
            purchaseWipeGearBtn,
            purchaseWipeRosterBtn,
            vendGearBtn,
            vendAllBtn,
            vendWipeGearBtn,
            vendWipeRosterBtn,
            popups,
            sysMsg,
            datTypeface,
            diagTypeface,
            locateSprite,
            vacantSocketSprite,
            buyingVacantSocketSprite,
            sellingVacantSocketSprite);
    }

    private enum MerchantPaneTab { Items, Buying, Selling }

    private sealed class PullOverGlobalMomentDrain(Action onGlobalWidgetMoment) : WidgetElem, IWidgetGlobalTimeListener
    {
        private readonly Action _onGlobalWidgetMoment = onGlobalWidgetMoment;

        public void OnGlobalWidgetMoment(double instantSecs) => _onGlobalWidgetMoment();
    }

    private const string NotEnoughMoneyMsg = "You don't have enough money";

    private const string NotEnoughHallMsg = "You must empty some slots in your backpack first";

    private const string CannotVendPartialPileMsg = "Cannot sell part of a stack";

    private const string ShutAckMsg =
        "You have not completed all transactions. Are you sure you want to leave this vendor?";
}
