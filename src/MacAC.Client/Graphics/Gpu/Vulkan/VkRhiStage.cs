using System.Numerics;
using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkRhiStage : IDisposable
{
    internal static readonly (string Corner, uint Rgba)[] QuadrantMarkers =
    [
        ("top-left", 0xE04040FFu),
        ("top-right", 0x40E040FFu),
        ("bottom-left", 0x4060E0FFu),
        ("bottom-right", 0xF0F0F0FFu),
    ];

    private const int OffscreenReach = 128;

    private readonly ClientVulkanGpuDevice _device;
    private readonly IClientGpuBuffer _vertArena;
    private readonly IClientGpuBuffer _ordinalArena;
    private readonly IGpuPipe _triMeshPipe;
    private readonly IGpuPipe _strokePipe;
    private readonly IGpuRasterizeMark _offscreen;
    private readonly IGpuBitmap _cardTexture;
    private readonly IGpuBitmap _compressedTexture;
    private readonly List<IDisposable> _possessed = [];

    private readonly GpuTextureSlot _cardSocket;
    private readonly GpuTextureSlot _compressedSocket;
    private GpuTextureSlot _offscreenSocket = GpuTextureSlot.Unassigned;

    private readonly uint _quadOrdinalTally;
    private readonly uint _strokeVertTally;
    private readonly uint _strokeLeadVert;

    private bool _destroyed;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Vert(Vector3 locus, Vector3 norm, Vector2 bmpCoord)
    {
        public Vector3 Position = locus;
        public Vector3 Normal = norm;
        public Vector2 BmpCoord = bmpCoord;
    }

    // std430 BatchData at the pinned 16-byte stride
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BatchData
    {
        public uint TextureIndex;
        public uint TextureLayer;
        public uint Tint;
        public uint Pad;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DrawIndexedIndirectDirective
    {
        public uint OrdinalCount;
        public uint InstanceTally;
        public uint FirstOrdinal;
        public int VertShift;
        public uint LeadInstance;
    }

    internal VkRhiStage(ClientVulkanGpuDevice device, int specimenTally)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        SampleCount = Math.Max(1, specimenTally);

        Vert[] verts = AssembleVerts(out ushort[] ordinals, out _quadOrdinalTally, out _strokeLeadVert, out _strokeVertTally);
        _vertArena = device.BuildBuf(new GpuBufferSpec(
            "vk-scene-vertex-arena",
            verts.Length * Marshal.SizeOf<Vert>(),
            GpuBufferPurpose.Vertex | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _ordinalArena = device.BuildBuf(new GpuBufferSpec(
            "vk-scene-index-arena",
            ordinals.Length * sizeof(ushort),
            GpuBufferPurpose.Index | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _vertArena.Upload(0, MemoryMarshal.AsBytes<Vert>(verts));
        _ordinalArena.Upload(0, MemoryMarshal.AsBytes<ushort>(ordinals));
        _possessed.Add(_vertArena);
        _possessed.Add(_ordinalArena);

        _cardTexture = AssembleFacingCard(device);
        _compressedTexture = AssembleCompressedCheckerboard(device);
        _possessed.Add(_cardTexture);
        _possessed.Add(_compressedTexture);

        IClientGpuSampler sampler = device.BuildSampler(GpuSamplerSpec.RealmClamp);
        _cardSocket = device.EnrollTexture(_cardTexture, sampler);
        _compressedSocket = device.EnrollTexture(_compressedTexture, sampler);

        _offscreen = device.BuildRasterizeMark(new GpuRenderTargetSpec(
            "vk-scene-offscreen",
            OffscreenReach,
            OffscreenReach,
            GpuBitmapFmt.Rgba8UnormRenderTarget,
            DepthFormat: null,
            SampleCount: 1));
        _possessed.Add(_offscreen);

        _triMeshPipe = device.BuildPipe(new GpuPipeSpec
        {
            Name = "vk-scene-mesh",
            Shaders = new GpuShaderGroup("gpu_probe"),
            VertArrangement = GpuVertexArrangement.RealmTriMesh,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.StraightAlpha,
            Depth = GpuDepthLedger.SolidDefault,
            Cull = GpuPruneManner.None,
            SampleCount = SampleCount,
        });
        _strokePipe = device.BuildPipe(new GpuPipeSpec
        {
            Name = "vk-scene-line",
            Shaders = new GpuShaderGroup("gpu_probe"),
            VertArrangement = GpuVertexArrangement.RealmTriMesh,
            Wiring = GpuPrimitiveWiring.LineList,
            Blend = GpuBlendManner.None,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            SampleCount = SampleCount,
        });
        _possessed.Add(_triMeshPipe);
        _possessed.Add(_strokePipe);

        OffscreenPipe = device.BuildPipe(new GpuPipeSpec
        {
            Name = "vk-scene-offscreen",
            Shaders = new GpuShaderGroup("gpu_probe"),
            VertArrangement = GpuVertexArrangement.RealmTriMesh,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.None,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            SampleCount = 1,
        });
        _possessed.Add(OffscreenPipe);
    }

    internal int SampleCount { get; }

    internal IGpuPipe OffscreenPipe { get; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        for (int idx = _possessed.Count - 1; idx >= 0; --idx)
            _possessed[idx].Dispose();
        _possessed.Clear();
    }

    internal void Render(IGpuCycle cycle, uint width, uint height, double secs)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        PaintOffscreen(cycle);
        PaintPrimary(cycle, width, height, secs);
    }

    private void PaintOffscreen(IGpuCycle cycle)
    {
        using (IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = "vk-scene-offscreen",
            Color = new GpuTintAffix(
                _offscreen,
                GpuPullOp.Clear,
                GpuVaultOp.Store,
                new Vector4(0.12f, 0.02f, 0.24f, 1f)),
            ZDepth = null,
            SampleCount = 1,
        }))
        {
            using IDisposable _ = coder.CommenceTickerAmbit("offscreen");
            coder.BindPipeline(OffscreenPipe);

            var constants = GpuShoveConstants.Default;
            constants.IlluminationManner = 1;
            coder.AssignPushConstants(constants);

            EmitInsts(cycle, coder, [Matrix4x4.CreateScale(0.75f)]);
            EmitLots(cycle, coder, [new BatchData { Tint = 0xFFC020FFu }]);
            AttachArena(coder);
            coder.PaintIndexed(6, 1, 0, 0, 0);
        }

        if (!_offscreenSocket.IsAssigned)
        {
            _offscreenSocket = _device.EnrollTexture(
                _offscreen.ColorTexture,
                _device.BuildSampler(GpuSamplerSpec.WidgetClosest));
        }
    }

    private void PaintPrimary(IGpuCycle cycle, uint width, uint height, double secs)
    {
        using var coder = cycle.BeginPass(GpuPassSpec.BackbufferWipe(
            "vk-scene-main",
            new Vector4(0.043f, 0.075f, 0.153f, 1f),
            SampleCount));
        using IDisposable ambit = coder.CommenceTickerAmbit("main");

        float aspect = height is 0 ? 1f : width / (float)height;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f,
            aspect,
            0.1f,
            50f);
        Matrix4x4 lens = Matrix4x4.CreateLookAt(
            new Vector3(0f, 0f, 3.4f),
            Vector3.Zero,
            Vector3.UnitY);

        var constants = GpuShoveConstants.Default;
        constants.LensMirror = lens * proj;
        constants.IlluminationManner = 0;

        coder.BindPipeline(_triMeshPipe);
        coder.AssignPushConstants(constants);
        coder.AssignPruneManner(GpuPruneManner.None);
        coder.AssignZDepthEmit(true);

        float wobble = (float)Math.Sin(secs) * 0.05f;
        Matrix4x4[] insts =
        [
            Matrix4x4.CreateScale(2.6f, 1.6f, 1f) * Matrix4x4.CreateTranslation(0f, 0f, -0.4f),
            Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(-1.0f, 0.55f + wobble, 0f),
            Matrix4x4.CreateScale(0.36f) * Matrix4x4.CreateTranslation(0.95f, 0.55f, 0f),
            Matrix4x4.CreateScale(0.28f) * Matrix4x4.CreateTranslation(-1.0f, -0.6f, 0f),
            Matrix4x4.CreateScale(0.44f) * Matrix4x4.CreateTranslation(0.6f, -0.62f, 0f),
        ];
        BatchData[] lots =
        [
            new BatchData { TextureIndex = _cardSocket.Index, Tint = 0xFFFFFFFFu },
            new BatchData { TextureIndex = _compressedSocket.Index, Tint = QuadrantMarkers[0].Rgba },
            new BatchData { TextureIndex = _offscreenSocket.Index, Tint = QuadrantMarkers[1].Rgba },
            new BatchData { TextureIndex = _compressedSocket.Index, Tint = QuadrantMarkers[2].Rgba },
            new BatchData { TextureIndex = _cardSocket.Index, Tint = QuadrantMarkers[3].Rgba },
        ];

        EmitInsts(cycle, coder, insts);
        EmitLots(cycle, coder, lots);
        AttachArena(coder);

        var directives = cycle.ReserveLoop(
            insts.Length * Marshal.SizeOf<DrawIndexedIndirectDirective>(),
            GpuLoopPurpose.Indirect);
        var span = directives.AsSpan<DrawIndexedIndirectDirective>();
        for (int idx = 0; idx < insts.Length; ++idx)
        {
            span[idx] = new DrawIndexedIndirectDirective
            {
                OrdinalCount = _quadOrdinalTally,
                InstanceTally = 1,
                FirstOrdinal = 0,
                VertShift = 0,
                LeadInstance = (uint)idx,
            };
        }

        coder.MultiPaintIndexedIndirect(
            directives.Buffer,
            directives.ShiftOctets,
            (uint)insts.Length,
            (uint)Marshal.SizeOf<DrawIndexedIndirectDirective>());

        constants.IlluminationManner = 1;
        coder.BindPipeline(_strokePipe);
        coder.AssignPushConstants(constants);
        coder.AssignZDepthEmit(false);

        EmitInsts(cycle, coder, [Matrix4x4.Identity]);
        EmitLots(cycle, coder, [new BatchData { Tint = 0xFFE060FFu }]);
        AttachArena(coder);
        coder.Draw(_strokeVertTally, 1, _strokeLeadVert, 0);
    }

    private void AttachArena(IGpuSweepCoder coder)
    {
        coder.AttachVertBuf(0, _vertArena, 0);
        coder.AttachOrdinalBuf(_ordinalArena, 0, GpuOrdinalKind.UInt16);
    }

    private static void EmitInsts(
        IGpuCycle cycle,
        IGpuSweepCoder coder,
        ReadOnlySpan<Matrix4x4> xforms)
    {
        var alloc = cycle.ReserveLoop(
            xforms.Length * Marshal.SizeOf<Matrix4x4>(),
            GpuLoopPurpose.Storage);
        xforms.CopyTo(alloc.AsSpan<Matrix4x4>());
        coder.AttachDepotBuf(
            GpuBindingModel.DepotInsts,
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)alloc.Data.Length);
    }

    private static void EmitLots(
        IGpuCycle cycle,
        IGpuSweepCoder coder,
        ReadOnlySpan<BatchData> lots)
    {
        var alloc = cycle.ReserveLoop(
            lots.Length * GpuBindingModel.GpuLotBlobStrideOctets,
            GpuLoopPurpose.Storage);
        lots.CopyTo(alloc.AsSpan<BatchData>());
        coder.AttachDepotBuf(
            GpuBindingModel.DepotLots,
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)alloc.Data.Length);
    }

    // One unit quad (indexed) followed by an asymmetric open line figure
    private static Vert[] AssembleVerts(
        out ushort[] ordinals,
        out uint quadOrdinalTally,
        out uint strokeLeadVert,
        out uint strokeVertTally)
    {
        List<Vert> verts = new List<Vert>
        {
            new(new Vector3(-0.5f, 0.5f, 0f), Vector3.UnitZ, new Vector2(0f, 0f)),
            new(new Vector3(-0.5f, -0.5f, 0f), Vector3.UnitZ, new Vector2(0f, 1f)),
            new(new Vector3(0.5f, -0.5f, 0f), Vector3.UnitZ, new Vector2(1f, 1f)),
            new(new Vector3(0.5f, 0.5f, 0f), Vector3.UnitZ, new Vector2(1f, 0f)),
        };
        ordinals = [0, 1, 2, 0, 2, 3];
        quadOrdinalTally = 6;

        strokeLeadVert = (uint)verts.Count;
        Vector3[] trail =
        [
            new(-1.5f, 0.9f, 0.2f),
            new(-1.5f, -0.9f, 0.2f),
            new(-1.5f, -0.9f, 0.2f),
            new(0.2f, -0.9f, 0.2f),
            new(0.2f, -0.9f, 0.2f),
            new(0.2f, -0.4f, 0.2f),
        ];
        foreach (Vector3 pt in trail)
            verts.Add(new Vert(pt, Vector3.UnitZ, Vector2.Zero));
        strokeVertTally = (uint)trail.Length;

        return [.. verts];
    }

    private static IGpuBitmap AssembleFacingCard(ClientVulkanGpuDevice dev)
    {
        const int reach = 16;
        int tiers = VkTextureFormatMapping.WholeMipTierTally(reach, reach);
        IGpuBitmap texture = dev.BuildTexture(new GpuBitmapSpec(
            "vk-scene-orientation-card",
            GpuBitmapFlavor.Texture2DArray,
            GpuBitmapFmt.Rgba8Unorm,
            reach,
            reach,
            LayerCount: 1,
            MipLevelCount: tiers));

        byte[] px = new byte[reach * reach * 4];
        for (int y = 0; y < reach; ++y)
        {
            for (int x = 0; x < reach; ++x)
            {
                bool top = y < reach / 2;
                bool left = x < reach / 2;
                uint colour = (top, left) switch
                {
                    (true, true) => QuadrantMarkers[0].Rgba,
                    (true, false) => QuadrantMarkers[1].Rgba,
                    (false, true) => QuadrantMarkers[2].Rgba,
                    _ => QuadrantMarkers[3].Rgba,
                };
                bool cycle = x is 0 || y is 0 || x == reach - 1 || y == reach - 1;
                if (cycle)
                    colour = 0x101010FFu;

                int shift = ((y * reach) + x) * 4;
                px[shift + 0] = (byte)(colour >> 24);
                px[shift + 1] = (byte)(colour >> 16);
                px[shift + 2] = (byte)(colour >> 8);
                px[shift + 3] = (byte)colour;
            }
        }

        texture.Upload(0, 0, px);
        texture.ProduceMipChain();
        return texture;
    }

    private static IGpuBitmap AssembleCompressedCheckerboard(ClientVulkanGpuDevice dev)
    {
        const int reach = 32;
        int tiers = VkTextureFormatMapping.WholeMipTierTally(reach, reach);
        IGpuBitmap texture = dev.BuildTexture(new GpuBitmapSpec(
            "vk-scene-checkerboard",
            GpuBitmapFlavor.Texture2DArray,
            GpuBitmapFmt.Bc1Unorm,
            reach,
            reach,
            LayerCount: 1,
            MipLevelCount: tiers));

        byte[] rgba = new byte[reach * reach * 4];
        for (int y = 0; y < reach; ++y)
        {
            for (int x = 0; x < reach; ++x)
            {
                bool lamp = ((x / 4) + (y / 4)) % 2 is 0;
                byte val = lamp ? (byte)0xFF : (byte)0x50;
                int shift = ((y * reach) + x) * 4;
                rgba[shift + 0] = val;
                rgba[shift + 1] = val;
                rgba[shift + 2] = val;
                rgba[shift + 3] = 0xFF;
            }
        }

        texture.Upload(0, 0, ChunkCompressionCodec.PackTier(GpuBitmapFmt.Bc1Unorm, rgba, reach, reach));
        foreach (ChunkCompressionMipSequence.Tier tier in
                 ChunkCompressionMipSequence.AssembleFromRgba(GpuBitmapFmt.Bc1Unorm, rgba, reach, reach, tiers))
        {
            texture.Upload(tier.MipLevel, 0, tier.Data);
        }

        return texture;
    }
}
