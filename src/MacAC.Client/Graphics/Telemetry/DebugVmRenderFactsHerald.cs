using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

internal readonly record struct DiagVmRasterizeFacts(
    int VisibleLandblocks,
    int TotalLandblocks,
    float NearestObjectDistance,
    string NearestObjectLabel,
    bool Colliding)
{
    public static DiagVmRasterizeFacts Initial
    {
        get
        {
            return new(
        VisibleLandblocks: 0,
        TotalLandblocks: 0,
        NearestObjectDistance: float.PositiveInfinity,
        NearestObjectLabel: "-",
        Colliding: false);
        }
    }
}

internal interface IDiagVmRasterizeFactsOrigin
{
    DiagVmRasterizeFacts DiagVmFacts { get; }
}

internal sealed class DebugVmRenderFactsHerald : IDiagVmRasterizeFactsOrigin
{
    internal const float AvatarImpactRadius = 0.48f;
    internal const float LinkThreshold = 0.05f;

    public DiagVmRasterizeFacts DiagVmFacts { get; private set; } =
        DiagVmRasterizeFacts.Initial;

    public void BroadcastDiagVmFacts(
        bool consumerEngaged,
        int shownLbs,
        int sumLbs,
        Vector3 closestOrigin,
        IEnumerable<ProxyEntry>? shadeObjects)
    {
        if (!consumerEngaged)
            return;
        ArgumentNullException.ThrowIfNull(shadeObjects);

        float closestGap = float.PositiveInfinity;
        string closestCaption = "-";
        foreach (ProxyEntry shade in shadeObjects)
        {
            float dx = shade.Position.X - closestOrigin.X;
            float dy = shade.Position.Y - closestOrigin.Y;
            float gap = MathF.Sqrt(dx * dx + dy * dy)
                - shade.Radius
                - AvatarImpactRadius;
            if (gap < closestGap)
            {
                closestGap = gap;
                closestCaption = $"0x{shade.EntityId:X8} {shade.CollisionType}";
            }
        }

        DiagVmFacts = new DiagVmRasterizeFacts(
            shownLbs,
            sumLbs,
            closestGap < 0f ? 0f : closestGap,
            closestCaption,
            closestGap < LinkThreshold);
    }
}
