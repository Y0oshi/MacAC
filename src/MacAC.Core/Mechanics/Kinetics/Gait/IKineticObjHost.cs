using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public interface IKineticObjHost
{
    uint Id { get; }

    Locus Position { get; }

    Vector3 Velocity { get; }

    float Radius { get; }

    bool InContact { get; }

    float? MinterpMaxSpeed { get; }

    double CurTime { get; }

    double PhysicsTimerTime { get; }

    IKineticObjHost? GetObjectA(uint ident);

    IKineticObjHost? FetchRelationshipMark(uint objectIdent);

    void HandleUpdateTarget(TargetFacts details);

    void InterruptCurrentMovement();

    void AssignMark(uint ctxIdent, uint objectIdent, float radius, double quantum);

    void WipeMark();

    void TakeMarkRefresh(TargetFacts details, IKineticObjHost sender);

    void AppendVoyeur(IKineticObjHost watcher, float radius, double quantum);

    void DropVoyeur(uint watcherIdent, IKineticObjHost anticipatedWatcher);
}
