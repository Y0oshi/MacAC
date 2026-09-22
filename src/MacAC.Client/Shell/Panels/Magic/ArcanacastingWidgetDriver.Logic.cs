using MacAC.Cockpit.Input;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ArcanacastingWidgetDriver
{
    public int EngagedTab { get; private set; }

    public static ArcanacastingWidgetDriver? Bind(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        SimArcanaCastLedger casting,
        ClientThingChart objects,
        Func<uint> avatarOid,
        Func<uint, uint> locateArcanumGlyph,
        Func<ClientThing, uint> locateGearPullGlyph,
        Action<uint> useGear,
        PickPhase pick,
        Action<int, int, uint>? appendFavorite,
        Action<int, uint>? dropFavorite,
        WidgetShortcutDigitGraphics? shortcutDigits = null,
        uint vacantSocketSprite = 0u,
        Action<uint>? examineArcanum = null)
    {
        if (arrangement.SeekElem(CastingBtnIdent) is not WidgetBtn cast
            || arrangement.SeekElem(EndowmentIdent) is not { } endowmentHub)
            return null;

        WidgetElem[] tabs = new WidgetElem[8];
        WidgetElem[] clusters = new WidgetElem[8];
        WidgetGearRoster?[] rosters = new WidgetGearRoster?[8];
        WidgetScroller?[] scrollbars = new WidgetScroller?[8];
        for (int idx = 0; idx < 8; ++idx)
        {
            if (arrangement.SeekElem(TabIdents[idx]) is not { } tab
                || arrangement.SeekElem(ClusterIdents[idx]) is not { } cluster)
                return null;
            tabs[idx] = tab;
            clusters[idx] = cluster;
            rosters[idx] = Descendants(cluster).OfType<WidgetGearRoster>().FirstOrDefault();
            scrollbars[idx] = Descendants(cluster).OfType<WidgetScroller>().FirstOrDefault(
                scroller => scroller.DatElemIdent == FavoriteScrollerIdent);
        }

        return new ArcanacastingWidgetDriver(
            arrangement, grimoire, casting, objects, avatarOid, locateArcanumGlyph,
            locateGearPullGlyph, useGear, examineArcanum, pick,
            appendFavorite, dropFavorite,
            tabs, clusters, rosters, scrollbars, cast, endowmentHub,
            shortcutDigits, vacantSocketSprite);
    }

    public void AttachFavorite(uint arcanumIdent)
    {
        var arcana = _grimoire.FetchFavorites(EngagedTab);
        if (arcana.Contains(arcanumIdent))
        {
            PickArcanum(arcanumIdent);
            return;
        }
        int locus = arcana.Count;
        _appendFavorite?.Invoke(EngagedTab, locus, arcanumIdent);
        PickArcanum(arcanumIdent);
    }

    public bool Handle(FeedAct act)
    {
        if (TryLookupArcanumShortcut(act, out int ordinal))
        {
            var arcana = _grimoire.FetchFavorites(EngagedTab);
            if (ordinal < arcana.Count)
            {
                PickArcanum(arcana[ordinal]);
                InvokeChosen();
            }
            return true;
        }

        switch (act)
        {
            case FeedAct.CombatPrevSpellTab: PickTab((EngagedTab + 7) % 8); return true;
            case FeedAct.CombatNextSpellTab: PickTab((EngagedTab + 1) % 8); return true;
            case FeedAct.CombatFirstSpellTab: PickTab(0); return true;
            case FeedAct.CombatLastSpellTab: PickTab(7); return true;
            case FeedAct.CombatPrevSpell: ShiftPick(-1, false); return true;
            case FeedAct.CombatNextSpell: ShiftPick(1, false); return true;
            case FeedAct.CombatFirstSpell: ShiftPick(0, true); return true;
            case FeedAct.CombatLastSpell: ShiftPick(-1, true); return true;
            case FeedAct.CombatCastCurrentSpell: InvokeChosen(); return true;
            default: return false;
        }
    }

    public void OnShown() => SynchronizePick();

    public void Tick()
    {
        if (_endowmentStale)
        {
            _endowmentStale = false;
            RefreshEndowment();
            PickTab(EngagedTab);
        }
        if (_favoritesStale && !_favoritePullEngaged)
        {
            _favoritesStale = false;
            Rebuild();
        }
        for (int tab = 0; tab < _rosters.Length; ++tab)
        {
            if (_rosters[tab] is not { } roster || _favoriteViewRectWidths[tab] == roster.Width)
                continue;
            _favoriteViewRectWidths[tab] = roster.Width;
            roster.ArrangementChambers();
            if (!_endowmentChosen[tab] && _chosen[tab] is uint chosen)
            {
                RollChosenArcanumIntoLens(tab, chosen);
                roster.ArrangementChambers();
            }
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _grimoire.SpellbookChanged -= OnGrimoireAltered;
        _pick.Changed -= OnPickAltered;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.ObjectRemoved -= OnObjectAltered;
        _objects.Cleared -= OnObjectsCleared;
        foreach (WidgetElem tab in _tabs) AssignPress(tab, null);
        foreach (WidgetGearRoster? roster in _rosters)
        {
            if (roster is null) continue;
            roster.PrimaryRegistryListingPressed = null;
            roster.ExamineRegistryListingAsked = null;
        }
        _cast.OnClick = null;
    }

    internal static bool TryLookupArcanumShortcut(
        FeedAct act,
        out int ordinal)
    {
        if (act is >= FeedAct.UseSpellSlot_1
            and <= FeedAct.UseSpellSlot_9)
        {
            ordinal = (int)act - (int)FeedAct.UseSpellSlot_1;
            return true;
        }

        ordinal = act switch
        {
            FeedAct.UseSpellSlot_10 => 9,
            FeedAct.UseSpellSlot_11 => 10,
            FeedAct.UseSpellSlot_12 => 11,
            _ => -1,
        };
        return ordinal >= 0;
    }

    internal static GearDragAcceptance FavoritePullOverAcceptance(object cargo)
    {
        return cargo is ArcanaFavoriteDragPayload or ArcanabookShortcutDragPayload
                ? GearDragAcceptance.Accept
                : GearDragAcceptance.None;
    }

    internal int FavoriteDiscardOrdinal(
        ArcanaFavoriteDragPayload cargo, int markTab, WidgetGearRoster roster, WidgetGearSlot chamber)
    {
        int ordinal = Math.Max(0, roster.IdxOf(chamber));
        if (chamber.IsVacantSocket)
            return Math.Min(ordinal, _grimoire.FetchFavorites(markTab).Count);
        if (cargo.SourceTab == markTab && cargo.SourcePosition < ordinal)
            ordinal -= 1;
        return ordinal;
    }

    private void PickTab(int tab)
    {
        EngagedTab = Math.Clamp(tab, 0, 7);
        for (int idx = 0; idx < 8; ++idx)
        {
            AssignChosen(_tabs[idx], idx == EngagedTab);
            _clusters[idx].Visible = idx == EngagedTab;
        }
        var favorites = _grimoire.FetchFavorites(EngagedTab);
        if (_endowmentChosen[EngagedTab] && _endowmentGearIdent is not 0u)
            _chosen[EngagedTab] = null;
        else if (_chosen[EngagedTab] is not uint chosen || !favorites.Contains(chosen))
        {
            _chosen[EngagedTab] = favorites.Count is 0 ? null : favorites[0];
            _endowmentChosen[EngagedTab] = favorites.Count is 0 && _endowmentGearIdent is not 0u;
        }
        SynchronizePick();
        RefreshCastingReadiness();
    }

    private void PickArcanum(uint arcanumIdent)
    {
        _endowmentChosen[EngagedTab] = false;
        _chosen[EngagedTab] = arcanumIdent;
        SynchronizePick();
        RollChosenArcanumIntoLens(EngagedTab, arcanumIdent);
        RefreshCastingReadiness();
    }

    private void PickEndowment()
    {
        if (_endowmentGearIdent is 0u) return;
        _endowmentChosen[EngagedTab] = true;
        _chosen[EngagedTab] = null;
        SynchronizePick();
        RefreshCastingReadiness();
    }

    private void ShiftPick(int diff, bool rim)
    {
        var arcana = _grimoire.FetchFavorites(EngagedTab);
        int tally = arcana.Count + (_endowmentGearIdent is not 0u ? 1 : 0);
        if (tally is 0) return;
        int latest = _endowmentChosen[EngagedTab]
            ? 0
            : (_chosen[EngagedTab] is uint ident ? arcana.OrdinalOf(ident) : -1)
                + (_endowmentGearIdent is not 0u ? 1 : 0);
        int upcoming = rim ? (diff < 0 ? tally - 1 : 0) : (latest + diff + tally) % tally;
        if (_endowmentGearIdent is not 0u && upcoming is 0) PickEndowment();
        else PickArcanum(arcana[upcoming - (_endowmentGearIdent is not 0u ? 1 : 0)]);
    }

    private void RollChosenArcanumIntoLens(int tab, uint arcanumIdent)
    {
        WidgetGearRoster? roster = _rosters[tab];
        if (roster is null) return;
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            if (roster.GetItem(idx) is WidgetRegistrySlot { ListingTag: var listingIdent }
                && listingIdent == arcanumIdent)
            {
                roster.RollGearIntoLens(idx);
                return;
            }
        }
    }

    private void InvokeChosen()
    {
        if (_endowmentChosen[EngagedTab] && _endowmentGearIdent is not 0u)
            _useGear(_endowmentGearIdent);
        else if (_chosen[EngagedTab] is uint arcanumIdent)
            _casting.Cast(arcanumIdent);
    }

    private void Rebuild()
    {
        for (int tab = 0; tab < 8; ++tab)
        {
            WidgetGearRoster? roster = _rosters[tab];
            if (roster is null) continue;
            var favorites = _grimoire.FetchFavorites(tab);
            int markTab = tab;
            roster.RegistryDropped = (cargo, x, _) =>
            {
                if (cargo is ArcanabookShortcutDragPayload shortcut)
                    DiscardGrimoireShortcut(
                        shortcut, markTab, DiscardLocus(roster, x, favorites.Count));
            };
            using (roster.DeferArrangement())
            {
                roster.Flush();
                roster.SingleRank = true;
                roster.HorizontalRoll = true;
                roster.ChamberWidth = 32f;
                roster.ChamberHeight = 32f;
                roster.ChamberVacantSprite = _vacantSocketSprite;
                roster.VacantSocketMaker = () => BuildVacantFavoriteSocket(roster, markTab);
                roster.PopulateShownVacantSockets = true;
                foreach (uint arcanumIdent in favorites)
                {
                    uint ident = arcanumIdent;
                    int locus = roster.FetchCountWIDGETGearList();
                    _grimoire.TryFetchMetadata(ident, out SpellMeta? metadata);
                    WidgetRegistrySlot? socket = null;
                    socket = new WidgetRegistrySlot
                    {
                        ListingTag = ident,
                        RegistryGlyphTexture = metadata is null ? 0u : _locateArcanumGlyph(ident),
                        Label = metadata?.Name ?? $"Spell {ident}",
                        SpriteResolve = roster.SpriteResolve,
                        RegistryPullCargo = new ArcanaFavoriteDragPayload(tab, locus, ident),
                        PullBegan = cargo => CommenceFavoritePull((ArcanaFavoriteDragPayload)cargo),
                        PullEnded = cargo => RebuildRest((ArcanaFavoriteDragPayload)cargo),
                        PullOverAcceptance = FavoritePullOverAcceptance,
                        Dropped = cargo =>
                        {
                            if (cargo is ArcanaFavoriteDragPayload favorite)
                                DiscardFavorite(favorite, markTab, roster, socket!);
                            else if (cargo is ArcanabookShortcutDragPayload shortcut)
                                DiscardGrimoireShortcut(shortcut, markTab, locus);
                        },
                        DoubleClicked = InvokeChosen
                    };
                    roster.AddItem(socket);
                }
            }
            StampShortcutTopLayers(roster);
        }
        PickTab(EngagedTab);
    }

    private WidgetRegistrySlot BuildVacantFavoriteSocket(WidgetGearRoster roster, int markTab)
    {
        int shortcutOrdinal = roster.FetchCountWIDGETGearList();
        WidgetRegistrySlot? socket = null;
        socket = new WidgetRegistrySlot
        {
            SpriteResolve = roster.SpriteResolve,
            PullOverAcceptance = FavoritePullOverAcceptance,
            Dropped = cargo =>
            {
                if (cargo is ArcanaFavoriteDragPayload favorite)
                    DiscardFavorite(favorite, markTab, roster, socket!);
                else if (cargo is ArcanabookShortcutDragPayload shortcut)
                {
                    int locus = Math.Max(0, roster.IdxOf(socket!));
                    locus = Math.Min(locus, _grimoire.FetchFavorites(markTab).Count);
                    DiscardGrimoireShortcut(shortcut, markTab, locus);
                }
            },
        };
        ConfigureShortcutTopLayer(socket, shortcutOrdinal);
        return socket;
    }

    private void StampShortcutTopLayers(WidgetGearRoster roster)
    {
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            var socket = roster.GetItem(idx);
            if (socket is null) continue;
            ConfigureShortcutTopLayer(socket, idx);
        }
    }

    private void ConfigureShortcutTopLayer(WidgetGearSlot socket, int ordinal)
    {
        socket.RegularDigits = _shortcutDigits?.RegularDigits;
        socket.GhostedDigits = _shortcutDigits?.GhostedDigits;
        socket.VacantDigits = _shortcutDigits?.EmptyDigits;
        if (ordinal < 9)
            socket.AssignShortcutCount(ordinal, ghosted: false);
        else
            socket.WipeShortcutCount();
    }

    private void ConfigureArcanumLabel()
    {
        if (_arcanumLabel is null) return;
        _arcanumLabel.OneLine = true;
        _arcanumLabel.Centered = true;
        _arcanumLabel.Padding = 0;
        _arcanumLabel.StrokesSupplier = () =>
        {
            uint? chosenArcanum = _endowmentChosen[EngagedTab]
                ? (_endowmentArcanumIdent is 0u ? null : _endowmentArcanumIdent)
                : _chosen[EngagedTab];
            string label = chosenArcanum is uint arcanumIdent
                && _grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata)
                    ? metadata.Name : string.Empty;
            return [new WidgetPhrase.Line(label, _arcanumLabel.DefaultTint)];
        };
    }

    private void CommenceFavoritePull(ArcanaFavoriteDragPayload cargo)
    {
        _favoritePullEngaged = true;
        _dropFavorite?.Invoke(cargo.SourceTab, cargo.SpellId);
    }

    private void RebuildRest(ArcanaFavoriteDragPayload cargo) => _favoritePullEngaged = false;

    private void DiscardFavorite(
        ArcanaFavoriteDragPayload cargo, int markTab, WidgetGearRoster roster, WidgetGearSlot chamber)
    {
        int markLocus = FavoriteDiscardOrdinal(cargo, markTab, roster, chamber);
        _appendFavorite?.Invoke(markTab, markLocus, cargo.SpellId);
        _chosen[markTab] = cargo.SpellId;
    }

    private void DiscardGrimoireShortcut(
        ArcanabookShortcutDragPayload cargo,
        int markTab,
        int markLocus)
    {
        _appendFavorite?.Invoke(markTab, markLocus, cargo.SpellId);
        _chosen[markTab] = cargo.SpellId;
    }

    private static int DiscardLocus(WidgetGearRoster roster, int ownX, int favoriteTally)
    {
        int locus = roster.ChamberWidth <= 0f
            ? favoriteTally
            : (int)MathF.Floor(MathF.Max(0f, ownX) / roster.ChamberWidth);
        return Math.Clamp(locus, 0, favoriteTally);
    }

    private void OnGrimoireAltered() => _favoritesStale = true;

    private void OnObjectAltered(ClientThing _) => _endowmentStale = true;

    private void OnObjectsCleared() => _endowmentStale = true;

    private void OnPickAltered(PickShift _) => RefreshCastingReadiness();

    private void SynchronizePick()
    {
        for (int tab = 0; tab < 8; ++tab)
        {
            WidgetGearRoster? roster = _rosters[tab];
            if (roster is null) continue;
            for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
                if (roster.GetItem(idx) is WidgetRegistrySlot socket)
                    socket.Selected = socket.ListingTag == _chosen[tab];
        }
        _endowmentSocket.Selected = _endowmentGearIdent is not 0u
            && _endowmentChosen[EngagedTab];
    }

    private void RefreshCastingReadiness()
    {
        if (_endowmentChosen[EngagedTab] && _endowmentGearIdent is not 0u)
        {
            (bool turnedOn, string? hint) = CalculateEndowmentCastingPhase();
            _cast.Enabled = turnedOn;
            _cast.TooltipText = hint;
            return;
        }

        if (_chosen[EngagedTab] is uint arcanumIdent)
        {
            (bool turnedOn, string? hint) = CalculateArcanumCastingPhase(arcanumIdent);
            _cast.Enabled = turnedOn;
            _cast.TooltipText = hint;
            return;
        }

        _cast.Enabled = false;
        bool anyFavorites = false;
        for (int tab = 0; tab < 8 && !anyFavorites; ++tab)
            anyFavorites = _grimoire.FetchFavorites(tab).Count > 0;
        _cast.TooltipText = anyFavorites
            ? "Select a spell to cast"
            : "You have no spells ready to cast";
    }

    private void RefreshEndowment()
    {
        var endowment = _objects.FetchEquippedBy(_avatarOid())
            .FirstOrDefault(gear =>
                (gear.CurrentlyEquippedLocale & WieldBitmask.Held) != 0
                && (gear.Type & GearKind.Caster) != 0
                && gear.SpellId.GetValueOrDefault() is not 0u);

        _endowmentGearIdent = endowment?.ObjectId ?? 0u;
        _endowmentArcanumIdent = endowment?.SpellId ?? 0u;
        _endowmentHub.Visible = endowment is not null;
        _endowmentSocket.ListingTag = _endowmentGearIdent;
        _endowmentSocket.RegistryGlyphTexture = _endowmentArcanumIdent is 0u
            ? 0u : _locateArcanumGlyph(_endowmentArcanumIdent);
        _endowmentSocket.RegistryTopLayerTexture = endowment is null
            ? 0u : _locateGearPullGlyph(endowment);
        string arcanumLabel = _grimoire.TryFetchMetadata(_endowmentArcanumIdent, out SpellMeta metadata)
            ? metadata.Name : $"Spell {_endowmentArcanumIdent}";
        _endowmentSocket.Label = endowment is null
            ? string.Empty : $"{endowment.FetchAppropriateLabel()} ({arcanumLabel})";

        for (int tab = 0; tab < 8; ++tab)
        {
            if (_endowmentGearIdent is not 0u && _chosen[tab] is null)
                _endowmentChosen[tab] = true;
            else if (_endowmentGearIdent is 0u && _endowmentChosen[tab])
                _endowmentChosen[tab] = false;
        }
    }

    private (bool enabled, string? tooltip) CalculateEndowmentCastingPhase()
    {
        var endowment = _objects.Get(_endowmentGearIdent);
        if (endowment is null)
            return (false, null);

        string composedLabel = ConstructEndowmentLabel(endowment);
        if (GearUseability.AllowsSelfMark(endowment.Useability ?? 0u))
            return (true, $"USE the {composedLabel}");

        uint? markIdent = _pick.ChosenObjectTag;
        if (markIdent is null or 0u)
            return (false, $"You must select a target for the {composedLabel}");

        string markLabel = _objects.Get(markIdent.Value)?.FetchAppropriateLabel() ?? composedLabel;
        return (true, $"USE the {composedLabel} on {markLabel}");
    }

    private (bool enabled, string? tooltip) CalculateArcanumCastingPhase(uint arcanumIdent)
    {
        if (!_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata))
            return (false, null);

        string arcanumLabel = metadata.Name;
        var latch = _casting.EvaluateCastingLatch(arcanumIdent);
        switch (latch)
        {
            case ArcanaCastTurnstile.NoTargetNeeded:
                return (true, $"CAST {arcanumLabel}");
            case ArcanaCastTurnstile.TargetCompatible:
                {
                    uint? markIdent = _pick.ChosenObjectTag;
                    string? markLabel = markIdent is uint ident and not 0u
                        ? _objects.Get(ident)?.FetchAppropriateLabel()
                        : null;
                    return (true, markLabel is null
                        ? $"CAST {arcanumLabel}"
                        : $"CAST {arcanumLabel} on {markLabel}");
                }
            case ArcanaCastTurnstile.TargetIncompatible:
                return (false, $"You must select an appropriate target for {arcanumLabel}");
            case ArcanaCastTurnstile.NoTargetSelected:
                return (false, $"You must select a target for {arcanumLabel}");
            default:
                return (false, null);
        }
    }

    private string ConstructEndowmentLabel(ClientThing endowment)
    {
        string gearLabel = endowment.FetchAppropriateLabel();
        return _grimoire.TryFetchMetadata(_endowmentArcanumIdent, out SpellMeta arcanumMetadata)
            ? $"{gearLabel} ({arcanumMetadata.Name})"
            : gearLabel;
    }

    private static IEnumerable<WidgetElem> Descendants(WidgetElem trunk)
    {
        foreach (WidgetElem descendant in trunk.Children)
        {
            yield return descendant;
            foreach (WidgetElem nested in Descendants(descendant)) yield return nested;
        }
    }

    private static void AssignPress(WidgetElem elem, Action? act)
    {
        elem.ClickThrough = act is null;
        switch (elem)
        {
            case WidgetBtn btn: btn.OnClick = act; break;
            case WidgetPhrase phrase: phrase.OnClick = act; break;
            case WidgetDatElement dat: dat.OnClick = act; break;
        }
    }

    private static void AssignChosen(WidgetElem elem, bool chosen)
    {
        if (elem is IWidgetDatStateful stateful
            && stateful.TrySetCanonPhase(chosen ? CanonWidgetStateIds.Open : CanonWidgetStateIds.Closed))
            return;
        if (elem is WidgetBtn btn)
            btn.Selected = chosen;
        else if (elem is WidgetPhrase phrase)
            phrase.DefaultTint = chosen
                ? new System.Numerics.Vector4(1f, 1f, 1f, 1f)
                : new System.Numerics.Vector4(0.65f, 0.65f, 0.65f, 1f);
    }
}
