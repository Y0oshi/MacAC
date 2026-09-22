using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public sealed class Structure
{
    public required uint StructureIdent { get; init; }

    public required HashSet<uint> EnvironChamberIdents { get; init; }

    public required IReadOnlyList<Vector3[]> QuitGatewayPolygs { get; init; }

    public bool HasGatewayLimits { get; init; }

    public BatchBoundingBox GatewayLimits { get; init; }

    // Step 5 occlusion-query state (mutable, per-frame, RR9 scope)

    public uint AskIdent;

    public bool AskBegun;

    public bool WasShown;
}
