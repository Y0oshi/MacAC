using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

public sealed partial class LandModernPainter
{
    internal sealed class DirectionalShadeReceiverPipelineLedger(
        IDirectionalShadeReceiverSource src,
        IGpuPipe pipe) : IDisposable
    {
        internal IDirectionalShadeReceiverSource Source { get; } = src;

        internal IGpuPipe Pipeline { get; } = pipe;

        public void Dispose() => Pipeline.Dispose();
    }

    internal DirectionalShadeReceiverPipelineLedger? ReadyDirectedShadeRecipient(
        IDirectionalShadeReceiverSource? src,
        int specimenTally)
    {
        if (src is null)
            return null;
        IClientGpuDevice dev = _device
            ?? throw new InvalidOperationException("Directional receivers require the modern RHI device");
        return _ambit is null || specimenTally != _ambit.SampleCount
            ? throw new InvalidOperationException("Receiver and world-pass sample counts must match")
            : new DirectionalShadeReceiverPipelineLedger(
            src,
            dev.BuildPipe(
            new GpuPipeSpec
            {
                Name = "terrain-atmospheric",
                Shaders = src.PipeShaders.TerrainReceiver,
                VertArrangement = LandVertArrangement,
                Wiring = GpuPrimitiveWiring.TriangleList,
                Blend = GpuBlendManner.None,
                Depth = new GpuDepthLedger(true, true, RealmDepthContract.RealmContrast),
                Cull = GpuPruneManner.Back,
                FrontFace = GpuFrontFacet.CounterClockwise,
                AlphaToCoverage = false,
                TintEmit = true,
                UsesRasterizeBundleShaderAbi = true,
                SampleCount = specimenTally,
            }));
    }

    internal DirectionalShadeReceiverPipelineLedger? SwapDirectedShadeRecipient(
        DirectionalShadeReceiverPipelineLedger? contender)
    {
        var preceding = _directedShadeRecipient;
        _directedShadeRecipient = contender;
        return preceding;
    }
}
