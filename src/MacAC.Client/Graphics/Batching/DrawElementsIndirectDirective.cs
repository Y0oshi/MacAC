using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Batching;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DrawElementsIndirectDirective
{
    public uint Count;
    public uint InstTally;  // number of instances
    public uint LeadOrdinal;     // offset into IBO, in indices
    public int BaseVert;     // vertex offset into VBO
    public uint BaseInst;   // first instance ID (offsets per-instance attribs / SSBO read)

    /// <summary>
    /// The directive that draws one batch: where its triangles are, and which run of staged
    /// instances to draw them with.
    ///
    /// The deferred alpha pass and the ordered stream both build this from the world draw identity,
    /// and each has to map the same four geometry fields the same way. Crossing two of them, say the
    /// index offset and the vertex offset, draws whatever geometry happens to live there: no error,
    /// no wrong count, just the wrong triangles.
    /// </summary>
    internal static DrawElementsIndirectDirective For(
        ClusterTag tag,
        int firstInstance,
        int instanceCount) => new()
        {
            Count = checked((uint)tag.IndexCount),
            InstTally = checked((uint)instanceCount),
            LeadOrdinal = tag.FirstIndex,
            BaseVert = tag.BaseVertex,
            BaseInst = checked((uint)firstInstance),
        };
}
