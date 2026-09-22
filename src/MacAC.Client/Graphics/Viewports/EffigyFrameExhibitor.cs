using MacAC.Dat;
using System.Numerics;
using MacAC.Assets;
using MacAC.Client.Controls;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IEffigyDollPainter
{
    void AssignDoll(RealmActor? doll);

    void Prepare();

    uint Render(int width, int height);
}

internal interface IEffigyFrameView
{
    bool TryFetchVisibleSize(out int width, out int height);

    void AssignTextureHandle(uint textureHnd);

    void WipeTextureHnd();
}

internal interface IEffigyStashVisibility
{
    bool IsVisible { get; }
}

internal interface IEffigyDollMint
{
    bool TryBuild(out RealmActor? doll);
}

internal interface IEffigyActorLookup
{
    bool TryGet(uint srvOid, out RealmActor avatar);
}

internal interface IEffigyPoseApplicator
{
    void Apply(RealmActor doll, uint rigIdent);
}

// Owns paperdoll dirty/rebuild state and the private render-target presentation edge
internal sealed class EffigyFrameExhibitor(
    IEffigyDollPainter renderer,
    IEffigyFrameView view,
    IEffigyDollMint factory) :
    IPrivateActorViewportFrame,
    IPrivateActorViewportResourcePreparation
{
    private readonly IEffigyDollPainter _painter = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IEffigyFrameView _lens = view ?? throw new ArgumentNullException(nameof(view));
    private readonly IEffigyDollMint _maker = factory ?? throw new ArgumentNullException(nameof(factory));
    private RealmActor? _doll;

    internal bool IsStale { get; private set; } = true;

    public void FlagStale() => IsStale = true;

    public void ReadyAssetList()
    {
        if (IsStale)
        {
            if (_maker.TryBuild(out RealmActor? doll))
            {
                _painter.AssignDoll(doll);
                _doll = doll;
                IsStale = false;
            }
            else
            {
            }
        }

        _painter.Prepare();
    }

    public void Render()
    {
        if (!_lens.TryFetchVisibleSize(out int width, out int height))
            return;

        uint textureHnd = _painter.Render(width, height);
        if (textureHnd is not 0u)
            _lens.AssignTextureHandle(textureHnd);
    }

    public void ResetSession()
    {
        _painter.AssignDoll(null);
        _lens.WipeTextureHnd();
        _doll = null;
        IsStale = true;
    }
}

// Retained-UI visibility and texture publication for the doll view
internal sealed class CanonEffigyFrameView(
    WidgetViewport viewport,
    IEffigyStashVisibility inventory) : IEffigyFrameView
{
    private readonly WidgetViewport _viewRect = viewport ?? throw new ArgumentNullException(nameof(viewport));
    private readonly IEffigyStashVisibility _satchel = inventory ?? throw new ArgumentNullException(nameof(inventory));

    public bool TryFetchVisibleSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!_viewRect.Visible || !_satchel.IsVisible)

            return false;

        width = (int)_viewRect.Width;
        height = (int)_viewRect.Height;
        return true;
    }

    public void AssignTextureHandle(uint textureHnd) =>
        _viewRect.TextureSlot = WidgetTextureChartHandle.ToSocket(textureHnd);

    public void WipeTextureHnd() =>
        _viewRect.TextureSlot = GpuTextureSlot.Unassigned;
}

// Narrow visibility adapter for the paperdoll's inventory host
internal sealed class EffigyStashVisibility(WidgetElem inventoryFrame) : IEffigyStashVisibility
{
    private readonly WidgetElem _satchelCycle = inventoryFrame
            ?? throw new ArgumentNullException(nameof(inventoryFrame));

    public bool IsVisible => _satchelCycle.Visible;
}

internal sealed class OnlineEffigyActorLookup(OnlineActorCore liveEntities) : IEffigyActorLookup
{
    private readonly OnlineActorCore _onlineActors = liveEntities
            ?? throw new ArgumentNullException(nameof(liveEntities));

    public bool TryGet(uint srvOid, out RealmActor avatar) =>
        _onlineActors.TryFetchRealmActor(srvOid, out avatar);
}

internal sealed class CanonEffigyDollMint(
    IEffigyActorLookup entities,
    IAvatarIdentitySource identity,
    IEffigyPoseApplicator pose) : IEffigyDollMint
{
    private readonly IEffigyActorLookup _actors = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly IEffigyPoseApplicator _posture = pose ?? throw new ArgumentNullException(nameof(pose));

    public bool TryBuild(out RealmActor? doll)
    {
        doll = null;
        if (!_actors.TryGet(_identity.SrvOid, out RealmActor avatar)
            || avatar.MeshRefs.Count is 0)

            return false;

        uint? baseSwatch = null;
        List<(uint, byte, byte)>? subSwatches = null;
        if (avatar.SwatchOverride is { } swatch)
        {
            baseSwatch = swatch.BasePaletteId;
            subSwatches = [];
            foreach (var span in swatch.SubPalettes)
            {
                subSwatches.Add((
                    span.SubPaletteId,
                    span.Offset,
                    span.Length));
            }
        }

        List<(byte, uint)>? pieceSubstitutions = null;
        if (avatar.PieceSubstitutions.Count > 0)
        {
            pieceSubstitutions = new List<(byte, uint)>(avatar.PieceSubstitutions.Count);
            foreach (var piece in avatar.PieceSubstitutions)
                pieceSubstitutions.Add((piece.PartIndex, piece.GfxObjId));
        }

        doll = DollActorAssembler.Build(
            avatar.SrcGfxObjRefOrRigIdent,
            new List<TriMeshRef>(avatar.MeshRefs),
            baseSwatch,
            subSwatches,
            pieceSubstitutions);
        _posture.Apply(doll, avatar.SrcGfxObjRefOrRigIdent);
        return true;
    }
}

internal sealed class CanonEffigyPoseApplicator(
    IDatAccess dats,
    IAnimReader animations,
    object datLock) : IEffigyPoseApplicator
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly IAnimReader _anims = animations ?? throw new ArgumentNullException(nameof(animations));
    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));

    private uint LocatePostureDid() => CanonHeldPose.LocatePostureDid(_datFiles, 0x10000005u);

    public void Apply(RealmActor doll, uint rigIdent)
    {
        MotionClip? anim;
        RigSpec? rig;
        lock (_datMutex)
        {
            uint postureDid = LocatePostureDid();
            if ((postureDid >> 24) != 0x03u)
                return;

            anim = _anims.PullAnim(postureDid);
            rig = _datFiles.Get<RigSpec>(rigIdent);
        }
        if (anim is null || rig is null || anim.Frames.Count is 0)
            return;

        ApplyRest(anim, rig, doll);
    }

    private void ApplyRest(MotionClip anim, RigSpec rig, RealmActor doll)
    {
        MotionFrame cycle = anim.Frames[^1];
        List<TriMeshRef> reposed = new List<TriMeshRef>(doll.MeshRefs.Count);
        for (int ordinal = 0; ordinal < doll.MeshRefs.Count; ++ordinal)
        {
            Vector3 scaling = ordinal < rig.DefaultScale.Count
                ? rig.DefaultScale[ordinal]
                : Vector3.One;
            Vector3 origin = Vector3.Zero;
            Quaternion facing = Quaternion.Identity;
            if (ordinal < cycle.Poses.Count)
            {
                origin = cycle.Poses[ordinal].Origin;
                facing = cycle.Poses[ordinal].Orientation;
            }

            Matrix4x4 xform = CanonHeldPose.ConstructPieceXform(scaling, origin, facing);
            TriMeshRef src = doll.MeshRefs[ordinal];
            reposed.Add(new TriMeshRef(src.GfxObjId, xform)
            {
                CanvasOverrides = src.CanvasOverrides,
            });
        }
        doll.MeshRefs = reposed;
    }
}
