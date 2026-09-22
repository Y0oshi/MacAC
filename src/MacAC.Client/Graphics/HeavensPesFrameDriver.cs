using System.Numerics;
using MacAC.Client.Graphics.Effects;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed class HeavensPesFrameDriver(
    KineticScriptRunner scripts,
    ParticleHookTap particles,
    ActorEffectPoseRegistry poses,
    ActorEffectDriver? fxList,
    Action<string>? probe = null,
    Action<uint>? haltSound = null)
{
    private static readonly Matrix4x4[] PersonaPiecePosture =
        [Matrix4x4.Identity];

    private readonly record struct HeavensPesKey(
        int ObjectIndex,
        uint GfxObjId,
        uint Properties);

    private readonly KineticScriptRunner _programs = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private readonly ParticleHookTap _motes = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly ActorEffectPoseRegistry _postures = poses ?? throw new ArgumentNullException(nameof(poses));
    private readonly ActorEffectDriver? _fxList = fxList;
    private readonly Action<uint>? _haltSound = haltSound;
    private readonly HashSet<HeavensPesKey> _engaged = [];
    private readonly HashSet<HeavensPesKey> _absent = [];
    private readonly HashSet<uint> _reportedProgramMismatches = [];
    private readonly HashSet<HeavensPesKey> _observedTemp = [];
    private readonly List<HeavensPesKey> _haltTemp = [];
    private readonly Action<string>? _diagnostic = probe;

    public void Update(
        float dayRatio,
        DayGroupRow? dayCluster,
        Vector3 camRealmLocus,
        bool heavensEngaged = true)
    {
        if (!heavensEngaged)
        {
            AssignEngaged(false);
            return;
        }

        _observedTemp.Clear();
        if (dayCluster is not null)
        {
            for (int ordinal = 0; ordinal < dayCluster.SkyObjects.Count; ++ordinal)
            {
                var heavensObject = dayCluster.SkyObjects[ordinal];
                if (LocateProgramIdent(heavensObject) is 0
                    || !heavensObject.IsVisible(dayRatio))

                    continue;

                _observedTemp.Add(new HeavensPesKey(
                    ordinal,
                    heavensObject.GfxObjId,
                    heavensObject.Properties));
            }
        }

        HaltUnseen(_engaged, haltPrograms: true);
        HaltUnseen(_absent, haltPrograms: false);

        if (dayCluster is null)
            return;

        for (int ordinal = 0; ordinal < dayCluster.SkyObjects.Count; ++ordinal)
        {
            var heavensObject = dayCluster.SkyObjects[ordinal];
            uint programIdent = LocateProgramIdent(heavensObject);
            if (programIdent is 0 || !heavensObject.IsVisible(dayRatio))
                continue;

            HeavensPesKey tag = new HeavensPesKey(
                ordinal,
                heavensObject.GfxObjId,
                heavensObject.Properties);
            uint holderIdent = ActorIdent(tag);
            ParticleDrawPass rasterizePass = heavensObject.IsPostTableau
                ? ParticleDrawPass.SkyPostScene
                : ParticleDrawPass.SkyPreScene;
            _motes.AssignActorRasterizePass(holderIdent, rasterizePass);
            _programs.AssignHolderMooring(holderIdent, camRealmLocus);
            Quaternion spin = Spin(heavensObject, dayRatio);
            _postures.Publish(
                holderIdent,
                Matrix4x4.CreateFromQuaternion(spin)
                    * Matrix4x4.CreateTranslation(camRealmLocus),
                PersonaPiecePosture,
                chamberIdent: 0u);

            if (_engaged.Contains(tag) || _absent.Contains(tag))
                continue;

            _fxList?.EnrollSyntheticHolder(holderIdent);
            if (_programs.Play(programIdent, holderIdent, camRealmLocus))
            {
                _engaged.Add(tag);
            }
            else
            {
                _absent.Add(tag);
                _fxList?.WithdrawSyntheticHolder(holderIdent);
                _motes.WipeActorRasterizePass(holderIdent);
                _postures.Delete(holderIdent);
            }
        }
    }

    public void AssignEngaged(bool heavensEngaged)
    {
        if (heavensEngaged)
            return;

        _observedTemp.Clear();
        HaltUnseen(_engaged, haltPrograms: true);
        HaltUnseen(_absent, haltPrograms: false);
    }

    private void HaltUnseen(HashSet<HeavensPesKey> set, bool haltPrograms)
    {
        _haltTemp.Clear();
        foreach (HeavensPesKey tag in set)
        {
            if (!_observedTemp.Contains(tag))
                _haltTemp.Add(tag);
        }

        foreach (HeavensPesKey tag in _haltTemp)
        {
            if (haltPrograms)
            {
                uint holderIdent = ActorIdent(tag);
                _programs.HaltAllForActor(holderIdent);
                _fxList?.WithdrawSyntheticHolder(holderIdent);
                _motes.CeaseAllForActor(holderIdent, fadeOut: true);
                _postures.Delete(holderIdent);
                _haltSound?.Invoke(holderIdent);
            }

            set.Remove(tag);
        }
    }

    private uint LocateProgramIdent(SkyObjectRow heavensObject)
    {
        uint programIdent = heavensObject.DefaultProgramIdent;
        if (programIdent != heavensObject.PesObjectId
            && heavensObject.GfxObjId is not 0
            && _reportedProgramMismatches.Add(heavensObject.GfxObjId))
        {
            _diagnostic?.Invoke(
                $"[sky-pes] carrier 0x{heavensObject.GfxObjId:X8}: Setup DefaultScript " +
                $"0x{programIdent:X8} != SkyObject pes_id 0x{heavensObject.PesObjectId:X8}; " +
                "playing the DefaultScript (retail's source).");
        }

        return programIdent;
    }

    private static uint ActorIdent(HeavensPesKey tag)
    {
        uint postTableau = (tag.Properties & 0x01u) is not 0u ? 0x08000000u : 0u;
        return 0xF0000000u
            | postTableau
            | ((uint)tag.ObjectIndex & 0x07FFFFFFu);
    }

    private static Quaternion Spin(
        SkyObjectRow heavensObject,
        float dayRatio)
    {
        float radians = heavensObject.LatestAngle(dayRatio) * (MathF.PI / 180f);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -radians);
    }
}
