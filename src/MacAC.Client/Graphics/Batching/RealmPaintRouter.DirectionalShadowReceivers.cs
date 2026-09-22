using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmPaintRouter
{
    internal sealed class DirectionalShadeReceiverPipelineLedger : IDisposable
    {
        private readonly TriMeshPipeGroup _backbuffer;
        private readonly TriMeshPipeGroup _offscreen;

        internal DirectionalShadeReceiverPipelineLedger(
            IDirectionalShadeReceiverSource src,
            TriMeshPipeGroup backbuffer,
            TriMeshPipeGroup offscreen)
        {
            Source = src;
            _backbuffer = backbuffer;
            _offscreen = offscreen;
        }

        internal IDirectionalShadeReceiverSource Source { get; }

        internal TriMeshPipeGroup ForSpecimenTally(int specimenTally) =>
            specimenTally > 1 ? _backbuffer : _offscreen;

        public void Dispose()
        {
            TeardownTriMeshPipeSet(_backbuffer);
            if (!ReferenceEquals(_offscreen, _backbuffer))
                TeardownTriMeshPipeSet(_offscreen);
        }
    }

    private DirectionalShadeReceiverPipelineLedger? _directedShadeRecipient;

    internal DirectionalShadeReceiverPipelineLedger? ReadyDirectedShadeRecipient(
        IDirectionalShadeReceiverSource? src,
        int specimenTally)
    {
        if (src is null)
            return null;
        IClientGpuDevice dev = _device
            ?? throw new InvalidOperationException("Directional receivers require the modern RHI device");
        if (_ambit is null || specimenTally != _ambit.SampleCount)
            throw new InvalidOperationException("Receiver and world-pass sample counts must match");

        TriMeshPipeGroup? backbuffer = null;
        TriMeshPipeGroup? offscreen = null;
        try
        {
            backbuffer = BuildTriMeshPipeSet(
                dev,
                specimenTally,
                baseShaders: src.PipeShaders.WorldReceiver,
                labelStem: "wb-mesh-atmospheric",
                usesRasterizeBundleShaderAbi: true);
            offscreen = specimenTally is 1
                ? backbuffer
                : BuildTriMeshPipeSet(
                    dev,
                    1,
                    baseShaders: src.PipeShaders.WorldReceiver,
                    labelStem: "wb-mesh-atmospheric",
                    usesRasterizeBundleShaderAbi: true);
            return new DirectionalShadeReceiverPipelineLedger(src, backbuffer, offscreen);
        }
        catch
        {
            TeardownTriMeshPipeSet(backbuffer);
            if (!ReferenceEquals(offscreen, backbuffer))
                TeardownTriMeshPipeSet(offscreen);
            throw;
        }
    }

    internal DirectionalShadeReceiverPipelineLedger? SwapDirectedShadeRecipient(
        DirectionalShadeReceiverPipelineLedger? contender)
    {
        var preceding = _directedShadeRecipient;
        _directedShadeRecipient = contender;
        return preceding;
    }

    internal static void AttachDirectedShadeRecipient(
        IGpuSweepCoder coder,
        in DirectionalShadeFrameWiring mapping)
    {
        if (mapping.Buffer is null)
            return;
        coder.AttachUniformBuf(
            GpuBindingModel.UniformDirectedShade,
            mapping.Buffer,
            mapping.OffsetBytes,
            mapping.SizeBytes);
        if (mapping.AtmosphericFrame.IsTied)
        {
            coder.AttachUniformBuf(
                GpuBindingModel.UniformAtmosphericCycle,
                mapping.AtmosphericFrame.Buffer!,
                mapping.AtmosphericFrame.OffsetBytes,
                mapping.AtmosphericFrame.SizeBytes);
        }
    }

    private TriMeshPipeGroup PipesFor(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        out DirectionalShadeFrameWiring shadeMapping)
    {
        var recipient = _directedShadeRecipient;
        var src = recipient?.Source;
        shadeMapping = DirectionalShadeFrameWiring.Disabled;
        bool mappingValid = src is not null
            && src.TryFetchLatestCycleMapping(cycle, out shadeMapping);
        return DirectionalShadeReceiverRule.ShouldPickRecipientPipe(
                coder.Pass.Name,
                src is not null,
                mappingValid)
            ? recipient!.ForSpecimenTally(coder.Pass.SampleCount)
            : PipesFor(coder);
    }

    private void TeardownDirectedShadeRecipientPipes()
    {
        var phase =
            SwapDirectedShadeRecipient(null);
        phase?.Dispose();
    }
}
