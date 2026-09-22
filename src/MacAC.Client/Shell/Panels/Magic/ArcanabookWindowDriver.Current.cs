using System.Globalization;
using MacAC.Assets;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ArcanabookWindowDriver
{
    public ArcanabookWindowPage LatestSheet { get; private set; }

    public void RevealSheet(ArcanabookWindowPage sheet)
    {
        LatestSheet = sheet;
        _arcanumSheet.Visible = sheet == ArcanabookWindowPage.Spells;
        _moduleSheet.Visible = sheet == ArcanabookWindowPage.Components;
        CanonTabWiring.ApplyOpen(_arcanumTab, sheet == ArcanabookWindowPage.Spells);
        CanonTabWiring.ApplyOpen(_moduleTab, sheet == ArcanabookWindowPage.Components);
    }

    public void Tick()
    {
        if (_arcanaStale)
        {
            _arcanaStale = false;
            ReassembleArcana();
        }
        if (_modulesStale && !_moduleEditEngaged)
        {
            _modulesStale = false;
            ReassembleModules();
        }
    }

    public void OnShown() => ReassembleAll();

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _grimoire.SpellbookChanged -= OnGrimoireAltered;
        _grimoire.DesiredComponentsChanged -= OnWantedModulesAltered;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.ObjectRemoved -= OnObjectRemoved;
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.ContainerContentsReplaced -= OnVesselInsidesReplaced;
        _pick.Changed -= OnPickAltered;
        CanonTabWiring.AssignPress(_arcanumTab, null);
        CanonTabWiring.AssignPress(_moduleTab, null);
        _shutBtn.OnClick = null;
        _eraseBtn.OnClick = null;
        foreach ((WidgetBtn btn, _) in _filters) btn.OnClick = null;
    }

    private void ConfigureArcanumRoster(ImportedArrangement arrangement)
    {
        _arcanumRoster.ExamineRegistryListingAsked = _examineArcanum;
        _arcanumRoster.Columns = 1;
        _arcanumRoster.ChamberWidth = Math.Max(1f, _arcanumRoster.Width);
        _arcanumRoster.ChamberHeight = _rankStyling.Height;
        if (arrangement.SeekElem(ArcanumScrollerIdent) is WidgetScroller scroller)
            scroller.Model = _arcanumRoster.Scroll;
    }

    private void ConfigureModuleRoster(ImportedArrangement arrangement)
    {
        _moduleRoster.Columns = 1;
        _moduleRoster.ChamberWidth = Math.Max(1f, _moduleRoster.Width);
        _moduleRoster.ChamberHeight = 32f;
        if (arrangement.SeekElem(ModuleScrollerIdent) is WidgetScroller scroller)
            scroller.Model = _moduleRoster.Scroll;
    }

    private void FlipSift(uint bitmask)
    {
        uint filters = _grimoire.GrimoireFilters ^ bitmask;
        _transmitSift(filters);
        _arcanumRoster.Scroll.AssignRollY(0);
    }

    private void ReassembleAll()
    {
        ReassembleArcana();
        ReassembleModules();
        _arcanaStale = false;
        _modulesStale = false;
    }

    private void ReassembleArcana()
    {
        uint net = _grimoire.GrimoireFilters;
        foreach ((WidgetBtn btn, uint bitmask) in _filters)
            btn.Selected = (net & bitmask) != 0;

        SpellMeta[] arcana = [.. _grimoire.LearnedArcana
            .Select(ident => _grimoire.TryFetchMetadata(ident, out SpellMeta metadata) ? metadata : null)
            .Where(metadata => metadata is not null && IsShown(metadata, net))
            .OrderBy(metadata => metadata!.SortKey)
            .Cast<SpellMeta>()];

        using (_arcanumRoster.DeferArrangement())
        {
            _arcanumRoster.Flush();
            foreach (SpellMeta metadata in arcana)
            {
                uint arcanumIdent = metadata.SpellId;
                WidgetRegistrySlot socket = new WidgetRegistrySlot
                {
                    ListingTag = arcanumIdent,
                    RegistryGlyphTexture = _locateArcanumGlyph(arcanumIdent),
                    Label = metadata.Name,
                    UnhideCaption = true,
                    BackgroundSprite = _rankStyling.BackgroundSprite,
                    ChosenSprite = _rankStyling.SelectedSprite,
                    PickBehindSubstance = true,
                    LabelFont = _rankTypeface,
                    LabelColor = _rankStyling.LabelColor,
                    GlyphLeft = _rankStyling.IconLeft,
                    GlyphTop = _rankStyling.IconTop,
                    GlyphWidth = _rankStyling.IconWidth,
                    GlyphHeight = _rankStyling.IconHeight,
                    CaptionLeft = _rankStyling.LabelLeft,
                    CaptionWidth = _rankStyling.LabelWidth,
                    SpriteResolve = _arcanumRoster.SpriteResolve,
                    RegistryPullCargo = new ArcanabookShortcutDragPayload(arcanumIdent),
                    PullBegan = _ => PickArcanum(arcanumIdent),
                    Clicked = () => PickArcanum(arcanumIdent),
                    DoubleClicked = () => { PickArcanum(arcanumIdent); _appendFavorite(arcanumIdent); }
                };
                _arcanumRoster.AddItem(socket);
            }
        }
        SynchronizeArcanumPick();
    }

    private void ReassembleModules()
    {
        uint avatar = _avatarOid();
        var bundles = _objects.Objects
            .Where(gear => (gear.IsModulePack || _modules.ContainsKey(gear.WeenieClassIdent))
                && IsPossessedByAvatar(gear, avatar))
            .GroupBy(gear => gear.WeenieClassIdent)
            .Select(cluster => new
            {
                ComponentId = cluster.Key,
                First = cluster.First(),
                Quantity = cluster.Sum(gear => Math.Max(1, gear.StackSize)),
            })
            .OrderBy(cluster => _modules.TryGetValue(cluster.ComponentId, out var descriptor) ? descriptor.Category : uint.MaxValue)
            .ThenBy(cluster => cluster.First.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        float width = Math.Max(96f, _moduleRoster.Width);
        _moduleRoster.ChamberWidth = width;
        using (_moduleRoster.DeferArrangement())
        {
            _moduleRoster.Flush();
            uint? bucket = null;
            foreach (var bundleCluster in bundles)
            {
                var bundle = bundleCluster.First;
                uint moduleIdent = bundleCluster.ComponentId;
                _modules.TryGetValue(moduleIdent, out SpellComponentCard? descriptor);
                uint upcomingBucket = descriptor?.Category ?? 8u;
                if (bucket != upcomingBucket)
                {
                    bucket = upcomingBucket;
                    var bucketRank =
                        _moduleBlueprints.BuildBucketRank(upcomingBucket);
                    bucketRank.Clicked = () =>
                    {
                        _chosenModule = null;
                        SynchronizeModulePick();
                        _pickObject(0u);
                    };
                    _moduleRoster.AddItem(bucketRank);
                }
                uint wanted = _grimoire.DesiredComponents.TryGetValue(moduleIdent, out uint quantity) ? quantity : 0u;
                string moduleLabel = string.IsNullOrWhiteSpace(bundle.Name)
                    ? descriptor?.Name ?? $"Component {moduleIdent}"
                    : bundle.Name;
                var rank =
                    _moduleBlueprints.BuildModuleRank(
                        moduleIdent,
                        _locateModuleGlyph(
                            bundle.IconId is not 0 ? bundle.IconId : descriptor?.IconId ?? 0u),
                        moduleLabel,
                        bundleCluster.Quantity,
                        wanted);
                WidgetField wantedField = rank.DesiredField;
                wantedField.OnFocusGained = () => _moduleEditEngaged = true;
                uint committed = wanted;
                void Seal(string val)
                {
                    if (!uint.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out uint decoded)
                        || decoded > 5000u)
                    {
                        wantedField.AssignPhrase(committed.ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                    if (decoded == committed) return;
                    committed = decoded;
                    _setWantedModule(moduleIdent, decoded);
                }
                wantedField.OnSubmit = Seal;
                wantedField.OnFocusLost = val =>
                {
                    Seal(val);
                    _moduleEditEngaged = false;
                };
                rank.Slot.Clicked = () =>
                {
                    _chosenModule = moduleIdent;
                    SynchronizeModulePick();
                    _pickObject(bundle.ObjectId);
                };
                _moduleRoster.AddItem(rank.Slot);
            }
        }
        SynchronizeModulePickFromRealm();
    }

    private bool IsPossessedByAvatar(ClientThing gear, uint avatarOid)
    {
        if (avatarOid is 0) return gear.VesselTag is not 0 || gear.WielderIdent is not 0;
        ClientThing latest = gear;
        for (int zDepth = 0; zDepth < 16; ++zDepth)
        {
            if (latest.WielderIdent == avatarOid || latest.VesselTag == avatarOid) return true;
            if (latest.VesselTag is 0 || _objects.Get(latest.VesselTag) is not { } ancestor) return false;
            latest = ancestor;
        }
        return false;
    }

    private bool IsShown(SpellMeta metadata, uint filters)
    {
        uint school = metadata.SchoolIdent switch
        {
            MechMagicSchool.CreatureEnchantment => 0x0001u,
            MechMagicSchool.ItemEnchantment => 0x0002u,
            MechMagicSchool.LifeMagic => 0x0004u,
            MechMagicSchool.WarMagic => 0x0008u,
            MechMagicSchool.VoidMagic => 0x2000u,
            _ => 0u,
        };
        int arcanumTier = _arcanumTier(metadata.SpellId);
        if (arcanumTier is < 1 or > 8) return false;
        uint tier = 0x10u << (arcanumTier - 1);
        return school is not 0 && (filters & school) is not 0 && (filters & tier) is not 0;
    }

    private void PickArcanum(uint arcanumIdent)
    {
        _chosenArcanum = arcanumIdent;
        SynchronizeArcanumPick();
        for (int idx = 0; idx < _arcanumRoster.FetchCountWIDGETGearList(); ++idx)
            if (_arcanumRoster.GetItem(idx) is WidgetRegistrySlot socket && socket.ListingTag == arcanumIdent)
            {
                _arcanumRoster.RollGearIntoLens(idx);
                break;
            }
    }

    private void ReqEraseChosen()
    {
        if (_chosenArcanum is not uint arcanumIdent
            || !_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata))
            return;

        string msg = $"Are you sure you want to remove {metadata.Name} from your spellbook? "
            + "You will no longer be able to cast this spell unless you learn it again!";
        _unhideAck(msg, approved =>
        {
            if (approved) _dropArcanum(arcanumIdent);
        });
    }

    private void SynchronizeArcanumPick()
    {
        for (int idx = 0; idx < _arcanumRoster.FetchCountWIDGETGearList(); ++idx)
            if (_arcanumRoster.GetItem(idx) is WidgetRegistrySlot socket)
                socket.Selected = socket.ListingTag == _chosenArcanum;
    }

    private void SynchronizeModulePick()
    {
        for (int idx = 0; idx < _moduleRoster.FetchCountWIDGETGearList(); ++idx)
            if (_moduleRoster.GetItem(idx) is WidgetTemplateListSlot socket)
                socket.AssignChosen(socket.ListingIdent == _chosenModule);
    }

    private void SynchronizeModulePickFromRealm()
    {
        _chosenModule = _pick.ChosenObjectTag is uint chosen
            && _objects.Get(chosen) is { } gear
            && IsModule(gear)
                ? gear.WeenieClassIdent
                : null;
        SynchronizeModulePick();
    }

    private void OnGrimoireAltered() => _arcanaStale = true;

    private void OnWantedModulesAltered() => _modulesStale = true;

    private void OnObjectAltered(ClientThing gear)
    {
        if (IsModule(gear)) _modulesStale = true;
    }

    private void OnObjectRemoved(ClientThing gear)
    {
        if (IsModule(gear)) _modulesStale = true;
    }

    private void OnObjectMoved(ObjectRelocation _) => _modulesStale = true;

    private void OnVesselInsidesReplaced(uint _) => _modulesStale = true;

    private void OnPickAltered(PickShift _) => SynchronizeModulePickFromRealm();

    private bool IsModule(ClientThing gear)
        => gear.IsModulePack || _modules.ContainsKey(gear.WeenieClassIdent);
}
