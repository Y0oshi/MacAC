using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;
using MacAC.Wire;

namespace MacAC.Client.Kinetics;

internal interface IOnlineActorMotionEngineWiring
{
    (float Radius, float Height) GetSetupCylinder(uint srvOid, RealmActor actor);

    (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)
        FetchRigCarrierForm(uint srvOid, RealmActor actor);
    bool RouteServerMoveTo(
        LocomotionKeeper travel,
        uint chamberIdent,
        RealmSession.MoverMotionUpdate refresh);
    void StickToObjectFromWire(IKineticObjHost? hub, uint markOid);
    void ClearTargetForHiddenEntity(uint srvOid);
    IKineticObjHost? ResolvePhysicsHost(uint srvOid);
}

internal sealed class DeferredOnlineActorMotionEngineWiring
    : IOnlineActorMotionEngineWiring
{
    private IOnlineActorMotionEngineWiring? _mark;

    public void Bind(IOnlineActorMotionEngineWiring mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (_mark is not null)
        {
            throw new InvalidOperationException(
                "Live entity motion runtime bindings are by now bound");
        }
        _mark = mark;
    }

    public IDisposable BindOwned(IOnlineActorMotionEngineWiring mark)
    {
        Bind(mark);
        return new Binding(this, mark);
    }

    public (float Radius, float Height) GetSetupCylinder(
        uint srvOid,
        RealmActor actor) => Target.GetSetupCylinder(srvOid, actor);

    private IOnlineActorMotionEngineWiring Target
    {
        get
        {
            return _mark ?? throw new InvalidOperationException(
            "Live entity motion runtime bindings were used prior to binding");
        }
    }

    public (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)
        FetchRigCarrierForm(uint srvOid, RealmActor actor) =>
        Target.FetchRigCarrierForm(srvOid, actor);

    public bool RouteServerMoveTo(
        LocomotionKeeper travel,
        uint chamberIdent,
        RealmSession.MoverMotionUpdate refresh) =>
        Target.RouteServerMoveTo(travel, chamberIdent, refresh);

    public void StickToObjectFromWire(IKineticObjHost? hub, uint markOid) =>
        Target.StickToObjectFromWire(hub, markOid);

    public void ClearTargetForHiddenEntity(uint srvOid) =>
        Target.ClearTargetForHiddenEntity(srvOid);

    public IKineticObjHost? ResolvePhysicsHost(uint srvOid) =>
        Target.ResolvePhysicsHost(srvOid);

    private void Loosen(IOnlineActorMotionEngineWiring anticipated)
    {
        if (ReferenceEquals(_mark, anticipated))
            _mark = null;
    }

    private sealed class Binding(
        DeferredOnlineActorMotionEngineWiring holder,
        IOnlineActorMotionEngineWiring anticipated) : IDisposable
    {
        private DeferredOnlineActorMotionEngineWiring? _holder = holder;
        private readonly IOnlineActorMotionEngineWiring _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}
