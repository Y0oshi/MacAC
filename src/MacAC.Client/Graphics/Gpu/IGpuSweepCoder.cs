namespace MacAC.Client.Graphics.Gpu;

internal interface IGpuSweepCoder : IDisposable
{
    // The pass this encoder is recording into
    GpuPassSpec Pass { get; }

    // Binds the shader program and all baked fixed state
    void BindPipeline(IGpuPipe pipe);

    void AttachDepotBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets, uint byteSize);

    void AttachUniformBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets, uint byteSize);

    void AttachVertBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets);

    // Binds the index source
    void AttachOrdinalBuf(IClientGpuBuffer buf, uint shiftOctets, GpuOrdinalKind ordinalKind);

    void AssignPushConstants(in GpuShoveConstants constants);

    void AssignViewRect(int x, int y, int width, int height);

    void AssignScissor(int x, int y, int width, int height);

    void AssignPruneManner(GpuPruneManner pruneManner);

    void AssignFrontFace(GpuFrontFacet frontFace);

    // Dynamic depth-write override - how the translucent pass stops occluding later draws
    void AssignZDepthEmit(bool turnedOn);

    void AssignStencil(in GpuStencilLedger stencil);

    // Draws indexed geometry directly, without an indirect buffer
    void PaintIndexed(uint ordinalTally, uint instTally, uint leadOrdinal, int vertShift, uint leadInst);

    // Draws non-indexed geometry - the retained UI's batched sprite/glyph quads
    void Draw(uint vertTally, uint instTally, uint leadVert, uint leadInst);

    void MultiPaintIndexedIndirect(IClientGpuBuffer directives, uint shiftOctets, uint paintTally, uint strideOctets);

    IDisposable CommenceTickerAmbit(string ambitLabel);
}
