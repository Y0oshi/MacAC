using System.Numerics;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed partial class PrivateActorViewportPainter
{
    public bool TextureIsBottomUp => false;

    public void AssignActor(RealmActor? actor)
    {
        _primarySocket.Set(actor);
        if (actor is null)

            _flightMarks.DirtyFinishedScenes();
    }

    public void ApplyBackdrop(RealmActor? actor)
    {
        if (_backdropSocket is null)
        {
            throw new InvalidOperationException(
                $"The {_probeLabel} wasn't constructed with a "
                + "backdropRenderId and can't render a second (backdrop) entity");
        }

        _backdropSocket.Set(actor);
    }

    public bool Prepare()
    {
        if (!_primarySocket.ReadyForPaint()
            || !(_backdropSocket?.ReadyForPaint() ?? true))

            return false;
        var actor = _primarySocket.Entity;
        if (actor is null || actor.MeshRefs.Count is 0)

            return false;
        var actors = AssemblePaintActors(
            _backdropSocket?.Entity,
            actor);
        return _router.ReadyPrivateActorAssetList(actors);
    }

    public uint Render(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0u;

        IGpuCycle cycle = _cycles.LatestCycle
            ?? throw new InvalidOperationException(
                $"The {_probeLabel} needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
        int cycleSocket = cycle.SocketOrdinal;

        bool primaryPrimed = _primarySocket.ReadyForPaint();
        bool backdropPrimed = _backdropSocket?.ReadyForPaint() ?? true;
        if (!primaryPrimed || !backdropPrimed)
        {
            return _primarySocket.Entity is not null
                    ? _flightMarks.FinishedHnd(cycleSocket)
                    : 0u;
        }

        var actor = _primarySocket.Entity;
        if (actor is null || actor.MeshRefs.Count is 0)
            return 0u;

        var paintActors = AssemblePaintActors(
            _backdropSocket?.Entity,
            actor);
        if (!_router.ReadyPrivateActorAssetList(paintActors))
            return _flightMarks.FinishedHnd(cycleSocket);

        var markSocket =
            _flightMarks.Secure(cycleSocket, width, height);
        if (markSocket is null)
            return 0u;
        _cam.Aspect = width / (float)height;

        using var coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = _probeLabel,
            Color = new GpuTintAffix(
                Target: markSocket.Target,
                Load: GpuPullOp.Clear,
                Store: GpuVaultOp.Store,
                ClearColor: Vector4.Zero),
            ZDepth = new GpuZDepthAffix(
                Load: GpuPullOp.Clear,
                Store: GpuVaultOp.DontCare,
                ClearDepth: 1f,
                ClearStencil: 0),
            SampleCount = 1,
        });

        using IDisposable bulletin = _ambit.Publish(coder);

        PushBeastLamp();

        var listings =
            new (uint, Vector3, Vector3, IReadOnlyList<RealmActor>,
                IReadOnlyDictionary<uint, RealmActor>?)[]
            {
                (
                    PrivateLbIdent,
                    new Vector3(-1024f),
                    new Vector3(1024f),
                    paintActors,
                    null),
            };

        _router.UpcomingClassicPaintIsPrivatePass = true;
        _router.Draw(
            _cam,
            listings,
            frustum: null,
            neverPruneLbIdent: PrivateLbIdent,
            shownChamberIdents: null,
            movingActorIdents: _movingIdents);
        markSocket.HasRenderedTableau = true;
        return WidgetTextureChartHandle.FromSocket(markSocket.TextureSlot);
    }

    public void Dispose()
    {
        List<Exception>? misses = null;
        try
        {
            _primarySocket.Dispose();
        }
        catch (Exception problem)
        {
            (misses ??= []).Add(problem);
        }
        try
        {
            _backdropSocket?.Dispose();
        }
        catch (Exception problem)
        {
            (misses ??= []).Add(problem);
        }
        try
        {
            _flightMarks.Dispose();
        }
        catch (Exception problem)
        {
            (misses ??= []).Add(problem);
        }

        if (misses is not null)
        {
            throw new AggregateException(
                $"The {_probeLabel} resources didn't fully release",
                misses);
        }
    }

    internal static IReadOnlyList<RealmActor> AssemblePaintActors(RealmActor? backdrop, RealmActor primary)
    {
        return backdrop is not null && backdrop.MeshRefs.Count > 0
            ? [backdrop, primary]
            : [primary];
    }

    private void PushBeastLamp()
    {
        Vector3 dir = Vector3.Normalize(new Vector3(0.3f, 1.9f, 0.65f));
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
            CamAndMoment = new Vector4(_cam.Eye, 0f),
        });
    }
}
