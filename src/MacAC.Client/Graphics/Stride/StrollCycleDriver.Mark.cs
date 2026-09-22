using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

internal sealed partial class StrollCycleDriver
{
    private void FlagAlphaIfGrown()
    {
        int tally = _alphaSubmissions.Count;
        if (tally == _alphaSubmitFlag)
            return;

        _alphaSubmitFlag = tally;
        _signals.Add(StrideFrameEvent.AlphaSubmitFlag(tally));
    }

    private IStrideBuildingFrameScope DemandOpenCycle()
    {
        return _cx ?? throw new InvalidOperationException(
            "StrollCycleDriver received a walk turn beyond BeginFrame/EndFrame - call "
            + "BeginFrame (or Collect/RunFrame) prior to driving the walk with this driver as "
            + "its IWalkEventSink");
    }

    private static StridePolygon ConvertToRealm(StridePolygon own, Matrix4x4 realmXform)
    {
        Vector3[] verts = new Vector3[own.Vertices.Length];
        for (int idx = 0; idx < verts.Length; ++idx)
            verts[idx] = Vector3.Transform(own.Vertices[idx], realmXform);

        Vector3 norm = own.Vertices.Length > 0
            ? Vector3.Normalize(Vector3.TransformNormal(own.Plane.Normal, realmXform))
            : Vector3.Zero;
        float d = verts.Length > 0 ? -Vector3.Dot(norm, verts[0]) : 0f;

        return new StridePolygon { Vertices = verts, Plane = new StridePlane(norm, d) };
    }
}
