namespace MacAC.Client.Graphics.Gpu;

internal interface IClientGpuDevice : IDisposable
{
    GpuBackendFlavor Backend { get; }

    GpuCapabilityCapture Capabilities { get; }

    // Frame-flight-gated resource release
    IGpuAssetSunsetFifo Retirement { get; }

    // GPU timing results from retired frames
    IGpuTickerReservoir Tickers { get; }

    GpuTextureSlot DefaultTextureSocket { get; }

    IClientGpuBuffer BuildBuf(in GpuBufferSpec blurb);

    IGpuBitmap BuildTexture(in GpuBitmapSpec blurb);

    IClientGpuSampler BuildSampler(in GpuSamplerSpec blurb);

    IGpuPipe BuildPipe(GpuPipeSpec blurb);

    IGpuRasterizeMark BuildRasterizeMark(in GpuRenderTargetSpec blurb);

    IGpuDirectedZDepthMark BuildDirectedZDepthMark(
        in GpuDirectionalDepthTargetSpec blurb);

    // Publishes a (texture, sampler) pair into the global table and returns the slot shaders index it
    // by
    GpuTextureSlot EnrollTexture(IGpuBitmap texture, IClientGpuSampler sampler);

    void FreeTextureSocket(GpuTextureSlot socket);

    // Opens the next frame, waiting for its flight slot to retire first
    IGpuCycle BeginFrame();

    void EnqueueDevAct(Action act);

    void HandleDevActs();

    byte[] CaptureBackbuffer(int width, int height);

    void PauseIdle();
}
