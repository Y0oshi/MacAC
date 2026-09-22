using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Paging;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

public sealed class PortalTunnelDisplay : IDisposable
{
    public const uint RigClientEnum = 0x10000001u;
    public const uint AnimClientEnum = 0x10000002u;
    public const uint ClientEnumBucket = 7u;

    internal static readonly Vector4 CanonGatewaySpaceWipeTint = new(0f, 0f, 0f, 1f);

    private const uint SyntheticActorIdent = 0xFFFF_FF01u;
    private const uint SyntheticLbIdent = 0u;
    private const float SpinIntervalLower = 0.6f;
    private const float SpinIntervalUpper = 1.8f;

    private static readonly HashSet<uint> MovingIdents = [SyntheticActorIdent];

    private readonly IRealmPassScope _ambit;

    // The frame this presentation's own pass is opened on
    private readonly ILatestGpuCycleOrigin _cycles;

    private readonly RealmPaintRouter _router;
    private readonly StageLightingUboWiring _lampUbo;
    private readonly RigSpec _rig;
    private readonly AnimTrack _series;
    private readonly PortalMotionHookQueue _animTaps;
    private readonly RealmActor _actor;
    private readonly GatewayTunnelCamera _cam = new();
    private readonly Random _random;
    private readonly Action<string>? _readoutNotice;
    private readonly SyntheticActorMeshReferenceOwner _triMeshReferences;
    private double _spinPassed;
    private double _spinInterval;
    private float _spinBeginAngle;
    private float _spinFinishAngle;
    private float _spinLatestAngle;
    private bool _pauseCueShown;
    private bool _sensorFreezeReached;
    private bool _teardownAsked;
    private bool _disposing;
    private bool _destroyed;

    private PortalTunnelDisplay(
        IRealmPassScope ambit,
        ILatestGpuCycleOrigin cycles,
        RealmPaintRouter router,
        StageLightingUboWiring lampUbo,
        IBatchMeshBridge triMeshBridge,
        RigSpec rig,
        uint rigDid,
        uint animDid,
        IAnimReader animFetcher,
        IAnimHookTap tapDrain,
        Random random,
        Action<string>? readoutNotice)
    {
        _ambit = ambit;
        _cycles = cycles;
        _router = router;
        _lampUbo = lampUbo;
        _rig = rig;
        _rigDid = rigDid;
        _animDid = animDid;
        _series = new AnimTrack(animFetcher);
        _animTaps = new PortalMotionHookQueue(tapDrain, SyntheticActorIdent);
        _series.TapObjRef = _animTaps;
        _random = random;
        _readoutNotice = readoutNotice;

        _actor = new RealmActor
        {
            Id = SyntheticActorIdent,
            SrcGfxObjRefOrRigIdent = rigDid,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            MeshRefs = RigTriMesh.Flatten(rig),
        };

        _triMeshReferences = new SyntheticActorMeshReferenceOwner(
            triMeshBridge,
            rig.PartIds.Select(piece => (ulong)(uint)piece));
    }

    public void Enter()
    {
        HurlIfDestroyed();
        _animTaps.Clear();
        _series.WipeAnims();
        _series.AffixAnim(new ClipRef
        {
            ClipId = (uint)_animDid,
            LowFrame = 1,
            HighFrame = -1,
            Framerate = WarpAnimScheduler.TunnelCyclesPerSecond,
        });

        _spinPassed = 0.0;
        _spinInterval = 0.0;
        _spinBeginAngle = 0f;
        _spinFinishAngle = 0f;
        _spinLatestAngle = 0f;
        _cam.DirDeg = 0f;
        _pauseCueShown = false;
        _sensorFreezeReached = false;
        IsVisible = true;
        ReassemblePosture();
    }

    public void Exit()
    {
        if (_destroyed)
            return;
        IsVisible = false;
        _pauseCueShown = false;
        _sensorFreezeReached = false;
        _animTaps.Clear();
        _series.WipeAnims();
    }

    public bool IsVisible { get; private set; }
    public int LatestAnimCycle => _series.FetchCurrCycleNumber();
    private readonly uint _rigDid;

    public uint RigDid => _rigDid;
    private readonly uint _animDid;

    public uint AnimDid => _animDid;

    public void Tick(float dt)
    {
        if (!IsVisible || dt < 0f)
            return;
        if (_sensorFreezeReached)
            return;

        _series.Update(dt, cycle: null);
        ReassemblePosture();
        _animTaps.Drain(Vector3.Zero);
        PulseSpin(dt);

        if (PagingTelemetry.TunnelFreezeCycle is not { } freezeCycle
            || LatestAnimCycle < freezeCycle)

            return;

        _sensorFreezeReached = true;
        Console.WriteLine(
            $"[tunnel-freeze] frame={LatestAnimCycle} "
            + $"target={freezeCycle} setup=0x{_rigDid:X8} "
            + $"animation=0x{_animDid:X8} state=held");
    }

    public void AssignPauseCue(bool shown) => _pauseCueShown = shown;

    public void Draw(int width, int height, Matrix4x4 smartBboxProj)
    {
        if (!IsVisible || width <= 0 || height <= 0 || _actor.MeshRefs.Count is 0)
            return;

        _cam.Aspect = width / (float)height;
        _cam.EmploySmartBboxFov(smartBboxProj);

        PaintRhi();
    }

    public void Dispose()
    {
        if (_destroyed || _disposing)
            return;

        _teardownAsked = true;
        _disposing = true;
        try
        {
            IsVisible = false;
            _pauseCueShown = false;
            _animTaps.Clear();
            _series.WipeAnims();
            _triMeshReferences.Dispose();
            if (_triMeshReferences.IsDisposed)

                _destroyed = true;
        }
        finally
        {
            _disposing = false;
        }
    }

    internal static PortalTunnelDisplay BuildNeeded(
        IRealmPassScope ambit,
        ILatestGpuCycleOrigin cycles,
        IDatAccess datFiles,
        IAnimReader animFetcher,
        IAnimHookTap tapDrain,
        RealmPaintRouter router,
        StageLightingUboWiring lampUbo,
        IBatchMeshBridge triMeshBridge,
        Action<string>? readoutNotice = null,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(ambit);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(animFetcher);
        ArgumentNullException.ThrowIfNull(tapDrain);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(lampUbo);
        ArgumentNullException.ThrowIfNull(triMeshBridge);

        uint rigDid = CanonDataIdResolver.Resolve(datFiles, RigClientEnum, ClientEnumBucket);
        uint animDid = CanonDataIdResolver.Resolve(datFiles, AnimClientEnum, ClientEnumBucket);
        RigSpec? rig = rigDid is 0u ? null : datFiles.Get<RigSpec>(rigDid);
        MotionClip? anim = animDid is 0u
            ? null
            : animFetcher.PullAnim(animDid);

        SecureNeededHoldings(
            rigDid,
            rig is not null,
            animDid,
            anim is not null);

        return new PortalTunnelDisplay(
            ambit,
            cycles,
            router,
            lampUbo,
            triMeshBridge,
            rig!,
            rigDid,
            animDid,
            animFetcher,
            tapDrain,
            random ?? Random.Shared,
            readoutNotice);
    }

    internal static void SecureNeededHoldings(
        uint rigDid,
        bool rigFetched,
        uint animDid,
        bool animFetched)
    {
        if (rigFetched && animFetched)
            return;

        throw new InvalidOperationException(
            "[portal-space] needed retail DAT assets not available: "
            + $"setup=0x{rigDid:X8} ({(rigFetched ? "ok" : "missing")}), "
            + $"animation=0x{animDid:X8} ({(animFetched ? "ok" : "missing")})");
    }
    // Publishes the synthetic scene's mesh ownership after the presentation itself has been stored by
    // GameWindow
    internal void ReadyAssetList()
    {
        HurlIfDestroyed();
        _triMeshReferences.Acquire();
    }

    private void PaintRhi()
    {
        IGpuCycle cycle = _cycles.LatestCycle
            ?? throw new InvalidOperationException(
                "Portal space needs an open IGpuCycle (see GpuDeviceCycleLifespan)");

        using var coder = cycle.BeginPass(
            GpuPassSpec.BackbufferWipe(
                "portal-space",
                CanonGatewaySpaceWipeTint,
                _ambit.SampleCount));
        using IDisposable bulletin = _ambit.Publish(coder);
        PaintTableau();
    }

    private void PaintTableau()
    {
        PushCanonLamp();
        _router.AssignTableauLamps(null);

        var listings = new (uint, Vector3, Vector3, IReadOnlyList<RealmActor>, IReadOnlyDictionary<uint, RealmActor>?)[]
        {
            (SyntheticLbIdent,
                new Vector3(-16f, -16f, -16f),
                new Vector3(16f, 16f, 16f),
                new RealmActor[] { _actor },
                null),
        };

        _router.Draw(
            _cam,
            listings,
            frustum: null,
            neverPruneLbIdent: SyntheticLbIdent,
            shownChamberIdents: null,
            movingActorIdents: MovingIdents);
    }

    private void PulseSpin(float dt)
    {
        _spinPassed += dt;
        if (_spinPassed >= _spinInterval)
        {
            _spinLatestAngle = _spinFinishAngle;
            _spinPassed = 0.0;
            _spinInterval = UpcomingDouble(SpinIntervalLower, SpinIntervalUpper);
            _spinBeginAngle = _spinLatestAngle;
            _spinFinishAngle = (float)UpcomingDouble(0.0, 360.0);
            _readoutNotice?.Invoke("In Portal Space - Please Wait...");
        }
        else
        {
            float t = _spinInterval <= 0.0
                ? 1f
                : (float)(_spinPassed / _spinInterval);
            float tier = WarpAnimScheduler.FetchCanonAnimTier(t) / 1024f;
            _spinLatestAngle = _spinBeginAngle
                + ((_spinFinishAngle - _spinBeginAngle) * tier);
        }

        _cam.DirDeg = _spinLatestAngle;
    }

    private double UpcomingDouble(double lower, double upper) => lower + (_random.NextDouble() * (upper - lower));

    private void ReassemblePosture()
    {
        var cycle = _series.FetchCurrAnimframe();
        _actor.MeshRefs = RigTriMesh.Flatten(_rig, cycle);
    }

    private void PushCanonLamp()
    {
        Vector3 dir = Vector3.Normalize(new Vector3(0.3f, -1.9f, 0.65f));
        _lampUbo.Upload(new SceneLightBlock
        {
            Light0 = new PackedLight
            {
                SpotAndSort = Vector4.Zero,
                DirectionAndSpan = new Vector4(dir, 1e9f),
                TintAndIntensity = new Vector4(1f, 1f, 1f, 2f),
                ConeAngleEtc = Vector4.Zero,
            },
            ChamberAmbient = new Vector4(0.3f, 0.3f, 0.3f, 1f),
            FogParams = new Vector4(1e9f, 1e9f, 0f, 0f),
            FogColor = Vector4.Zero,
            CamAndMoment = new Vector4(GatewayTunnelCamera.CanonEyePt, 0f),
        });
    }

    private sealed class PortalMotionHookQueue(IAnimHookTap drain, uint holderIdent) : IAnimTapFifo
    {
        private readonly IAnimHookTap _drain = drain;
        private readonly uint _holderIdent = holderIdent;
        private readonly List<Cue> _queued = [];

        public void AppendAnimTap(Cue tap) => _queued.Add(tap);

        public void AppendAnimDoneTap()
        {
        }

        public void Drain(Vector3 realmLocus)
        {
            for (int idx = 0; idx < _queued.Count; ++idx)
                _drain.OnTap(_holderIdent, realmLocus, _queued[idx]);
            _queued.Clear();
        }

        public void Clear() => _queued.Clear();
    }

    private void HurlIfDestroyed() =>
        ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);
}
