using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Kinetics;

public interface ISimPeerMotion
{
    KineticBody Body { get; }
}

/// <summary>A runtime that reads the entity's physics host lazily.</summary>
public interface ISimKineticsHarborConsumer
{
    void AttachKineticsHub(Func<IKineticObjHost?> scan);
}

/// <summary>A runtime bound to the canonical physics host and cell accessors.</summary>
public interface ISimCanonicalKineticsConsumer
{
    void AttachCanonCore(Func<IKineticObjHost?> scanKineticsHub, Func<uint> scanChamber, Action<uint> emitChamber);
}

public interface ISimCanonicalCellConsumer
{
    void AttachCanonChamber(Func<uint> scan, Action<uint> emit);
}

public interface ISimPeerPlacement : ISimPeerMotion, ISimCanonicalCellConsumer
{
    uint CellId { get; set; }

    bool Airborne { get; set; }

    Vector3 LastServerPosition { get; set; }

    double LastServerPositionTime { get; set; }

    Vector3 LastShadowSyncPosition { get; set; }

    Quaternion LastShadowSyncOrientation { get; set; }

    void HitGround();

    void LeaveGround();
}
