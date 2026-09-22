using System.Numerics;

namespace MacAC.Client.Kinetics;

internal sealed class AvatarShadeLedger
{
    public readonly record struct ClientSnapshot(
        Vector3 Position,
        Quaternion Orientation,
        uint CellId);

    public ClientSnapshot? Current { get; private set; }

    public void Set(Vector3 locus, Quaternion facing, uint chamberIdent) =>
        Current = new ClientSnapshot(locus, facing, chamberIdent);

    public void Clear() => Current = null;
}
