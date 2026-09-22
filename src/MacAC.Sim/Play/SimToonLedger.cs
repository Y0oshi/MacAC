using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Traits;

namespace MacAC.Sim.Play;

public readonly record struct SimToonHoldingCapture(
    bool IsDisposed,
    bool InternalSubscriptionsAttached,
    int LearnedSpellCount,
    int ActiveEnchantmentCount,
    int DesiredComponentCount,
    int FavoriteSpellCount,
    int VitalCount,
    int AttributeCount,
    int SkillCount,
    int PositionCount,
    int PropertyCount,
    bool OptionsAreDefaults,
    bool MovementSkillsAreReset,
    bool AutonomyIsDefault = true,
    bool OptionsAreClean = true,
    int TitleCount = 0,
    bool DisplayTitleIsDefault = true)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed && !InternalSubscriptionsAttached
        && LearnedSpellCount is 0 && ActiveEnchantmentCount is 0 && DesiredComponentCount is 0 && FavoriteSpellCount is 0
        && VitalCount is 0 && AttributeCount is 0 && SkillCount is 0 && PositionCount is 0 && PropertyCount is 0
        && OptionsAreDefaults && MovementSkillsAreReset && AutonomyIsDefault && OptionsAreClean
        && TitleCount is 0 && DisplayTitleIsDefault;
        }
    }
}

public sealed class SimToonLedger : IDisposable
{
    public const uint ExecAptitudeIdent = 24u;
    public const uint LeapAptitudeIdent = 22u;
    public const uint WholeAutonomyTier = 2u;

    private const int FavoriteTabs = 8;
    private const uint UpperWantedModuleQuantity = 5000u;

    private bool _destroyed;
    private bool _hooked;
    private long _sheetRev;
    private long _grimoireRev;
    private uint _autonomy = WholeAutonomyTier;

    // The base run/jump skills and augmentations the effective skills are derived from.
    private int _execBase = -1;
    private int _leapBase = -1;
    private SkillRules.AugBonuses _augmentations;

    public SimToonLedger(ArcanumChart? arcanumChart = null, TimeProvider? momentSupplier = null)
    {
        Spellbook = new Grimoire(arcanumChart);
        LocalPlayer = new SelfState(Spellbook);
        Options = new SimToonOptionsLedger(momentSupplier);
        TravelAptitudes = new SimLocomotionSkillLedger();
        Titles = new SimToonTitleLedger();
        View = new Lens(this);
        Spellbook.StateChanged += OnGrimoireShift;
        Spellbook.EnchantmentsChanged += RederiveTravelAptitudes;
        LocalPlayer.Changed += OnVitalShift;
        LocalPlayer.AttributeChanged += OnAttrShift;
        LocalPlayer.CharacterChanged += OnSheetShift;
        _hooked = true;
    }

    public Grimoire Spellbook { get; }
    public SelfState LocalPlayer { get; }
    public SimToonOptionsLedger Options { get; }
    public SimLocomotionSkillLedger TravelAptitudes { get; }
    public SimToonTitleLedger Titles { get; }
    public ISimToonLens View { get; }
    public bool IsDisposed => _destroyed;

    public bool TrySetAutonomyTier(uint tier)
    {
        Live();
        if (tier > WholeAutonomyTier)
            return false;
        Volatile.Write(ref _autonomy, tier);
        return true;
    }

    public bool IsOlthoiAvatar
    {
        get
        {
            int lineage = LocalPlayer.Properties.FetchInt((uint)TraitInt.HeritageGroup, 0);
            return lineage is 12 or 13; // HeritageGroup.Olthoi / OlthoiAcid
        }
    }

    public uint AutonomyTier => Volatile.Read(ref _autonomy);

    public bool UseLocusFromSrv => AutonomyTier != WholeAutonomyTier;

    public SimToonHoldingCapture CaptureOwnership()
    {
        int favorites = 0;
        for (int tab = 0; tab < FavoriteTabs; ++tab)
            favorites += Spellbook.FetchFavorites(tab).Count;

        int vitals = Enum.GetValues<SelfState.VitalSort>().Count(sort => LocalPlayer.Get(sort) is not null);
        int attrs = Enum.GetValues<SelfState.StatKind>().Count(sort => LocalPlayer.FetchAttr(sort) is not null);

        TraitBundle traits = LocalPlayer.Properties;
        int props = traits.Bools.Count + traits.Ints.Count + traits.Int64s.Count + traits.Floats.Count
            + traits.Texts.Count + traits.BlobIdents.Count + traits.InstIdents.Count;
        var knobs = Options.Snapshot;

        return new SimToonHoldingCapture(
            _destroyed,
            _hooked,
            Spellbook.LearnedTally,
            Spellbook.ActiveCount,
            Spellbook.DesiredComponents.Count,
            favorites,
            vitals,
            attrs,
            LocalPlayer.Skills.Count,
            LocalPlayer.Loci.Count,
            props,
            knobs.Options1 == SimToonOptionsLedger.DefaultOptions1 && knobs.Options2 == SimToonOptionsLedger.DefaultOptions2,
            TravelAptitudes.IsPristine && _execBase == -1 && _leapBase == -1 && _augmentations == default,
            AutonomyTier == WholeAutonomyTier,
            OptionsAreClean: !Options.IsDirty,
            TitleCount: Titles.Count,
            DisplayTitleIsDefault: Titles.ReadoutBannerIdent is 0u);
    }

    public void RefreshTravelAptitudeBase(int execAptitudeBase, int leapAptitudeBase)
    {
        Live();
        if (execAptitudeBase >= 0)
            _execBase = execAptitudeBase;
        if (leapAptitudeBase >= 0)
            _leapBase = leapAptitudeBase;
        RederiveTravelAptitudes();
    }

    public void RefreshTravelAptitudeAugmentations(SkillRules.AugBonuses augmentations)
    {
        Live();
        if (_augmentations == augmentations)
            return;
        _augmentations = augmentations;
        RederiveTravelAptitudes();
    }

    public void PlaceArcanumMetadata(ArcanumChart arcanumChart)
    {
        Live();
        Spellbook.SetupMetadata(arcanumChart);
    }

    public void RestartGrimoire()
    {
        Live();
        Spellbook.Clear();
    }

    public void RestartOwnAvatar()
    {
        Live();
        LocalPlayer.Clear();
    }

    public bool TryAppendFavorite(int tabOrdinal, int locus, uint arcanumIdent, Action broadcastOutgoing)
    {
        Live();
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if ((uint)tabOrdinal >= FavoriteTabs || locus < 0 || arcanumIdent is 0u)
            return false;
        Spellbook.SetFavorite(tabOrdinal, locus, arcanumIdent);
        broadcastOutgoing();
        return true;
    }

    public bool TryDropFavorite(int tabOrdinal, uint arcanumIdent, Action broadcastOutgoing)
    {
        Live();
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if ((uint)tabOrdinal >= FavoriteTabs || arcanumIdent is 0u)
            return false;
        Spellbook.RemoveFavorite(tabOrdinal, arcanumIdent);
        broadcastOutgoing();
        return true;
    }

    public void ApplyGrimoireSift(uint filters, Action broadcastOutgoing)
    {
        Live();
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if (Spellbook.GrimoireFilters == filters)
            return;
        Spellbook.SetSpellbookFilters(filters);
        broadcastOutgoing();
    }

    /// <summary>The local edit lands even when the send throws.</summary>
    public bool TrySetWantedModule(uint moduleIdent, uint quantity, Action broadcastOutgoing)
    {
        Live();
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        if (moduleIdent is 0u || quantity > UpperWantedModuleQuantity)
            return false;
        try { broadcastOutgoing(); }
        finally { Spellbook.SetDesiredComponent(moduleIdent, quantity); }
        return true;
    }

    public void WipeDesiredComponents(Action broadcastOutgoing)
    {
        Live();
        ArgumentNullException.ThrowIfNull(broadcastOutgoing);
        try { broadcastOutgoing(); }
        finally { Spellbook.PurgeWantedModules(); }
    }

    public void ResetSession()
    {
        Live();
        List<Exception>? misses = null;
        Wipe(ref misses);
        if (misses is not null)
            throw new AggregateException("Runtime character state didn't converge during reset", misses);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        List<Exception>? misses = null;
        try
        {
            Wipe(ref misses);
        }
        finally
        {
            Spellbook.StateChanged -= OnGrimoireShift;
            Spellbook.EnchantmentsChanged -= RederiveTravelAptitudes;
            LocalPlayer.Changed -= OnVitalShift;
            LocalPlayer.AttributeChanged -= OnAttrShift;
            LocalPlayer.CharacterChanged -= OnSheetShift;
            _hooked = false;
            _destroyed = true;
        }
        if (misses is not null)
            throw new AggregateException("Runtime character state didn't converge during disposal", misses);
    }

    private void Live() => ObjectDisposedException.ThrowIf(_destroyed, this);

    private void RederiveTravelAptitudes()
    {
        int exec = _execBase >= 0 ? NetAptitude(_execBase, ExecAptitudeIdent) : -1;
        int leap = _leapBase >= 0 ? NetAptitude(_leapBase, LeapAptitudeIdent) : -1;
        var execMod = Spellbook.ObtainAptitudeMod(ExecAptitudeIdent);
        int engaged = Spellbook.ActiveEnchantments.Count();
        Console.WriteLine(FormattableString.Invariant(
            $"[stat-chain] base run={_execBase} jump={_leapBase} runMod={execMod.Multiplier:F4}x+{execMod.Additive:F1} -> eff run={exec} jump={leap} (activeEnchantments={engaged})"));
        TravelAptitudes.Update(exec, leap);
    }

    private int NetAptitude(int baseAptitude, uint aptitudeIdent)
    {
        var mod = Spellbook.ObtainAptitudeMod(aptitudeIdent);
        float vitae = EnchantmentRules.FetchVitaeMultiplier(Spellbook.ActiveEnchantments);
        uint advancement = LocalPlayer.FetchAptitude(aptitudeIdent)?.Status ?? 0u;
        int enchantedBase = baseAptitude + LocalPlayer.AttrEnchantmentAptitudeDiff(aptitudeIdent);
        return SkillRules.Derive(baseAptitude, enchantedBase, aptitudeIdent, advancement, _augmentations, mod, vitae).EffectiveLevel;
    }

    private void Wipe(ref List<Exception>? misses)
    {
        foreach (Action hop in (Action[])[Spellbook.Clear, LocalPlayer.Clear, Options.ResetSession])
            Attempt(hop, ref misses);
        _execBase = -1;
        _leapBase = -1;
        _augmentations = default;
        Volatile.Write(ref _autonomy, WholeAutonomyTier);
        foreach (Action hop in (Action[])[TravelAptitudes.ResetSession, Titles.ResetSession])
            Attempt(hop, ref misses);
    }

    private static void Attempt(Action hop, ref List<Exception>? misses)
    {
        try { hop(); }
        catch (Exception problem) { (misses ??= []).Add(problem); }
    }

    private void OnGrimoireShift() => Interlocked.Increment(ref _grimoireRev);
    private void OnVitalShift(SelfState.VitalSort _) => Interlocked.Increment(ref _sheetRev);
    private void OnAttrShift(SelfState.StatKind _) => Interlocked.Increment(ref _sheetRev);
    private void OnSheetShift() => Interlocked.Increment(ref _sheetRev);

    private sealed class Lens(SimToonLedger holder) : ISimToonLens
    {
        public SimToonCapture Snapshot
        {
            get
            {
                return new(
            Interlocked.Read(ref holder._sheetRev),
            Interlocked.Read(ref holder._grimoireRev),
            holder.Options.Snapshot,
            holder.TravelAptitudes.Snapshot,
            holder.Spellbook.LearnedArcana.Count,
            holder.Spellbook.ActiveEnchantments.Count(),
            holder.Spellbook.DesiredComponents.Count,
            holder.LocalPlayer.Skills.Count,
            holder.Spellbook.GrimoireFilters,
            holder.Titles.Snapshot);
            }
        }

        public bool TryFetchVital(int kind, out SimVitalCapture vital)
        {
            SelfState.VitalSort order = (SelfState.VitalSort)kind;
            if (!Enum.IsDefined(order) || holder.LocalPlayer.Get(order) is not SelfState.VitalFrame frame)
            {
                vital = default;
                return false;
            }
            vital = new SimVitalCapture(kind, frame.Ranks, frame.Start, frame.Xp, frame.Current, holder.LocalPlayer.FetchUpperApprox(order) ?? 0u);
            return true;
        }

        public bool TryFetchAttr(int sort, out SimTraitCapture attr)
        {
            SelfState.StatKind stat = (SelfState.StatKind)sort;
            if (!Enum.IsDefined(stat) || holder.LocalPlayer.FetchAttr(stat) is not SelfState.StatFrame frame)
            {
                attr = default;
                return false;
            }
            attr = new SimTraitCapture(sort, frame.Ranks, frame.Start, frame.Xp, frame.Current);
            return true;
        }

        public bool TryFetchSkill(uint aptitudeIdent, out SimSkillCapture aptitude)
        {
            if (holder.LocalPlayer.FetchAptitude(aptitudeIdent) is not SelfState.SkillFrame frame)
            {
                aptitude = default;
                return false;
            }
            aptitude = new SimSkillCapture(frame.SkillId, frame.Ranks, frame.Status, frame.Xp, frame.Init, frame.Resistance, frame.LastUsed, frame.FormulaBonus, frame.LatestTier);
            return true;
        }

        public bool KnowsArcanum(uint arcanumIdent) => holder.Spellbook.Knows(arcanumIdent);

        public bool TryFetchFavorite(int tabOrdinal, int locus, out uint arcanumIdent)
        {
            var favorites = holder.Spellbook.FetchFavorites(tabOrdinal);
            bool inSpan = (uint)locus < (uint)favorites.Count;
            arcanumIdent = inSpan ? favorites[locus] : 0u;
            return inSpan;
        }

        public bool TryFetchWantedModule(uint moduleIdent, out uint quantity)
        {
            return holder.Spellbook.DesiredComponents.TryGetValue(moduleIdent, out quantity);
        }
    }
}
