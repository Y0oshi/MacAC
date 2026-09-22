using System.Numerics;

namespace MacAC.Mechanics.Illumination;

public sealed class LightKeeper
{
    /// <summary>D3D fixed-function parity.</summary>
    public const int UpperEngagedLamps = 8;

    public const int UpperLightsPerObject = 8;

    public const int UpperDynamicPtLamps = 7;

    public const int UpperStaticPtLamps = 40;

    public const int UpperGlobalLamps = UpperDynamicPtLamps + UpperStaticPtLamps;

    public const int UpperLightsPerEnvCell = UpperGlobalLamps;

    public const float BeholderLampIntensity = 2.25f;

    public const float BeholderLampFalloff = 10f;

    // Never a real entity id
    private const uint BeholderLampHolderIdent = 0xFFFFFFFFu;

    private static readonly Vector3 BeholderLampLift = new(0f, 0f, 2f);

    private readonly List<LightEmitter> _registered = [];
    private readonly LightEmitter?[] _engaged = new LightEmitter?[UpperEngagedLamps];
    private readonly List<LightEmitter> _ptCapture = [];
    private readonly List<LightEmitter> _dynamicPts = new(UpperDynamicPtLamps);
    private readonly List<LightEmitter> _staticPts = new(UpperStaticPtLamps);
    private int _engagedTally;
    private LightEmitter? _beholderLamp;

    public CellAmbientLight LatestAmbient { get; set; }

    public LightEmitter? Sun { get; set; }

    public ReadOnlySpan<LightEmitter?> Active => _engaged.AsSpan(0, _engagedTally);

    public int ActiveCount => _engagedTally;

    public int RegisteredTally => _registered.Count;

    public IReadOnlyList<LightEmitter> PtCapture => _ptCapture;

    /// <summary>Adds a light; adding the same instance again does nothing.</summary>
    public void Register(LightEmitter light)
    {
        ArgumentNullException.ThrowIfNull(light);
        if (light.Kind != LampFlavor.Directional && !light.HasRankingOrigin)
        {
            throw new ArgumentException(
                "A nondirectional light needs an explicit authored ranking origin",
                nameof(light));
        }
        if (!_registered.Contains(light))
            _registered.Add(light);
    }

    public void Unregister(LightEmitter lamp) => _registered.Remove(lamp);

    public void WithdrawByHolder(uint holderIdent) => _registered.RemoveAll(emitter => emitter.HolderTag == holderIdent);

    public void Clear()
    {
        _registered.Clear();
        Array.Clear(_engaged);
        _engagedTally = 0;
        _beholderLamp = null;
        _ptCapture.Clear();
        _dynamicPts.Clear();
        _staticPts.Clear();
    }

    public void Tick(Vector3 beholderRealmSpot)
    {
        Array.Clear(_engaged);
        int lead = 0;
        if (Sun is not null)
            _engaged[lead++] = Sun;

        int hall = UpperEngagedLamps - lead;
        int kept = 0;
        foreach (LightEmitter lamp in _registered)
        {
            if (!lamp.IsLit || lamp.Kind == LampFlavor.Directional)
                continue;
            lamp.DistanceSq = Vector3.DistanceSquared(lamp.RealmPosition, beholderRealmSpot);
            kept = SlotRanked(_engaged.AsSpan(lead, hall), kept, lamp);
        }

        _engagedTally = lead + kept;
    }

    public void AssemblePtLampCapture(Vector3 avatarRealmSpot)
    {
        _ptCapture.Clear();
        _dynamicPts.Clear();
        _staticPts.Clear();

        if (_beholderLamp is { IsLit: true } beholder)
            Keep(beholder, avatarRealmSpot, _dynamicPts, UpperDynamicPtLamps);

        foreach (LightEmitter lamp in _registered)
        {
            if (ReferenceEquals(lamp, _beholderLamp) || !lamp.IsLit || lamp.Kind == LampFlavor.Directional)
                continue;
            if (lamp.IsDynamic)
                Keep(lamp, avatarRealmSpot, _dynamicPts, UpperDynamicPtLamps);
            else
                Keep(lamp, avatarRealmSpot, _staticPts, UpperStaticPtLamps);
        }

        _ptCapture.AddRange(_dynamicPts);
        _ptCapture.AddRange(_staticPts);
    }

    /// <summary>The always-on white light two metres above the player.</summary>
    public void RefreshBeholderLamp(Vector3 avatarRealmSpot)
    {
        if (_beholderLamp is null)
        {
            _beholderLamp = new LightEmitter
            {
                Kind = LampFlavor.Point,
                OwnPosture = Matrix4x4.CreateTranslation(BeholderLampLift),
                TintLinear = Vector3.One,
                Intensity = BeholderLampIntensity,
                Range = BeholderLampFalloff * 1.5f,
                HolderTag = BeholderLampHolderIdent,
                IsLit = true,
                IsDynamic = true,
            };
            _registered.Add(_beholderLamp);
        }
        _beholderLamp.RankingOrigin = avatarRealmSpot;
        _beholderLamp.RealmPosition = avatarRealmSpot + BeholderLampLift;
    }

    public static int PickForObject(
        IReadOnlyList<LightEmitter> capture,
        Vector3 middle,
        float radius,
        Span<int> outOrdinals)
    {
        int cap = Math.Min(outOrdinals.Length, UpperLightsPerObject);
        if (cap <= 0)
            return 0;

        Span<float> keptDistanceSq = stackalloc float[UpperLightsPerObject];
        int tally = 0;

        for (int li = 0; li < capture.Count; ++li)
        {
            var lamp = capture[li];
            float reach = lamp.Range + radius;
            float dsq = Vector3.DistanceSquared(lamp.RealmPosition, middle);
            if (dsq >= reach * reach)
                continue;

            int at;
            if (tally < cap)
                at = tally++;
            else if (dsq < keptDistanceSq[cap - 1])
                at = cap - 1;
            else
                continue;

            while (at > 0 && keptDistanceSq[at - 1] > dsq)
            {
                keptDistanceSq[at] = keptDistanceSq[at - 1];
                outOrdinals[at] = outOrdinals[at - 1];
                --at;
            }
            keptDistanceSq[at] = dsq;
            outOrdinals[at] = li;
        }
        return tally;
    }

    public static int PickForChamber(IReadOnlyList<LightEmitter> capture, Span<int> outOrdinals)
    {
        int tally = Math.Min(Math.Min(capture.Count, outOrdinals.Length), UpperLightsPerEnvCell);
        for (int idx = 0; idx < tally; ++idx)
            outOrdinals[idx] = idx;
        return tally;
    }

    // Insertion into a distance-sorted window
    private static int SlotRanked(Span<LightEmitter?> pane, int filled, LightEmitter lamp)
    {
        if (pane.Length is 0)
            return 0;

        int at;
        if (filled < pane.Length)
        {
            at = filled++;
        }
        else if (lamp.DistanceSq < pane[^1]!.DistanceSq)
        {
            at = pane.Length - 1;
        }
        else
        {
            return filled;
        }

        while (at > 0 && pane[at - 1]!.DistanceSq > lamp.DistanceSq)
        {
            pane[at] = pane[at - 1];
            --at;
        }
        pane[at] = lamp;
        return filled;
    }

    private static void Keep(LightEmitter lamp, Vector3 avatarRealmSpot, List<LightEmitter> ranked, int cap)
    {
        float grade = lamp.Kind == LampFlavor.Point
            ? Vector3.DistanceSquared(lamp.RankingOrigin, avatarRealmSpot)
            : 0f;
        lamp.DistanceSq = grade;

        int at = 0;
        while (at < ranked.Count && !(grade < ranked[at].DistanceSq))
            ++at;
        if (at >= cap)
            return;

        ranked.Insert(at, lamp);
        if (ranked.Count > cap)
            ranked.RemoveAt(cap);
    }
}
