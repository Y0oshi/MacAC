using System.Numerics;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Effects;

public sealed class ActorEffectPoseRegistry :
    IEffectPoseSource,
    IActorFxChamberOrigin,
    IActorFxPostureEditOrigin,
    IActorEffectPoseLifetimeSource
{
    private sealed class PostureCapture
    {
        public Matrix4x4 TrunkRealm;
        public Matrix4x4[] PieceOwn = [];
        public bool[] PieceAvailable = [];
        public uint CellId;
        public ulong LifespanVer;
        public ulong EditVer;
    }

    private readonly Dictionary<uint, PostureCapture> _postures = [];
    private ulong _upcomingLifespanVer;

    public event Action<uint>? EffectPoseChanged;

    public int Count => _postures.Count;

    public void Publish(RealmActor actor, IReadOnlyList<Matrix4x4> pieceOwn) => Publish(actor, pieceOwn, readiness: null);

    public void Publish(
        RealmActor actor,
        IReadOnlyList<Matrix4x4> pieceOwn,
        IReadOnlyList<bool>? readiness)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Publish(
            actor.Id,
            Matrix4x4.CreateFromQuaternion(actor.Rotation)
                * Matrix4x4.CreateTranslation(actor.Position),
            pieceOwn,
            actor.VisChamberIdent ?? 0u,
            readiness);
    }

    public void BroadcastTriMeshRefs(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Matrix4x4 trunkRealm = Matrix4x4.CreateFromQuaternion(actor.Rotation)
            * Matrix4x4.CreateTranslation(actor.Position);

        bool altered;
        if (_postures.TryGetValue(actor.Id, out PostureCapture? capture))
        {
            altered = capture.TrunkRealm != trunkRealm
                || capture.CellId != (actor.VisChamberIdent ?? 0u);
        }
        else
        {
            capture = new PostureCapture { LifespanVer = UpcomingLifespanVer() };
            _postures.Add(actor.Id, capture);
            altered = true;
        }

        capture.TrunkRealm = trunkRealm;
        capture.CellId = actor.VisChamberIdent ?? 0u;
        if (actor.IndexedPieceXforms.Count > 0)
        {
            altered |= DuplicatePieces(
                capture,
                actor.IndexedPieceXforms,
                actor.IndexedPieceOnHand);
        }
        else
        {
            altered = BroadcastTriMeshRefsBranch(capture, actor, altered);
        }

        if (altered)
        {
            capture.EditVer++;
            EffectPoseChanged?.Invoke(actor.Id);
        }
    }

    private bool BroadcastTriMeshRefsBranch(PostureCapture capture, RealmActor actor, bool altered)
    {
        if (capture.PieceOwn.Length != actor.MeshRefs.Count)
        {
            capture.PieceOwn = new Matrix4x4[actor.MeshRefs.Count];
            altered = true;
        }
        if (capture.PieceAvailable.Length != actor.MeshRefs.Count)
        {
            capture.PieceAvailable = new bool[actor.MeshRefs.Count];
            altered = true;
        }
        for (int idx = 0; idx < actor.MeshRefs.Count; ++idx)
        {
            altered |= capture.PieceOwn[idx] != actor.MeshRefs[idx].PartTransform
                || !capture.PieceAvailable[idx];
            capture.PieceOwn[idx] = actor.MeshRefs[idx].PartTransform;
            capture.PieceAvailable[idx] = true;
        }

        return altered;
    }

    public void Publish(
        uint ownActorIdent,
        Matrix4x4 trunkRealm,
        IReadOnlyList<Matrix4x4> pieceOwn,
        uint chamberIdent,
        IReadOnlyList<bool>? readiness = null)
    {
        if (ownActorIdent is 0)
            return;
        ArgumentNullException.ThrowIfNull(pieceOwn);

        bool altered;
        if (_postures.TryGetValue(ownActorIdent, out PostureCapture? capture))
        {
            altered = capture.TrunkRealm != trunkRealm || capture.CellId != chamberIdent;
        }
        else
        {
            capture = new PostureCapture { LifespanVer = UpcomingLifespanVer() };
            _postures.Add(ownActorIdent, capture);
            altered = true;
        }

        capture.TrunkRealm = trunkRealm;
        capture.CellId = chamberIdent;
        altered |= DuplicatePieces(capture, pieceOwn, readiness);
        if (altered)
        {
            capture.EditVer++;
            EffectPoseChanged?.Invoke(ownActorIdent);
        }
    }

    public bool RefreshTrunk(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!_postures.TryGetValue(actor.Id, out PostureCapture? capture))
            return false;
        Matrix4x4 trunkRealm = Matrix4x4.CreateFromQuaternion(actor.Rotation)
            * Matrix4x4.CreateTranslation(actor.Position);
        uint chamberIdent = actor.VisChamberIdent ?? 0u;
        if (capture.TrunkRealm == trunkRealm && capture.CellId == chamberIdent)
            return true;

        capture.TrunkRealm = trunkRealm;
        capture.CellId = chamberIdent;
        capture.EditVer++;
        EffectPoseChanged?.Invoke(actor.Id);
        return true;
    }

    public bool Delete(uint ownActorIdent)
    {
        if (!_postures.Remove(ownActorIdent))
            return false;
        EffectPoseChanged?.Invoke(ownActorIdent);
        return true;
    }

    public void Clear()
    {
        if (_postures.Count is 0)
            return;

        uint[] removedHolders = [.. _postures.Keys];
        _postures.Clear();
        foreach (uint holder in removedHolders)
        {
            EffectPoseChanged?.Invoke(holder);
        }
    }

    public bool TryFetchTrunkPosture(uint ownActorIdent, out Matrix4x4 trunkRealm)
    {
        if (_postures.TryGetValue(ownActorIdent, out PostureCapture? capture))
        {
            trunkRealm = capture.TrunkRealm;
            return true;
        }
        trunkRealm = default;
        return false;
    }

    public bool TryFetchPiecePosture(uint ownActorIdent, int pieceOrdinal, out Matrix4x4 pieceOwn)
    {
        if (pieceOrdinal >= 0
            && _postures.TryGetValue(ownActorIdent, out PostureCapture? capture)
            && pieceOrdinal < capture.PieceOwn.Length
            && capture.PieceAvailable[pieceOrdinal])
        {
            pieceOwn = capture.PieceOwn[pieceOrdinal];
            return true;
        }
        pieceOwn = default;
        return false;
    }

    public bool TryFetchPiecePostures(
        uint ownActorIdent,
        out IReadOnlyList<Matrix4x4> pieceOwn)
    {
        if (_postures.TryGetValue(ownActorIdent, out PostureCapture? capture))
        {
            pieceOwn = capture.PieceOwn;
            return true;
        }
        pieceOwn = Array.Empty<Matrix4x4>();
        return false;
    }

    public bool TryFetchPiecePostureCapture(
        uint ownActorIdent,
        out IReadOnlyList<Matrix4x4> pieceOwn,
        out IReadOnlyList<bool> readiness)
    {
        if (_postures.TryGetValue(ownActorIdent, out PostureCapture? capture))
        {
            pieceOwn = capture.PieceOwn;
            readiness = capture.PieceAvailable;
            return true;
        }
        pieceOwn = Array.Empty<Matrix4x4>();
        readiness = Array.Empty<bool>();
        return false;
    }

    public bool TryFetchChamberIdent(uint ownActorIdent, out uint chamberIdent)
    {
        if (_postures.TryGetValue(ownActorIdent, out PostureCapture? capture))
        {
            chamberIdent = capture.CellId;
            return true;
        }
        chamberIdent = 0;
        return false;
    }

    public ulong FetchPostureHolderLifespanVer(uint ownActorIdent)
    {
        return _postures.TryGetValue(ownActorIdent, out PostureCapture? capture)
            ? capture.LifespanVer
            : 0UL;
    }

    public ulong FetchPostureEditVer(uint ownActorIdent)
    {
        return _postures.TryGetValue(ownActorIdent, out PostureCapture? capture)
            ? capture.EditVer
            : 0UL;
    }

    private ulong UpcomingLifespanVer()
    {
        ++_upcomingLifespanVer;
        if (_upcomingLifespanVer is 0UL)
            ++_upcomingLifespanVer;
        return _upcomingLifespanVer;
    }

    private static bool DuplicatePieces(
        PostureCapture capture,
        IReadOnlyList<Matrix4x4> pieceOwn,
        IReadOnlyList<bool>? readiness)
    {
        if (readiness is not null && readiness.Count != pieceOwn.Count)
            throw new ArgumentException("Part pose and availability counts must match");
        bool altered = false;
        if (capture.PieceOwn.Length != pieceOwn.Count)
        {
            capture.PieceOwn = new Matrix4x4[pieceOwn.Count];
            altered = true;
        }
        if (capture.PieceAvailable.Length != pieceOwn.Count)
        {
            capture.PieceAvailable = new bool[pieceOwn.Count];
            altered = true;
        }
        for (int idx = 0; idx < pieceOwn.Count; ++idx)
        {
            altered = DuplicatePiecesLoop(readiness, idx, altered, capture, pieceOwn);
        }
        return altered;
    }

    private static bool DuplicatePiecesLoop(IReadOnlyList<bool>? readiness, int idx, bool altered, PostureCapture capture, IReadOnlyList<Matrix4x4> pieceOwn)
    {
        bool pieceOnHand = readiness is null || readiness[idx];
        altered |= capture.PieceOwn[idx] != pieceOwn[idx]
                        || capture.PieceAvailable[idx] != pieceOnHand;
        capture.PieceOwn[idx] = pieceOwn[idx];
        capture.PieceAvailable[idx] = pieceOnHand;
        return altered;
    }
}

public interface IActorEffectPoseLifetimeSource
{
    ulong FetchPostureHolderLifespanVer(uint ownActorIdent);
}
