using System.Globalization;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed class VitaeWidgetDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x21000020u;
    public const uint RootId = 0x100001C1u;
    public const uint PrimaryWordingIdent = 0x100001C3u;
    public const uint CloseId = 0x100000FCu;

    internal const uint VitaeCpReservoirProp = 0x81u;
    internal const uint DeathTierProp = 0x8Bu;
    internal const uint TierProp = 0x19u;

    private readonly Grimoire _grimoire;
    private readonly ClientThingChart _objects;
    private readonly Func<uint> _avatarOid;
    private readonly VitaeTexts _texts;
    private readonly WidgetPhrase _primaryPhrase;
    private readonly WidgetBtn? _shut;
    private IReadOnlyList<WidgetPhrase.Line> _strokes = Array.Empty<WidgetPhrase.Line>();
    private bool _destroyed;

    private VitaeWidgetDriver(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        ClientThingChart objects,
        Func<uint> avatarOid,
        VitaeTexts texts,
        Action? shut)
    {
        _grimoire = grimoire;
        _objects = objects;
        _avatarOid = avatarOid;
        _texts = texts;
        _primaryPhrase = (WidgetPhrase)arrangement.SeekElem(PrimaryWordingIdent)!;
        _shut = arrangement.SeekElem(CloseId) as WidgetBtn;
        _primaryPhrase.StrokesSupplier = () => _strokes;
        _shut?.OnClick = shut;

        _grimoire.EnchantmentsChanged += Update;
        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.Cleared += Update;
        Update();
    }

    public static VitaeWidgetDriver? Bind(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        ClientThingChart objects,
        Func<uint> avatarOid,
        VitaeTexts texts,
        Action? shut = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(grimoire);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(avatarOid);
        ArgumentNullException.ThrowIfNull(texts);
        return arrangement.SeekElem(PrimaryWordingIdent) is WidgetPhrase
            ? new VitaeWidgetDriver(
                arrangement, grimoire, objects, avatarOid, texts, shut)
            : null;
    }

    public void OnShown() => Update();

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _grimoire.EnchantmentsChanged -= Update;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.Cleared -= Update;
        _shut?.OnClick = null;
    }

    internal static int VitaeCpReservoirThreshold(float vitae, int tier)
    {
        return (int)(((Math.Pow(tier, 2.5d) * 2.5d) + 20d)
                     * Math.Pow(vitae, 5d)
                     + 0.5d);
    }

    internal static string ComposeExperience(int experience)
        => experience.ToString("N0", CultureInfo.InvariantCulture);

    private void OnObjectAltered(ClientThing gear)
    {
        if (gear.ObjectId == _avatarOid()) Update();
    }

    private void Update()
    {
        float vitae = LatestVitae();
        int lostPct = 100 - (int)(vitae * 100f);
        string corpus;
        if (lostPct <= 0)
        {
            corpus = _texts.FullStrength;
        }
        else
        {
            var avatar = _objects.Get(_avatarOid());
            int reservoir = avatar?.Properties.FetchInt(VitaeCpReservoirProp) ?? 0;
            int tier = 0;
            if (avatar is not null)
            {
                tier = avatar.Properties.Ints.TryGetValue(
                    DeathTierProp, out int deathTier)
                    ? deathTier
                    : avatar.Properties.FetchInt(TierProp);
            }
            int leftover = VitaeCpReservoirThreshold(vitae, tier) - reservoir;
            corpus = _texts.LostPrefix
                   + lostPct
                   + _texts.LostSuffix
                   + _texts.SkillsPrefix
                   + lostPct
                   + _texts.SkillsSuffix
                   + _texts.RecoveryPrefix
                   + ComposeExperience(leftover)
                   + _texts.RecoverySuffix;
        }
        _strokes = IndicatorSpecificsPhrase.Shape(_primaryPhrase, corpus);
    }

    private float LatestVitae()
    {
        return _grimoire.EngagedEnchantmentCapture
                .Where(capture => capture.Bucket == 4u)
                .Select(capture => capture.StatModValue)
                .OfType<float>()
                .Where(float.IsFinite)
                .DefaultIfEmpty(1f)
                .Last();
    }
}

public sealed record VitaeTexts(
    string FullStrength,
    string LostPrefix,
    string LostSuffix,
    string SkillsPrefix,
    string SkillsSuffix,
    string RecoveryPrefix,
    string RecoverySuffix)
{
    public static VitaeTexts English { get; } = new(
        "Your Vitae, or life force, is at full strength.",
        "Due to your recent death, you have temporarily lost ",
        "% of your Vitae, or life force.",
        "\n\nThis means that your health, stamina, mana, and skills are temporarily reduced by ",
        "%.  A reduction of less than 15% will not hinder you much, but beware losing much more than that.",
        "\n\nYou will regain 1% of your Vitae once you earn ",
        " more experience.");
}
