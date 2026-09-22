using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Illumination;

public sealed class LightingHookTap : IAnimHookTap
{
    private readonly LightKeeper _keeper;
    private readonly IEffectPoseSource _postures;
    private readonly IActorFxChamberOrigin? _chambers;

    private readonly Dictionary<uint, List<LightEmitter>> _possessedByHolder = [];
    private readonly Dictionary<uint, List<LightEmitter>> _postureFollowedByHolder = [];
    private readonly Dictionary<uint, bool> _latchByHolder = [];

    public LightingHookTap(LightKeeper lights, IEffectPoseSource poses)
    {
        _keeper = lights ?? throw new ArgumentNullException(nameof(lights));
        _postures = poses ?? throw new ArgumentNullException(nameof(poses));
        _chambers = poses as IActorFxChamberOrigin;
    }

    public event Action<uint, bool>? OwnerLightingChanged;

    public int PossessedLampHolderTally => _possessedByHolder.Count;

    public int PostureFollowedHolderTally => _postureFollowedByHolder.Count;

    public int KeptHolderPhaseTally => _latchByHolder.Count;

    public void EnrollPossessedLamp(LightEmitter lamp)
    {
        ArgumentNullException.ThrowIfNull(lamp);
        if (_latchByHolder.TryGetValue(lamp.HolderTag, out bool latched))
            lamp.IsLit = latched;

        _keeper.Register(lamp);
        Bin(_possessedByHolder, lamp.HolderTag).Add(lamp);
        if (lamp.TracksHolderPosture)
            Bin(_postureFollowedByHolder, lamp.HolderTag).Add(lamp);
    }

    /// <summary>Drops every light this owner registered (despawn or unload).</summary>
    public void WithdrawHolder(uint holderIdent, bool forgetPhase = true)
    {
        if (_possessedByHolder.Remove(holderIdent, out List<LightEmitter>? possessed))
        {
            foreach (LightEmitter lamp in possessed)
                _keeper.Unregister(lamp);
        }
        _postureFollowedByHolder.Remove(holderIdent);
        if (forgetPhase)
            _latchByHolder.Remove(holderIdent);
    }

    public void BootstrapHolderIllumination(uint holderIdent, bool turnedOn) => _latchByHolder.TryAdd(holderIdent, turnedOn);

    public bool IsHolderIlluminationTurnedOn(uint holderIdent)
    {
        return !_latchByHolder.TryGetValue(holderIdent, out bool turnedOn) || turnedOn;
    }

    public void AssignHolderIllumination(uint holderIdent, bool turnedOn)
    {
        if (_latchByHolder.TryGetValue(holderIdent, out bool latest) && latest == turnedOn)
            return;

        _latchByHolder[holderIdent] = turnedOn;
        if (_possessedByHolder.TryGetValue(holderIdent, out List<LightEmitter>? possessed))
        {
            foreach (LightEmitter lamp in possessed)
                lamp.IsLit = turnedOn;
        }
        OwnerLightingChanged?.Invoke(holderIdent, turnedOn);
    }

    public IReadOnlyList<LightEmitter>? FetchPossessedLamps(uint holderIdent) =>
        _possessedByHolder.GetValueOrDefault(holderIdent);

    /// <summary>Moves pose-tracked lights to wherever their owners are now.</summary>
    public void RenewAffixedLamps()
    {
        foreach ((uint holderIdent, List<LightEmitter> followed) in _postureFollowedByHolder)
        {
            if (!_postures.TryFetchTrunkPosture(holderIdent, out Matrix4x4 trunkRealm))
                continue;

            uint chamberIdent = 0;
            _chambers?.TryFetchChamberIdent(holderIdent, out chamberIdent);

            foreach (LightEmitter lamp in followed)
            {
                Matrix4x4 lampRealm = lamp.OwnPosture * trunkRealm;
                lamp.RankingOrigin = trunkRealm.Translation;
                lamp.RealmPosition = lampRealm.Translation;
                Vector3 ahead = Vector3.TransformNormal(Vector3.UnitY, lampRealm);
                if (ahead.LengthSquared() > 1e-8f)
                    lamp.RealmAhead = Vector3.Normalize(ahead);
                lamp.CellId = chamberIdent;
            }
        }
    }

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        if (tap is SetLightCue setLamp)
            AssignHolderIllumination(actorIdent, setLamp.LightsOn);
    }

    private static List<LightEmitter> Bin(Dictionary<uint, List<LightEmitter>> byHolder, uint holderIdent)
    {
        if (!byHolder.TryGetValue(holderIdent, out List<LightEmitter>? bin))
            byHolder[holderIdent] = bin = [];
        return bin;
    }
}
