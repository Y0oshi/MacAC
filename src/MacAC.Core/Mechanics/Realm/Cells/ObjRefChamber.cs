using System.Numerics;

namespace MacAC.Mechanics.Realm.Cells;

/// <summary>Anything an object can be "in": an outdoor ground cell or an indoor EnvCell.</summary>
public abstract class ObjRefChamber(
    uint ident,
    Matrix4x4 realmXform,
    Matrix4x4 invRealmXform,
    Vector3 ownLimitsLower,
    Vector3 ownLimitsUpper,
    IReadOnlyList<ChamberGateway> gateways,
    IReadOnlyList<uint> stabRoster,
    bool observedBeyond)
{
    private const uint LeadInside = 0x100u;

    public uint Id { get; } = ident;

    public Matrix4x4 WorldTransform { get; } = realmXform;

    public Matrix4x4 InverseWorldTransform { get; } = invRealmXform;

    public Vector3 LocalLimitsMin { get; } = ownLimitsLower;

    public Vector3 LocalLimitsMax { get; } = ownLimitsUpper;

    public IReadOnlyList<ChamberGateway> Portals { get; } = gateways;

    public IReadOnlyList<uint> StabList { get; } = stabRoster;

    public bool SeenOutside { get; } = observedBeyond;

    public bool IsEnviron => (Id & 0xFFFFu) >= LeadInside;

    public abstract bool PtInCell(Vector3 realmPt);
}
