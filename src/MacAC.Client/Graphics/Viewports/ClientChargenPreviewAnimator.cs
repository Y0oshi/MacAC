using MacAC.Dat;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed class ClientChargenPreviewAnimator
{
    public const float IdleFramerate = 30f;

    private readonly ChargenPreviewMovingAssemble _assemble;
    private float _currCycle;
    private readonly List<TriMeshRef> _triMeshRefsBufA = [];
    private readonly List<TriMeshRef> _triMeshRefsBufB = [];
    private bool _upcomingBufIsA = true;

    public ClientChargenPreviewAnimator(ChargenPreviewMovingAssemble build)
    {
        _assemble = build ?? throw new ArgumentNullException(nameof(build));
        _currCycle = build.IdleLoCycle;
        if (build.IdleAnim is not null)
            ImposeIdleCycle();
    }

    public RealmActor Entity => _assemble.Entity;

    public bool IsZoomedIn { get; private set; }

    public void AssignZoomedIn(bool zoomedIn)
    {
        if (IsZoomedIn == zoomedIn)
            return;
        IsZoomedIn = zoomedIn;
        if (zoomedIn)
        {
            _assemble.Entity.MeshRefs = _assemble.RestTriMeshRefs;
        }
        else
        {
            _currCycle = _assemble.IdleLoCycle;
            if (_assemble.IdleAnim is not null)
                ImposeIdleCycle();
        }
    }

    public void Tick(float passedSecs)
    {
        if (IsZoomedIn || _assemble.IdleAnim is null || passedSecs <= 0f)
            return;

        _currCycle = CanonAnimCyclePlayback.Advance(
            _currCycle, _assemble.IdleLoCycle, _assemble.IdleHiCycle, IdleFramerate, passedSecs);
        ImposeIdleCycle();
    }

    private void ImposeIdleCycle()
    {
        MotionClip anim = _assemble.IdleAnim!;
        var pieces = _assemble.DrawablePieces;
        List<TriMeshRef> triMeshRefs = _upcomingBufIsA ? _triMeshRefsBufA : _triMeshRefsBufB;
        _upcomingBufIsA = !_upcomingBufIsA;
        triMeshRefs.Clear();
        foreach (ChargenPreviewDrawablePiece piece in pieces)
        {
            bool settled = CanonAnimCyclePlayback.TryLerpPiece(
                anim, _currCycle, _assemble.IdleLoCycle, _assemble.IdleHiCycle,
                piece.SetupPartIndex, out Vector3 origin, out Quaternion facing);
            if (!settled)
            {
                origin = Vector3.Zero;
                facing = Quaternion.Identity;
            }
            Matrix4x4 xform = CanonHeldPose.ConstructPieceXform(piece.DefaultScale, origin, facing);
            triMeshRefs.Add(new TriMeshRef(piece.GfxObjId, xform) { CanvasOverrides = piece.SurfaceOverrides });
        }
        _assemble.Entity.MeshRefs = triMeshRefs;
    }
}
