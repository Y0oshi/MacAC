using System.Globalization;
using System.Text;
using MacAC.Client.Arcana;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed partial class AssayWidgetDriver
{
    public AssayView EngagedLens { get; private set; }

    public uint LatestObjectIdent => _dealing.LatestAppraisalIdent;

    public static AssayWidgetDriver? Bind(
        ImportedArrangement arrangement,
        ClientThingChart objects,
        GearDealingDriver dealing,
        PickPhase pick,
        FightingPhase fighting,
        Grimoire grimoire,
        Func<string> avatarLabel,
        Action<uint, string> transmitSetInscription,
        Action<string> sysMsg,
        Action unhide,
        Action shut,
        CreatureAssayRowTemplateMint? beastRankBlueprints = null,
        CreatureDisplayNamePicker? beastLabels = null,
        CanonAssayNamePicker? gearLabels = null,
        Func<uint, uint>? locateArcanumGlyph = null,
        Func<uint, uint>? locateModuleGlyph = null,
        Func<uint, IReadOnlyList<ArcanaExamineComponent>>? arcanumModules = null,
        Func<MechMagicSchool, uint>? magicAptitude = null,
        ArcanaExamineComponentTemplateMint? arcanumModuleBlueprints = null,
        Func<uint, string?>? locateToonBanner = null,
        Func<int>? ownFactionBitset = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(dealing);
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(fighting);
        ArgumentNullException.ThrowIfNull(grimoire);
        ArgumentNullException.ThrowIfNull(avatarLabel);
        ArgumentNullException.ThrowIfNull(transmitSetInscription);
        ArgumentNullException.ThrowIfNull(sysMsg);
        ArgumentNullException.ThrowIfNull(unhide);
        ArgumentNullException.ThrowIfNull(shut);

        return arrangement.SeekElem(GearBoardIdent) is not { } gearBoard
            || arrangement.SeekElem(BeastBoardIdent) is not { } beastBoard
            || arrangement.SeekElem(ArcanumBoardIdent) is not { } arcanumBoard
            || arrangement.SeekElem(BannerIdent) is not WidgetPhrase banner
            || arrangement.SeekElem(GearPhraseIdent) is not WidgetPhrase gearPhrase
            || arrangement.SeekElem(ArcanumSchoolPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(ArcanumManaPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(ArcanumIntervalPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(ArcanumSpanPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(ArcanumReadoutPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(ArcanumGlyphIdent) is null
            || arrangement.SeekElem(ArcanumEquationRosterIdent) is null
            ? null
            : new AssayWidgetDriver(
            arrangement,
            objects,
            dealing,
            pick,
            fighting,
            grimoire,
            avatarLabel,
            transmitSetInscription,
            sysMsg,
            unhide,
            shut,
            gearBoard,
            beastBoard,
            arcanumBoard,
            banner,
            gearPhrase,
            beastRankBlueprints,
            beastLabels,
            gearLabels,
            locateArcanumGlyph,
            locateModuleGlyph,
            arcanumModules,
            magicAptitude,
            arcanumModuleBlueprints,
            locateToonBanner,
            ownFactionBitset);
    }

    public bool StudyArcanum(uint arcanumIdent)
    {
        if (arcanumIdent is 0u
            || !_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata))
            return false;

        _dealing.AbortObjectAppraisalForArcanum();
        _arcanumIdent = arcanumIdent;
        _bannerVal = metadata.Name;
        AssignArcanumPhrase(_arcanumSchool, $"School: {metadata.School}");

        string mana = metadata.ManaCost > 0
            ? metadata.ManaCost.ToString(CultureInfo.InvariantCulture)
            : "???";
        if (metadata.ManaModifier > 0u)
            mana += $" + {metadata.ManaModifier.ToString(CultureInfo.InvariantCulture)} per target";
        AssignArcanumPhrase(_arcanumMana, $"Mana: {mana}");

        string interval = string.Empty;
        if (metadata.Duration is > 0f and not -1f)
        {
            uint displayed = metadata.Duration >= 60f
                ? (uint)(metadata.Duration / 60f)
                : (uint)metadata.Duration;
            interval = metadata.Duration >= 60f
                ? $"Duration: {displayed.ToString(CultureInfo.InvariantCulture)} min."
                : $"Duration: {displayed.ToString(CultureInfo.InvariantCulture)} sec.";
        }
        AssignArcanumPhrase(_arcanumInterval, interval);

        float span = MathF.Min(
            metadata.BaseRangeConstant
            + metadata.BaseRangeModifier * _magicAptitude(metadata.SchoolIdent),
            75f);
        AssignArcanumPhrase(
            _arcanumSpan,
            span > 0f
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Range: {0:F1} yds.",
                    span / 0.9144f)
                : string.Empty);

        var modules =
            _arcanumModules(arcanumIdent);
        AssignArcanumPhrase(_arcanumReadout, AssembleArcanumReadout(metadata, modules));
        _arcanumReadout.Scroll.AssignRollY(0);
        _arcanumGlyph.Texture = _locateArcanumGlyph(arcanumIdent);
        ReassembleArcanumEquation(modules);

        AssignEngagedLens(AssayView.Spell);
        _renewPassed = 0;
        _unhide();
        return true;
    }

    public bool Apply(AppraisalReader.WireParsed appraisal)
    {
        var acceptance =
            _dealing.AdmitAppraisalResponse(appraisal.Guid);
        if (!acceptance.Accepted)
            return false;

        var objRef = _objects.Get(appraisal.Guid);
        if (objRef is null)
            return false;

        _bannerVal = AssembleBanner(objRef, appraisal.Properties);
        AssayView lens = PickLens(appraisal);
        bool newlyChosen = lens switch
        {
            AssayView.Item => _gearObjectIdent != appraisal.Guid,
            AssayView.Creature => _beastObjectIdent != appraisal.Guid,
            AssayView.Character => _toonObjectIdent != appraisal.Guid,
            _ => false,
        };

        switch (lens)
        {
            case AssayView.Item:
                _gearObjectIdent = appraisal.Guid;
                ImposeGear(objRef, appraisal, newlyChosen);
                break;
            case AssayView.Creature:
                _beastObjectIdent = appraisal.Guid;
                ImposeBeast(objRef, appraisal, toon: false, newlyChosen);
                break;
            case AssayView.Character:
                _toonObjectIdent = appraisal.Guid;
                ImposeBeast(objRef, appraisal, toon: true, newlyChosen);
                break;
        }

        AssignEngagedLens(lens);
        _renewPassed = 0;
        if (acceptance.FirstResponse)
            _unhide();
        return true;
    }

    public void Tick(double diffSecs)
    {
        if (!_paneShown
            || EngagedLens is not (AssayView.Creature or AssayView.Character)
            || _fighting.LatestMode == FightingManner.NonCombat
            || LatestObjectIdent is 0)
        {
            _renewPassed = 0;
            return;
        }

        if (!double.IsFinite(diffSecs) || diffSecs <= 0)
            return;
        _renewPassed += diffSecs;
        if (_renewPassed < BeastRenewSecs)
            return;
        _renewPassed %= BeastRenewSecs;
        _dealing.RenewLatestAppraisal();
    }

    public void ResetSession()
    {
        _bannerVal = string.Empty;
        _gearDossier = GearAssayDigest.Empty;
        _inscriptionVal = string.Empty;
        _inscriptionField?.AssignPhrase(string.Empty);
        if (_inscriptionField is not null)
        {
            _inscriptionField.Editable = false;
            _inscriptionField.Selectable = false;
            _inscriptionField.Visible = false;
            _inscriptionField.Scroll.AssignRollY(0);
        }
        _signatureVal = string.Empty;
        _signature?.Visible = false;
        _scribeLabel = string.Empty;
        _formerInscription = string.Empty;
        _exhibitInscribable = false;
        _gearObjectIdent = 0;
        _beastObjectIdent = 0;
        _toonObjectIdent = 0;
        _arcanumIdent = 0u;
        _renewPassed = 0;
        _arcanumGlyph.Texture = 0u;
        AssignArcanumPhrase(_arcanumSchool, string.Empty);
        AssignArcanumPhrase(_arcanumMana, string.Empty);
        AssignArcanumPhrase(_arcanumInterval, string.Empty);
        AssignArcanumPhrase(_arcanumSpan, string.Empty);
        AssignArcanumPhrase(_arcanumReadout, string.Empty);
        WipeArcanumEquation();
        AssignEngagedLens(AssayView.Item);
        WipeBeastPhrase();
    }

    public bool HandleFeedAction(FeedAct act)
    {
        if (_destroyed || !_paneShown || act != FeedAct.SelectionExamine)
            return false;

        _shutPane();
        return true;
    }

    private void AttachPhraseSrc(
        WidgetPhrase phrase,
        Func<string> src,
        Func<WidgetPhrase, string, IReadOnlyList<WidgetPhrase.Line>> form)
    {
        var stash = new WidgetTextArrangementShelf<string>(
            phrase,
            form,
            src,
            StringComparer.Ordinal);
        _phraseArrangements.Add(phrase, stash);
        phrase.StrokesSupplier = stash.Provider;
    }

    private void ImposeGear(
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal,
        bool newlyChosen)
    {
        _gearDossier = GearAssayTextComposer.AssembleDossier(
            objRef,
            appraisal,
            LocateArcanum,
            _gearLabels);
        AssignInscription(objRef, appraisal);
        if (newlyChosen)
        {
            _gearPhrase.Scroll.AssignRollY(0);
            _inscriptionPhrase?.Scroll.AssignRollY(0);
            _inscriptionField?.Scroll.AssignRollY(0);
        }
    }

    private void ImposeBeast(
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal,
        bool toon,
        bool newlyChosen)
    {
        WipeBeastPhrase();
        TraitBundle bundle = appraisal.Properties;
        int tier = FetchInt(bundle, 25u);
        AssignPhrase(
            BeastTierValIdent,
            tier > 0
                ? tier.ToString(CultureInfo.InvariantCulture)
                : "???");

        if (toon)
        {
            AssignPhrase(0x10000150u, AssembleToonLineageReadout(bundle));
            AssignPhrase(0x10000151u, AssembleToonBannerReadout(bundle));
            AssignPhrase(0x10000152u, AssembleAvatarKillerReadout(objRef));
            AssignPhrase(0x1000053Au, AssembleAllegianceReadout(bundle));
            _bannerVal = AssembleToonBannerBarLabel(objRef, bundle);
        }
        else
        {
            AssignPhrase(
                BeastReadoutLabelIdent,
                _beastLabels.Resolve(FetchInt(bundle, 2u)));
        }

        ReassembleBeastStats(appraisal, toon);

        if (newlyChosen)
            RestartBeastRoll();
    }

    private void RestartBeastRoll()
    {
        _beastStats?.RestartRoll();
        _beastExtra?.RestartRoll();
        foreach (WidgetPhrase phrase in Descendants(_beastBoard).OfType<WidgetPhrase>())
            phrase.Scroll.AssignRollY(0);
    }

    private void AssignInscription(
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal)
    {
        _formerInscription = string.Empty;
        _scribeLabel = string.Empty;
        _inscriptionVal = string.Empty;
        _signatureVal = string.Empty;

        PublicWeenieBits publicFlagSet =
            (PublicWeenieBits)(objRef.PublicWeenieBitfield ?? 0u);
        bool publicInscribable =
            (publicFlagSet & PublicWeenieBits.Inscribable) != 0;
        _exhibitInscribable = objRef.IsTap
            ? appraisal.HookProfile is { } tap
              && (tap.Flags & 0x1u) is not 0
            : publicInscribable;

        if (!_exhibitInscribable)
        {
            if (_inscriptionField is not null)
            {
                _inscriptionField.AssignPhrase(string.Empty);
                _inscriptionField.Editable = false;
                _inscriptionField.Selectable = false;
                _inscriptionField.Visible = false;
            }
            _inscriptionPhrase?.Visible = false;
            _signature?.Visible = false;
            _inscriptionBackground?.ClickThrough = false;
            return;
        }

        _scribeLabel = FetchString(appraisal.Properties, 8u);
        _formerInscription = FetchString(appraisal.Properties, 7u);
        if (string.IsNullOrEmpty(_scribeLabel))
        {
            _inscriptionVal = "<Inscribe here>";
        }
        else if (!string.IsNullOrEmpty(_formerInscription))
        {
            _inscriptionVal = _formerInscription;
            _signatureVal = $"--{_scribeLabel}";
        }

        if (_inscriptionField is not null)
        {
            _inscriptionField.AssignPhrase(_inscriptionVal);
            _inscriptionField.Visible = true;
        }
        _inscriptionPhrase?.Visible = true;
        _signature?.Visible = true;

        bool editable = publicInscribable
            && _dealing.IsPossessedByAvatar(objRef.ObjectId)
            && (string.IsNullOrEmpty(_scribeLabel)
                || string.Equals(
                    _scribeLabel,
                    _avatarLabel(),
                    StringComparison.OrdinalIgnoreCase));
        if (_inscriptionField is not null)
        {
            _inscriptionField.Editable = editable;
            _inscriptionField.Selectable = editable;
        }
        _inscriptionBackground?.ClickThrough = editable;
    }

    private void AssignArcanumPhrase(WidgetPhrase phrase, string val)
        => FetchPhraseArrangement(phrase).SetValue(val);

    private void AssignPhrase(uint elemIdent, string val, bool scrollable = false)
    {
        if (_arrangement.SeekElem(elemIdent) is not WidgetPhrase phrase)
            return;
        phrase.PreserveFinishOnArrangement = false;
        phrase.WheelRollTurnedOn = scrollable;
        phrase.ClickThrough = !scrollable;
        FetchPhraseArrangement(phrase).SetValue(val);
        if (scrollable)
        {
            var scroller = Descendants(phrase.Ancestor)
                .OfType<WidgetScroller>()
                .FirstOrDefault(contender => contender.Visible);
            scroller?.Model = phrase.Scroll;
        }
    }

    private void AssignEngagedLens(AssayView lens)
    {
        EngagedLens = lens;
        _gearBoard.Visible = lens == AssayView.Item;
        _beastBoard.Visible = lens is AssayView.Creature or AssayView.Character;
        _arcanumBoard.Visible = lens == AssayView.Spell;

        WidgetElem? beastDetails = _arrangement.SeekElem(0x1000014Du);
        WidgetElem? toonDetails = _arrangement.SeekElem(0x1000014Fu);
        beastDetails?.Visible = lens == AssayView.Creature;
        toonDetails?.Visible = lens == AssayView.Character;
    }

    private void ProcessInscriptionGainingFocus()
    {
        if (_inscriptionField is not { Editable: true } field)
            return;
        if (string.IsNullOrEmpty(_scribeLabel))
        {
            field.AssignPhrase(string.Empty);
            _inscriptionVal = string.Empty;
        }
        _signatureVal = $"--{_avatarLabel()}";
    }

    private void ProcessInscriptionLosingFocus(string phrase)
    {
        if (_gearObjectIdent is 0 || !_exhibitInscribable)
            return;

        bool skipVacantUnscribed = string.IsNullOrEmpty(phrase)
                                  && string.IsNullOrEmpty(_scribeLabel);
        if (!skipVacantUnscribed
            && !string.Equals(
                phrase,
                _formerInscription,
                StringComparison.Ordinal))

            _transmitSetInscription(_gearObjectIdent, phrase);

        if (string.IsNullOrEmpty(phrase))
        {
            _inscriptionVal = "<Inscribe here>";
            _signatureVal = string.Empty;
            _scribeLabel = string.Empty;
            _inscriptionField?.AssignPhrase(_inscriptionVal);
        }
        else
        {
            _inscriptionVal = phrase;
            _scribeLabel = _avatarLabel();
            _signatureVal = $"--{_scribeLabel}";
        }
        _formerInscription = phrase;
    }

    private void ProcessPickAltered(PickShift changeover)
    {
        if (!_paneShown || _destroyed)
            return;

        if (changeover.SelectedObjectId is uint objectIdent && objectIdent is not 0u)
        {
            _dealing.StudyChosenOrJoinManner(objectIdent);
            return;
        }

        _shutPane();
    }

    private void ProcessArcanumModuleObjectAltered(ClientThing _)
        => RenewArcanumModules();

    private void ProcessArcanumModuleObjectMoved(ObjectRelocation _)
        => RenewArcanumModules();

    private void ProcessArcanumModuleObjectsCleared()
        => RenewArcanumModules();

    private void AnnounceInscriptionUnavailable()
    {
        if (_inscriptionField?.Editable == true)
            return;
        if (!string.IsNullOrEmpty(_scribeLabel)
            && !string.Equals(
                _scribeLabel,
                _avatarLabel(),
                StringComparison.OrdinalIgnoreCase))
        {
            _sysMsg($"Only {_scribeLabel} can change the inscription");
            return;
        }
        if (_gearObjectIdent is 0
            || !_dealing.IsPossessedByAvatar(_gearObjectIdent))
        {
            _sysMsg("Item must be in your inventory to inscribe.");
            return;
        }
        _sysMsg("This item is not inscribable.");
    }

    private string AssembleToonLineageReadout(TraitBundle bundle)
    {
        int gender = FetchInt(bundle, ToonIdentityText.GenderPropIdent);
        int lineageCluster = FetchInt(bundle, ToonIdentityText.LineageClusterPropIdent);
        string beastBackup = lineageCluster is 0
            ? _beastLabels.Resolve(FetchInt(bundle, 2u))
            : string.Empty;
        return ToonIdentityText.GenderLineageReadout(
            gender, lineageCluster, beastBackup);
    }

    private string AssembleToonBannerReadout(TraitBundle bundle)
    {
        return bundle.Ints.TryGetValue(ToonMarkerIntProp, out int bannerIdent)
            && bannerIdent is not 0
            && _locateToonBanner(unchecked((uint)bannerIdent)) is { Length: > 0 } settled
            ? settled
            : FetchString(bundle, BlueprintStringProp);
    }

    private static string AssembleAvatarKillerReadout(ClientThing objRef)
    {
        PublicWeenieBits bitfield = (PublicWeenieBits)(objRef.PublicWeenieBitfield ?? 0u);
        if ((bitfield & PublicWeenieBits.PlayerKiller) != 0)
            return "Player Killer";
        return (bitfield & PublicWeenieBits.PlayerKillerLite) != 0 ? "Player Killer Lite" : "Non-Player Killer";
    }

    private static string AssembleAllegianceReadout(TraitBundle bundle)
        => FetchInt(bundle, 30u) >= 1 ? FetchString(bundle, 47u) : string.Empty;

    private string AssembleToonBannerBarLabel(ClientThing objRef, TraitBundle bundle)
    {
        int grade = FetchInt(bundle, AllegianceRankTitleChart.AllegianceGradePropIdent);
        int lineageCluster = FetchInt(bundle, ToonIdentityText.LineageClusterPropIdent);
        int gender = FetchInt(bundle, ToonIdentityText.GenderPropIdent);
        string label = _gearLabels.LocateAppropriateLabel(objRef);
        return AllegianceRankTitleChart.ConstructWholeLabel(grade, lineageCluster, gender, label);
    }

    private static string AssembleArcanumReadout(
        SpellMeta metadata,
        IReadOnlyList<ArcanaExamineComponent> modules)
    {
        StringBuilder readout = new StringBuilder(metadata.Description);
        if (modules.Count is 0)
            return readout.ToString();

        readout.Append("\nCOMPONENTS:");
        foreach (ArcanaExamineComponent component in modules)
            readout.Append("\n     ").Append(component.Descriptor.Name);
        return readout.ToString();
    }

    private string AssembleBanner(ClientThing objRef, TraitBundle props)
    {
        string label = FetchString(props, DisplayedLabelStringProp);
        if (string.IsNullOrWhiteSpace(label))
            label = _gearLabels.LocateAppropriateLabel(objRef);
        return objRef.StackSize > 1
            ? $"{objRef.StackSize.ToString(CultureInfo.InvariantCulture)} {label}"
            : label;
    }

    private void ReassembleBeastStats(
        AppraisalReader.WireParsed appraisal,
        bool toon)
    {
        if (_beastStats is null || _beastRankBlueprints is null)
            return;

        _beastStats.Rebuild(
            appraisal.CreatureProfile is { } profile
                ? CreatureAssayRows.Build(profile, appraisal.Success)
                : Array.Empty<CreatureAssayRow>());
        _beastExtra?.Rebuild(
            CreatureAssayRows.AssembleExtra(
                appraisal.Properties,
                appraisal.ArmorLevels,
                toon,
                toon ? _ownFactionBitset() : 0));
    }

    private void ReassembleArcanumEquation(
        IReadOnlyList<ArcanaExamineComponent> modules)
    {
        WipeArcanumEquation();
        if (_arcanumModuleBlueprints is null || modules.Count is 0)
        {
            _arcanumEquationHub.Width = _arcanumEquationChamberWidth;
            _arcanumEquationHub.Left = _arcanumEquationOriginX;
            return;
        }

        float chamberWidth = _arcanumEquationChamberWidth;
        _arcanumEquationHub.Left =
            _arcanumEquationOriginX - chamberWidth * 0.5f * (modules.Count - 1);
        _arcanumEquationHub.Width = chamberWidth * modules.Count;
        _arcanumEquationHub.RestartMooringGrab();
        for (int idx = 0; idx < modules.Count; ++idx)
        {
            var component = modules[idx];
            WidgetElem chamber = _arcanumModuleBlueprints.Create(
                _locateModuleGlyph(component.Descriptor.IconId),
                component.Owned);
            chamber.ArrangementRule = null;
            chamber.Moorings = MooringRims.Left | MooringRims.Top;
            chamber.Left = idx * chamberWidth;
            chamber.Top = 0f;
            _arcanumEquationHub.AddChild(chamber);
            _arcanumEquationChambers.Add(chamber);
        }
    }

    private void ConfigureScrollablePhrase(
        WidgetPhrase phrase,
        uint scrollerIdent,
        Func<string> val)
    {
        phrase.PreserveFinishOnArrangement = false;
        phrase.WheelRollTurnedOn = true;
        phrase.ClickThrough = false;
        AttachPhraseSrc(
            phrase,
            val,
            static (mark, substance) =>
                IndicatorSpecificsPhrase.Shape(mark, substance));
        if (_arrangement.SeekElem(scrollerIdent) is WidgetScroller scroller)
            scroller.Model = phrase.Scroll;
    }

    private void ConfigureScrollableGearPhrase(
        WidgetPhrase phrase,
        uint scrollerIdent)
    {
        phrase.PreserveFinishOnArrangement = false;
        phrase.WheelRollTurnedOn = true;
        phrase.ClickThrough = false;
        _gearPhraseArrangement = new WidgetTextArrangementShelf<GearAssayDigest>(
            phrase,
            static (mark, dossier) =>
                GearAssayTextArrangement.Shape(mark, dossier),
            () => _gearDossier,
            ReferenceEqualityComparer.Instance);
        phrase.StrokesSupplier = _gearPhraseArrangement.Provider;
        if (_arrangement.SeekElem(scrollerIdent) is WidgetScroller scroller)
            scroller.Model = phrase.Scroll;
    }

    private static void ConfigureArcanumPhrase(WidgetPhrase phrase, bool enclose = false)
    {
        phrase.PreserveFinishOnArrangement = false;
        phrase.ClickThrough = true;
        if (enclose)
        {
            phrase.OneLine = false;
            phrase.VerticalJustify = ClientVJustify.Top;
        }
    }

    private WidgetTextArrangementShelf<string> FetchPhraseArrangement(WidgetPhrase phrase)
    {
        if (_phraseArrangements.TryGetValue(
                phrase,
                out WidgetTextArrangementShelf<string>? stash))

            return stash;

        stash = new WidgetTextArrangementShelf<string>(
            phrase,
            static (mark, val) =>
                IndicatorSpecificsPhrase.Shape(mark, val),
            string.Empty,
            StringComparer.Ordinal);
        _phraseArrangements.Add(phrase, stash);
        phrase.StrokesSupplier = stash.Provider;
        return stash;
    }

    private static int FetchInt(TraitBundle props, uint ident)
        => props.Ints.TryGetValue(ident, out int val) ? val : 0;

    private static string FetchString(TraitBundle props, uint ident)
        => props.Texts.TryGetValue(ident, out string? val) ? val : string.Empty;

    private void WipeArcanumEquation()
    {
        foreach (WidgetElem chamber in _arcanumEquationChambers)
            _arcanumEquationHub.DropDescendant(chamber);
        _arcanumEquationChambers.Clear();
        _arcanumEquationHub.Left = _arcanumEquationOriginX;
        _arcanumEquationHub.Width = _arcanumEquationChamberWidth;
        _arcanumEquationHub.RestartMooringGrab();
    }

    private void WipeBeastPhrase()
    {
        foreach (uint ident in new uint[]
        {
            BeastTierValIdent, BeastReadoutLabelIdent,
            0x10000150u, 0x10000151u, 0x10000152u, 0x1000053Au,
        })
            if (_arrangement.SeekElem(ident) is WidgetPhrase phrase)
                FetchPhraseArrangement(phrase).SetValue(string.Empty);
        _beastStats?.Flush();
        _beastExtra?.Flush();
    }

    private SpellMeta? LocateArcanum(uint arcanumIdent)
    {
        return _grimoire.TryFetchMetadata(arcanumIdent & 0x7FFF_FFFFu, out SpellMeta metadata)
                ? metadata
                : null;
    }

    private static AssayView PickLens(AppraisalReader.WireParsed appraisal)
    {
        return appraisal.CreatureProfile is null
            ? AssayView.Item
            : appraisal.Properties.Texts.ContainsKey(BlueprintStringProp)
               || appraisal.Properties.Ints.ContainsKey(ToonMarkerIntProp)
            ? AssayView.Character
            : AssayView.Creature;
    }
}
