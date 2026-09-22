using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Effects;

public sealed class ParticleHookTap : IAnimHookTap
{
    private readonly record struct Mount(
        uint OwnerLocalId,
        int PartIndex,
        Vector3 HookOffsetOrigin,
        Quaternion HookOffsetOrientation,
        uint LogicalId,
        ParticleDrawPass RenderPass);

    private readonly MoteSys _sys;
    private readonly IEffectPoseSource _postures;
    private readonly IActorFxChamberOrigin? _chambers;
    private readonly IActorFxPostureEditOrigin? _postureEdits;

    private readonly Dictionary<(uint Owner, uint Logical), int> _hndByLogical = [];
    private readonly Dictionary<uint, HashSet<int>> _hndsByHolder = [];
    private readonly Dictionary<int, Mount> _mounts = [];
    private readonly Dictionary<uint, ParticleDrawPass> _passByHolder = [];
    private readonly HashSet<uint> _examinationHolders = [];
    private readonly HashSet<uint> _concealedHolders = [];
    private readonly HashSet<uint> _stoppingHolders = [];
    private readonly HashSet<uint> _reportedMisses = [];
    private readonly HashSet<uint> _spatialHolders = [];
    private readonly HashSet<uint> _staleSet = [];
    private readonly List<uint> _staleOrdering = [];
    private readonly List<uint> _renewLot = [];

    public ParticleHookTap(MoteSys system, IEffectPoseSource poses)
    {
        _sys = system ?? throw new ArgumentNullException(nameof(system));
        _postures = poses ?? throw new ArgumentNullException(nameof(poses));
        _chambers = poses as IActorFxChamberOrigin;
        _postureEdits = poses as IActorFxPostureEditOrigin;
        if (_postureEdits is not null)
            _postureEdits.EffectPoseChanged += FlagStale;
        _sys.EmitterDied += Detach;
    }

    public Action<string>? DiagnosticSink { get; set; }

    public int EngagedMappingTally => _mounts.Count;

    public int LogicalSpoutTally => _hndByLogical.Count;

    public int FollowedHolderTally => _hndsByHolder.Count;

    public int RasterizePassHolderTally => _passByHolder.Count;

    public int ExaminationHolderTally => _examinationHolders.Count;

    public int ConcealedExhibitHolderTally => _concealedHolders.Count;

    internal int PreviousRenewHolderTourTally { get; private set; }

    internal int PreviousRenewMappingTourTally { get; private set; }

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        switch (tap)
        {
            case CreateBlockingParticleCue blocking:
                Summon(actorIdent, (uint)blocking.EmitterSpecId, blocking.Offset, unchecked((int)blocking.PartIndex), blocking.EmitterId, blocking: true);
                break;
            case CreateParticleCue build:
                Summon(actorIdent, (uint)build.EmitterSpecId, build.Offset, unchecked((int)build.PartIndex), build.EmitterId, blocking: false);
                break;
            case DestroyParticleCue demolish:
                Retire(actorIdent, demolish.EmitterId, fadeOut: false);
                break;
            case StopParticleCue halt:
                Retire(actorIdent, halt.EmitterId, fadeOut: true);
                break;
        }
    }

    public void AssignActorRasterizePass(uint actorIdent, ParticleDrawPass rasterizePass)
    {
        _passByHolder[actorIdent] = rasterizePass;
        ReapplyVisRule(actorIdent);
    }

    public void WipeActorRasterizePass(uint actorIdent)
    {
        _passByHolder.Remove(actorIdent);
        ReapplyVisRule(actorIdent);
    }

    public void AssignActorExaminationObject(uint actorIdent, bool isExaminationObject)
    {
        if (isExaminationObject)
            _examinationHolders.Add(actorIdent);
        else
            _examinationHolders.Remove(actorIdent);
        ReapplyVisRule(actorIdent);
    }

    public void AssignActorExhibitShown(uint actorIdent, bool shown)
    {
        if (shown)
        {
            _concealedHolders.Remove(actorIdent);
            if (_hndsByHolder.ContainsKey(actorIdent))
                _spatialHolders.Add(actorIdent);
            FlagStale(actorIdent);
            return;
        }

        _concealedHolders.Add(actorIdent);
        _spatialHolders.Remove(actorIdent);
        if (_hndsByHolder.TryGetValue(actorIdent, out HashSet<int>? hnds))
        {
            foreach (int hnd in hnds)
                Reveal(hnd, false);
        }
    }

    public void RefreshAttachedEmitters()
    {
        PreviousRenewHolderTourTally = 0;
        PreviousRenewMappingTourTally = 0;

        _renewLot.Clear();
        if (_postureEdits is null)
        {
            _renewLot.AddRange(_hndsByHolder.Keys);
        }
        else
        {
            _renewLot.AddRange(_staleOrdering);
            _staleOrdering.Clear();
            _staleSet.Clear();
        }

        foreach (uint holder in _renewLot)
        {
            if (!_hndsByHolder.TryGetValue(holder, out HashSet<int>? hnds))
                continue;

            ++PreviousRenewHolderTourTally;
            bool shown = !_concealedHolders.Contains(holder);
            foreach (int hnd in hnds)
            {
                if (!_mounts.TryGetValue(hnd, out Mount mount))
                    continue;
                ++PreviousRenewMappingTourTally;

                if (!TryMooring(holder, mount.PartIndex, mount.HookOffsetOrigin, out Vector3 mooring, out Quaternion spin, out Vector3 holderLocus))
                {
                    Reveal(hnd, false);
                    continue;
                }
                _sys.RefreshSpoutMooring(hnd, mooring, spin);
                _sys.RefreshSpoutHolderLocus(hnd, holderLocus);
                _sys.RefreshSpoutHolderChamber(hnd, ChamberOf(holder));
                Reveal(hnd, shown);
            }
        }
    }

    public void CeaseAllForActor(uint actorIdent, bool fadeOut)
    {
        if (!_stoppingHolders.Add(actorIdent))
            return;

        try
        {
            _spatialHolders.Remove(actorIdent);
            List<Exception>? misses = null;
            if (_hndsByHolder.TryGetValue(actorIdent, out HashSet<int>? hnds))
            {
                foreach (int hnd in hnds.ToArray())
                {
                    if (_mounts.TryGetValue(hnd, out Mount mount) && mount.LogicalId is not 0)
                        _hndByLogical.Remove((actorIdent, mount.LogicalId));

                    bool stopped;
                    try
                    {
                        if (fadeOut)
                            _sys.AssignSpoutSimulationTurnedOn(hnd, true);
                        _sys.HaltSpout(hnd, fadeOut);
                        stopped = true;
                    }
                    catch (Exception problem)
                    {
                        stopped = !_sys.IsSpoutAlive(hnd);
                        (misses ??= []).Add(problem);
                    }
                    if (!stopped)
                        continue;

                    hnds.Remove(hnd);
                    _mounts.Remove(hnd);
                }

                if (hnds.Count is 0 && _hndsByHolder.TryGetValue(actorIdent, out HashSet<int>? still) && ReferenceEquals(still, hnds))
                    _hndsByHolder.Remove(actorIdent);
            }

            WipeActorRasterizePass(actorIdent);
            _examinationHolders.Remove(actorIdent);
            _concealedHolders.Remove(actorIdent);

            if (misses is not null)
                throw new AggregateException($"One or more particle emitters for owner 0x{actorIdent:X8} could not stop cleanly", misses);
        }
        finally
        {
            _stoppingHolders.Remove(actorIdent);
        }
    }

    private void Detach(int hnd)
    {
        if (!_mounts.Remove(hnd, out Mount mount))
            return;
        if (mount.LogicalId is not 0
            && _hndByLogical.TryGetValue((mount.OwnerLocalId, mount.LogicalId), out int latest)
            && latest == hnd)

            _hndByLogical.Remove((mount.OwnerLocalId, mount.LogicalId));
        if (_hndsByHolder.TryGetValue(mount.OwnerLocalId, out HashSet<int>? hnds))
        {
            hnds.Remove(hnd);
            if (hnds.Count is 0)
            {
                _hndsByHolder.Remove(mount.OwnerLocalId);
                _spatialHolders.Remove(mount.OwnerLocalId);
            }
        }
    }

    private void Retire(uint holder, uint logicalIdent, bool fadeOut)
    {
        if (logicalIdent is 0 || !_hndByLogical.TryGetValue((holder, logicalIdent), out int hnd))
            return;
        if (fadeOut)
            _sys.AssignSpoutSimulationTurnedOn(hnd, true);
        else
            _hndByLogical.Remove((holder, logicalIdent));
        _sys.HaltSpout(hnd, fadeOut);
    }

    private void Summon(uint holder, uint spoutDetailsIdent, Pose shift, int pieceOrdinal, uint logicalIdent, bool blocking)
    {
        if (_stoppingHolders.Contains(holder))
        {
            DiagnosticSink?.Invoke($"Particle creation for owner 0x{holder:X8} was ignored while its emitters were stopping.");
            return;
        }

        // A logical id names one emitter per owner; a blocking hook keeps a live one, others replace it.
        if (logicalIdent is not 0 && _hndByLogical.TryGetValue((holder, logicalIdent), out int extant))
        {
            if (blocking && _sys.IsSpoutAlive(extant))
                return;
            _hndByLogical.Remove((holder, logicalIdent));
            _sys.HaltSpout(extant, fadeOut: false);
        }

        Vector3 shiftOrigin = shift?.Origin ?? Vector3.Zero;
        Quaternion shiftFacing = shift?.Orientation ?? Quaternion.Identity;
        if (!TryMooring(holder, pieceOrdinal, shiftOrigin, out Vector3 mooring, out Quaternion spin, out Vector3 holderLocus))
        {
            DiagnosticSink?.Invoke(
                $"No live effect pose for owner 0x{holder:X8}, part {pieceOrdinal}; " +
                $"emitter 0x{spoutDetailsIdent:X8} was not created.");
            return;
        }

        var pass = _passByHolder.GetValueOrDefault(holder, ParticleDrawPass.Scene);
        if (!_sys.TrySpawnEmitterById(spoutDetailsIdent, mooring, spin, holder, pieceOrdinal, pass, RuleFor(holder), out int hnd, out EmitterSpecMiss miss))
        {
            if (_reportedMisses.Add(spoutDetailsIdent))
                DiagnosticSink?.Invoke(DepictMiss(spoutDetailsIdent, holder, miss));
            return;
        }

        _sys.RefreshSpoutHolderLocus(hnd, holderLocus);
        _sys.RefreshSpoutHolderChamber(hnd, ChamberOf(holder));
        bool concealed = _concealedHolders.Contains(holder);
        if (concealed)
            Reveal(hnd, false);

        _mounts[hnd] = new Mount(holder, pieceOrdinal, shiftOrigin, shiftFacing, logicalIdent, pass);
        if (!_hndsByHolder.TryGetValue(holder, out HashSet<int>? hnds))
            _hndsByHolder.Add(holder, hnds = []);
        hnds.Add(hnd);
        if (!concealed)
            _spatialHolders.Add(holder);
        if (logicalIdent is not 0)
            _hndByLogical[(holder, logicalIdent)] = hnd;
    }

    private void Reveal(int hnd, bool shown)
    {
        _sys.AssignSpoutExhibitShown(hnd, shown);
        _sys.AssignSpoutSimulationTurnedOn(hnd, shown);
    }

    private uint ChamberOf(uint holder)
    {
        return _chambers is not null && _chambers.TryFetchChamberIdent(holder, out uint chamberIdent) ? chamberIdent : 0u;
    }

    private ParticleVisibilityRules RuleFor(uint holder)
    {
        if (_examinationHolders.Contains(holder))
            return ParticleVisibilityRules.Examination;
        return _passByHolder.TryGetValue(holder, out ParticleDrawPass pass) && pass != ParticleDrawPass.Scene
            ? ParticleVisibilityRules.PassOwned
            : ParticleVisibilityRules.World;
    }

    private void ReapplyVisRule(uint holder)
    {
        if (!_hndsByHolder.TryGetValue(holder, out HashSet<int>? hnds))
            return;
        var rule = RuleFor(holder);
        foreach (int hnd in hnds)
            _sys.AssignSpoutVisRule(hnd, rule);
    }

    private void FlagStale(uint holder)
    {
        if (_hndsByHolder.ContainsKey(holder) && _staleSet.Add(holder))
            _staleOrdering.Add(holder);
    }

    private bool TryMooring(uint holder, int pieceOrdinal, Vector3 shiftOrigin, out Vector3 mooring, out Quaternion rotation, out Vector3 holderLocus)
    {
        mooring = default;
        rotation = default;
        holderLocus = default;

        if (!_postures.TryFetchTrunkPosture(holder, out Matrix4x4 trunkRealm))
            return false;

        Matrix4x4 cycle = trunkRealm;
        if (pieceOrdinal != -1)
        {
            if (!_postures.TryFetchPiecePosture(holder, pieceOrdinal, out Matrix4x4 pieceOwn))
                return false;
            cycle = pieceOwn * trunkRealm;
        }

        if (!Matrix4x4.Decompose(cycle, out _, out Quaternion spin, out _))
            return false;

        holderLocus = new Vector3(trunkRealm.M41, trunkRealm.M42, trunkRealm.M43);
        mooring = Vector3.Transform(shiftOrigin, cycle);
        rotation = Quaternion.Normalize(spin);
        return true;
    }

    private static string DepictMiss(uint spoutDetailsIdent, uint holder, EmitterSpecMiss miss)
    {
        return miss.Kind switch
        {
            EmitterSpecMissKind.InvalidHardwareGfxObjId =>
                $"ParticleEmitterInfo 0x{spoutDetailsIdent:X8} has no hardware GfxObj; the emitter was skipped (first owner 0x{holder:X8}).",
            EmitterSpecMissKind.MissingHardwareGfxObj =>
                $"ParticleEmitterInfo 0x{spoutDetailsIdent:X8} references missing hardware GfxObj 0x{miss.RelatedDatId:X8}; " +
                $"the emitter was skipped (first owner 0x{holder:X8}).",
            _ => $"ParticleEmitterInfo 0x{spoutDetailsIdent:X8} for owner 0x{holder:X8} was not found; no fallback was created.",
        };
    }
}
