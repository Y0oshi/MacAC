using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Graphics;

internal static class CanonHeldPose
{
    public static uint LocatePostureDid(IDatAccess datFiles, uint postureEnum)
    {
        uint masterDid = (uint)datFiles.Portal.Db.Header.MasterMapId;
        return masterDid is 0
            || !datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master)
            || !master.ClientEnumToId.TryGetValue(7u, out uint subDid)
            || !datFiles.Portal.TryGet<IdNameMap>(subDid, out var sub)
            ? 0u
            : sub.ClientEnumToId.TryGetValue(postureEnum, out uint did) ? did : 0u;
    }

    public static Matrix4x4 ConstructPieceXform(Vector3 defaultScaling, Vector3 origin, Quaternion facing)
    {
        return Matrix4x4.CreateScale(defaultScaling)
        * Matrix4x4.CreateFromQuaternion(facing)
        * Matrix4x4.CreateTranslation(origin);
    }
}
