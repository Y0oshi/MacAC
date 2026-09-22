using System.Globalization;

namespace MacAC.Mechanics.Arcana;

public readonly record struct KnownSpellRow(uint SpellId, float Power);

/// <summary>One enchantment layer as the server reports it.</summary>
public readonly record struct LiveEnchantmentRow(
    uint SpellId,
    uint LayerId,
    double Duration,
    uint CasterGuid,
    uint? StatModType = null,
    uint? StatModKey = null,
    float? StatModValue = null,
    uint Bucket = 0,
    double StartTime = 0,
    uint SpellCategory = 0,
    uint PowerLevel = 0,
    float DegradeModifier = 0,
    float DegradeLimit = 0,
    double LastTimeDegraded = 0,
    uint? SpellSetId = null)
{
    public uint Identity => CraftPersona(SpellId, LayerId);

    public static uint CraftPersona(uint arcanumIdent, uint stratumIdent) =>
        (arcanumIdent & 0xFFFFu) | ((stratumIdent & 0xFFFFu) << 16);
}

public sealed class Grimoire
{
    public const uint CooldownArcanumShift = 0x8000u;
    public const uint CooldownBin = 8u;

    private const uint DefaultFilters = 0x3FFFu;
    private const int FavoriteTabs = 8;
    private const uint VitaeBin = 4u;
    private const uint BeneficialStatMod = 0x02000000u;

    // Active enchantments by identity, plus per-bucket ordering
    private sealed class Ledger
    {
        private readonly Dictionary<uint, LiveEnchantmentRow> _byPersona = [];
        private readonly Dictionary<uint, List<uint>> _orderingByBin = [];

        public int Count => _byPersona.Count;

        public IEnumerable<LiveEnchantmentRow> All => _byPersona.Values;

        public bool TryGet(uint persona, out LiveEnchantmentRow rank) => _byPersona.TryGetValue(persona, out rank);

        public bool BinHasAny(uint bin)
        {
            return _orderingByBin.TryGetValue(bin, out var ordering) && ordering.Count is not 0;
        }

        public IReadOnlyList<uint> Order(uint bin) => _orderingByBin.GetValueOrDefault(bin) ?? (IReadOnlyList<uint>)[];

        // Live updates go to the head of their bucket; manifest rows keep wire order at the tail
        public void Upsert(LiveEnchantmentRow rank, bool atFront)
        {
            uint persona = rank.Identity;
            if (_byPersona.TryGetValue(persona, out LiveEnchantmentRow earlier))
            {
                _byPersona[persona] = rank;
                if (earlier.Bucket == rank.Bucket)
                {
                    List<uint> ordering = RankFor(rank.Bucket);
                    if (!ordering.Contains(persona))
                        Place(ordering, persona, atFront);
                    return;
                }
                Unlist(earlier);
            }
            else
            {
                _byPersona.Add(persona, rank);
            }
            Place(RankFor(rank.Bucket), persona, atFront);
        }

        public bool Remove(uint persona, out LiveEnchantmentRow rank)
        {
            if (!_byPersona.Remove(persona, out rank))
                return false;
            Unlist(rank);
            return true;
        }

        public void Clear()
        {
            _byPersona.Clear();
            _orderingByBin.Clear();
        }

        public void AffixBin(uint bin, List<LiveEnchantmentRow> into)
        {
            foreach (uint persona in Order(bin))
            {
                if (_byPersona.TryGetValue(persona, out LiveEnchantmentRow rank) && rank.Bucket == bin)
                    into.Add(rank);
            }
        }

        private static void Place(List<uint> ordering, uint persona, bool atFront)
        {
            if (atFront)
                ordering.Insert(0, persona);
            else
                ordering.Add(persona);
        }

        private List<uint> RankFor(uint bin)
        {
            if (!_orderingByBin.TryGetValue(bin, out List<uint>? ordering))
                _orderingByBin.Add(bin, ordering = []);
            return ordering;
        }

        private void Unlist(in LiveEnchantmentRow rank)
        {
            if (!_orderingByBin.TryGetValue(rank.Bucket, out List<uint>? ordering))
                return;
            ordering.Remove(rank.Identity);
            if (ordering.Count is 0)
                _orderingByBin.Remove(rank.Bucket);
        }
    }

    private readonly Dictionary<uint, float> _recognized = [];
    private readonly Ledger _register = new();
    private readonly Dictionary<uint, EnchantmentRules.VitalTweak> _vitalStash = [];
    private readonly Dictionary<uint, EnchantmentRules.VitalTweak> _attrStash = [];
    private readonly Dictionary<uint, EnchantmentRules.VitalTweak> _aptitudeStash = [];
    private readonly List<uint>[] _favorites = new List<uint>[FavoriteTabs];
    private readonly Dictionary<uint, uint> _wishlist = [];
    private ArcanumChart _chart;
    private bool _chartInstalled;
    private uint _filters = DefaultFilters;

    public Grimoire(ArcanumChart? chart = null)
    {
        _chart = chart ?? ArcanumChart.Empty;
        _chartInstalled = chart is not null;
        for (int idx = 0; idx < _favorites.Length; ++idx)
            _favorites[idx] = [];
    }

    /// <summary>A spell was added to the spellbook.</summary>
    public event Action<uint>? SpellLearned;

    /// <summary>A spell was removed (respec or admin action).</summary>
    public event Action<uint>? SpellForgotten;

    /// <summary>An enchantment was added or refreshed.</summary>
    public event Action<LiveEnchantmentRow>? EnchantmentAdded;

    /// <summary>An enchantment expired or was dispelled.</summary>
    public event Action<LiveEnchantmentRow>? EnchantmentRemoved;

    public event Action? StateChanged;

    public event Action? SpellbookChanged;

    public event Action? EnchantmentsChanged;

    public event Action? DesiredComponentsChanged;

    public ArcanumChart Metadata => _chart;

    public bool TryFetchMetadata(uint arcanumIdent, out SpellMeta meta) => _chart.TryGet(arcanumIdent, out meta);

    public IReadOnlyCollection<uint> LearnedArcana => _recognized.Keys;

    /// <summary>Known spells with their server power, sorted by id.</summary>
    public IReadOnlyList<KnownSpellRow> LearnedArcanumCapture
    {
        get
        {
            return _recognized.Select(p => new KnownSpellRow(p.Key, p.Value)).OrderBy(s => s.SpellId).ToArray();
        }
    }

    public IEnumerable<LiveEnchantmentRow> ActiveEnchantments => _register.All;

    public IReadOnlyList<LiveEnchantmentRow> EngagedEnchantmentCapture =>
        _register.All.OrderBy(r => r.Identity).ToArray();

    public IReadOnlyList<LiveEnchantmentRow> EnchantmentsInFxCapture
    {
        get
        {
            var sequenced = new List<LiveEnchantmentRow>();
            _register.AffixBin(1u, sequenced);
            _register.AffixBin(2u, sequenced);
            return EnchantmentLedgerView.FetchEnchantmentsInFx(sequenced);
        }
    }

    public IReadOnlyList<uint> FetchFavorites(int tabOrdinal)
    {
        return (uint)tabOrdinal < (uint)_favorites.Length ? _favorites[tabOrdinal].ToArray() : [];
    }

    public IReadOnlyDictionary<uint, uint> DesiredComponents => _wishlist;

    public uint GrimoireFilters => _filters;

    public int LearnedTally => _recognized.Count;

    public int ActiveCount => _register.Count;

    public bool HasCooldownEnchantments => _register.BinHasAny(CooldownBin);

    public bool Knows(uint arcanumIdent) => _recognized.ContainsKey(arcanumIdent);

    public EnchantmentRules.VitalTweak FetchVitalMod(uint statTag)
    {
        if (_vitalStash.TryGetValue(statTag, out EnchantmentRules.VitalTweak recognized))
            return recognized;
        var fresh = EnchantmentRules.FetchMod(
            ActiveEnchantments, _chart, statTag, EnchantmentRules.EnchantmentBit.SecondAtt, includeVitae: true);
        _vitalStash.Add(statTag, fresh);
        return fresh;
    }

    public EnchantmentRules.VitalTweak FetchAttrMod(uint attrIdent)
    {
        if (_attrStash.TryGetValue(attrIdent, out EnchantmentRules.VitalTweak recognized))
            return recognized;
        var fresh = EnchantmentRules.FetchMod(
            ActiveEnchantments, _chart, attrIdent, EnchantmentRules.EnchantmentBit.Attribute, includeVitae: false);
        _attrStash.Add(attrIdent, fresh);
        return fresh;
    }

    public EnchantmentRules.VitalTweak ObtainAptitudeMod(uint aptitudeIdent)
    {
        if (_aptitudeStash.TryGetValue(aptitudeIdent, out EnchantmentRules.VitalTweak recognized))
            return recognized;
        var fresh = EnchantmentRules.FetchAptitudeMod(ActiveEnchantments, _chart, aptitudeIdent);
        _aptitudeStash.Add(aptitudeIdent, fresh);
        return fresh;
    }

    public void SetupMetadata(ArcanumChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (_chartInstalled)
        {
            if (ReferenceEquals(_chart, chart))
                return;
            throw new InvalidOperationException("Spell metadata is by now installed");
        }

        _chart = chart;
        _chartInstalled = true;
        if (_recognized.Count is not 0)
            FireGrimoireAltered();
        if (_register.Count is not 0)
            FireEnchantmentsAltered();
    }

    /// <summary>0x02C1 MagicUpdateSpell.</summary>
    public void OnArcanumLearned(uint arcanumIdent, float strength = 0f)
    {
        if (_recognized.TryAdd(arcanumIdent, strength))
        {
            SpellLearned?.Invoke(arcanumIdent);
            FireGrimoireAltered();
        }
        else if (_recognized[arcanumIdent] != strength)
        {
            _recognized[arcanumIdent] = strength;
            FireGrimoireAltered();
        }
    }

    /// <summary>0x01A8 MagicRemoveSpell.</summary>
    public void OnArcanumForgotten(uint arcanumIdent)
    {
        if (!_recognized.Remove(arcanumIdent))
            return;
        SpellForgotten?.Invoke(arcanumIdent);
        FireGrimoireAltered();
    }

    /// <summary>0x02C2 MagicUpdateEnchantment.</summary>
    public void OnEnchantmentAdded(uint arcanumIdent, uint stratumIdent, float interval, uint invokerOid)
    {
        OnEnchantmentAdded(new LiveEnchantmentRow(arcanumIdent, stratumIdent, interval, invokerOid));
    }

    public void OnEnchantmentAdded(LiveEnchantmentRow capture)
    {
        _register.Upsert(capture, atFront: true);
        EnchantmentAdded?.Invoke(capture);
        FireEnchantmentsAltered();
    }

    public void OnEnchantmentsAdded(IEnumerable<LiveEnchantmentRow> records)
    {
        foreach (LiveEnchantmentRow capture in records)
        {
            _register.Upsert(capture, atFront: true);
            EnchantmentAdded?.Invoke(capture);
        }
        FireEnchantmentsAltered();
    }

    public void OnEnchantmentRemoved(uint stratumIdent, uint arcanumIdent)
    {
        if (!_register.Remove(LiveEnchantmentRow.CraftPersona(arcanumIdent, stratumIdent), out LiveEnchantmentRow capture))
            return;
        EnchantmentRemoved?.Invoke(capture);
        FireEnchantmentsAltered();
    }

    public void OnEnchantmentsRemoved(IEnumerable<(uint SpellId, uint Layer)> arcana)
    {
        bool any = false;
        foreach ((uint arcanumIdent, uint stratum) in arcana)
        {
            if (!_register.Remove(LiveEnchantmentRow.CraftPersona(arcanumIdent, stratum), out LiveEnchantmentRow capture))
                continue;
            any = true;
            EnchantmentRemoved?.Invoke(capture);
        }
        if (any)
            FireEnchantmentsAltered();
    }

    public bool OnCooldown(uint cooldownIdent, double latestMoment, out double leftover)
    {
        leftover = 0d;
        if (cooldownIdent is 0u)
            return false;

        uint cooldownArcanum = unchecked(cooldownIdent + CooldownArcanumShift);
        foreach (uint persona in _register.Order(CooldownBin))
        {
            if (!_register.TryGet(persona, out LiveEnchantmentRow capture) || (capture.SpellId & 0xFFFFu) != cooldownArcanum)
                continue;

            leftover = capture.Duration + capture.StartTime - latestMoment;
            if (leftover > 0d)
                return true;

            _register.Remove(persona, out _);
            EnchantmentRemoved?.Invoke(capture);
            FireEnchantmentsAltered();
            return false;
        }
        return false;
    }

    public void OnPurgeAll() => Purge(static _ => true);

    public void OnPurgeBadEnchantments()
    {
        Purge(static r => (r.StatModType.GetValueOrDefault() & BeneficialStatMod) == 0);
    }

    /// <summary>Replaces everything from the login manifest.</summary>
    public void ReplaceManifest(
        IReadOnlyDictionary<uint, float> learnedArcana,
        IEnumerable<LiveEnchantmentRow> enchantments,
        IReadOnlyList<IReadOnlyList<uint>> favorites,
        IReadOnlyList<(uint Id, uint Amount)> wantedModules,
        uint grimoireFilters)
    {
        _recognized.Clear();
        foreach ((uint arcanumIdent, float strength) in learnedArcana)
            _recognized[arcanumIdent] = strength;

        _register.Clear();
        DiscardStashes();
        foreach (LiveEnchantmentRow rank in enchantments)
        {
            _register.Upsert(rank, atFront: false);
            if (rank.Bucket == VitaeBin)
                AnnounceSigninVitae(rank);
        }

        for (int tab = 0; tab < _favorites.Length; ++tab)
        {
            _favorites[tab].Clear();
            if (tab < favorites.Count)
                _favorites[tab].AddRange(favorites[tab]);
        }

        _wishlist.Clear();
        foreach ((uint ident, uint quantity) in wantedModules)
            _wishlist[ident] = quantity;
        _filters = grimoireFilters;

        SpellbookChanged?.Invoke();
        EnchantmentsChanged?.Invoke();
        DesiredComponentsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void SetFavorite(int tabOrdinal, int locus, uint arcanumIdent)
    {
        if ((uint)tabOrdinal >= (uint)_favorites.Length || locus < 0)
            return;
        List<uint> tab = _favorites[tabOrdinal];
        locus = Math.Min(locus, tab.Count);
        tab.Remove(arcanumIdent);
        tab.Insert(Math.Min(locus, tab.Count), arcanumIdent);
        FireGrimoireAltered();
    }

    public void RemoveFavorite(int tabOrdinal, uint arcanumIdent)
    {
        if ((uint)tabOrdinal < (uint)_favorites.Length && _favorites[tabOrdinal].Remove(arcanumIdent))
            FireGrimoireAltered();
    }

    public void SetSpellbookFilters(uint filters)
    {
        if (_filters == filters)
            return;
        _filters = filters;
        FireGrimoireAltered();
    }

    public void SetDesiredComponent(uint moduleIdent, uint quantity)
    {
        if (_wishlist.TryGetValue(moduleIdent, out uint latest) && latest == quantity)
            return;
        _wishlist[moduleIdent] = quantity;
        DesiredComponentsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void PurgeWantedModules()
    {
        if (_wishlist.Count is 0)
            return;
        _wishlist.Clear();
        DesiredComponentsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        _recognized.Clear();
        _register.Clear();
        DiscardStashes();
        foreach (List<uint> tab in _favorites)
            tab.Clear();
        _wishlist.Clear();
        _filters = DefaultFilters;
        SpellbookChanged?.Invoke();
        EnchantmentsChanged?.Invoke();
        DesiredComponentsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void Purge(Func<LiveEnchantmentRow, bool> alsoFits)
    {
        var gone = _register.All.Where(r => IsPurgeable(r) && alsoFits(r)).ToArray();
        foreach (LiveEnchantmentRow capture in gone)
            _register.Remove(capture.Identity, out _);
        foreach (LiveEnchantmentRow capture in gone)
            EnchantmentRemoved?.Invoke(capture);
        if (gone.Length is not 0)
            FireEnchantmentsAltered();
    }

    // Timed enchantments in buckets 1 and 2 can be purged; permanent ones cannot
    private static bool IsPurgeable(in LiveEnchantmentRow capture) =>
        capture.Bucket is 1u or 2u && capture.Duration != -1f;

    private static void AnnounceSigninVitae(in LiveEnchantmentRow rank)
    {
        string val = rank.StatModValue is { } v ? v.ToString("F4", CultureInfo.InvariantCulture) : "NULL";
        Console.WriteLine(FormattableString.Invariant(
            $"[stat-chain] login vitae installed: spell={rank.SpellId} statModType=0x{rank.StatModType ?? 0:X} key={rank.StatModKey ?? 0} value={val}"));
    }

    private void FireGrimoireAltered()
    {
        SpellbookChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void FireEnchantmentsAltered()
    {
        DiscardStashes();
        EnchantmentsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void DiscardStashes()
    {
        _vitalStash.Clear();
        _attrStash.Clear();
        _aptitudeStash.Clear();
    }
}
