using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

public sealed class PhrasePainter : IDisposable
{
    internal const int FloatsPerVert = 8;
    private const int VertStrideOctets = FloatsPerVert * sizeof(float);

    internal static readonly GpuVertexArrangement SpriteVertArrangement = GpuVertexArrangement.Interleaved(
        strideOctets: VertStrideOctets,
        [
            new GpuVertexAttribute(0, GpuVertFmt.Float2, 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float2, 8),
            new GpuVertexAttribute(2, GpuVertFmt.Float4, 16),
        ]);

    private readonly ILatestGpuCycleOrigin _cycleSrc;
    private readonly IGpuPipe _pipe;

    private sealed class ClientSpriteSeg { public uint Texture; public readonly List<float> Verts = new(256); }

    private readonly List<float> _phraseBuffer = new(8192);
    private readonly List<float> _rectBuffer = new(1024);
    private readonly List<ClientSpriteSeg> _spriteSegs = [];
    private int _segConsumed;
    private int _phraseVerts;
    private Vector2 _monitorDims;

    internal long DynamicBufCapOctets => 0;

    internal IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> DiagSpriteSegments
    {
        get
        {
            var outcome = new List<(uint, int, float)>(_segConsumed);
            for (int idx = 0; idx < _segConsumed; ++idx)
            {
                ClientSpriteSeg seg = _spriteSegs[idx];
                float alpha = seg.Verts.Count > 0 ? seg.Verts[7] : 0f;
                outcome.Add((seg.Texture, seg.Verts.Count / FloatsPerVert, alpha));
            }
            return outcome;
        }
    }

    internal IReadOnlyList<(uint Texture, IReadOnlyList<float> Verts)> DiagSpriteSegmentVerts
    {
        get
        {
            var outcome = new List<(uint, IReadOnlyList<float>)>(_segConsumed);
            for (int idx = 0; idx < _segConsumed; ++idx)
            {
                ClientSpriteSeg seg = _spriteSegs[idx];
                outcome.Add((seg.Texture, seg.Verts.ToArray()));
            }
            return outcome;
        }
    }

    internal (int VertexCount, float Alpha) DiagPhraseBuf
        => (_phraseVerts, _phraseBuffer.Count > 0 ? _phraseBuffer[7] : 0f);

    internal int DiagRectVertTally { get; private set; }

    private readonly List<float> _topLayerPhraseBuffer = new(1024);
    private readonly List<float> _topLayerRectBuffer = new(256);
    private readonly List<ClientSpriteSeg> _topLayerSpriteSegs = [];
    private int _topLayerSegConsumed;
    private int _topLayerPhraseVerts;
    private int _topLayerRectVerts;

    public bool TopLayerManner { get; set; }

    internal PhrasePainter(IClientGpuDevice dev, ILatestGpuCycleOrigin frameSource, string shaderDirection)
    {
        ArgumentNullException.ThrowIfNull(dev);
        _cycleSrc = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDirection);

        _pipe = dev.BuildPipe(new GpuPipeSpec
        {
            Name = "ui-text",
            Shaders = new GpuShaderGroup("hud_text"),
            VertArrangement = SpriteVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.StraightAlpha,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = 1,
        });
    }

    internal Vector2 CanvasScale = Vector2.One;

    internal Func<uint, uint>? LinearTwinResolver { get; set; }

    public void Begin(Vector2 monitorDims)
    {
        _monitorDims = monitorDims;
        _phraseBuffer.Clear();
        _rectBuffer.Clear();
        _segConsumed = 0; // pool the SpriteSeg objects across frames
        _phraseVerts = 0;
        DiagRectVertTally = 0;
        _topLayerPhraseBuffer.Clear();
        _topLayerRectBuffer.Clear();
        _topLayerSegConsumed = 0;
        _topLayerPhraseVerts = 0;
        _topLayerRectVerts = 0;
        TopLayerManner = false;
    }

    public void PaintRect(float x, float y, float w, float h, Vector4 tint)
    {
        if (TopLayerManner) { AffixQuad(_topLayerRectBuffer, x, y, w, h, 0, 0, 0, 0, tint); _topLayerRectVerts += 6; }
        else { AffixQuad(_rectBuffer, x, y, w, h, 0, 0, 0, 0, tint); DiagRectVertTally += 6; }
    }

    public void PaintPopulate(float x, float y, float w, float h, Vector4 tint)
    {
        PaintSprite(WidgetTextureChartHandle.None, x, y, w, h, 0f, 0f, 1f, 1f, tint);
    }

    public void PaintRectOutline(float x, float y, float w, float h, Vector4 tint, float thickness = 1f)
    {
        PaintRect(x, y, w, thickness, tint);
        PaintRect(x, y + h - thickness, w, thickness, tint);
        PaintRect(x, y, thickness, h, tint);
        PaintRect(x + w - thickness, y, thickness, h, tint);
    }

    public void PaintString(BitmapFont typeface, string phrase, float x, float y, Vector4 tint)
    {
        PaintStringCore(
                typeface, phrase, x, y, tint,
                clip: false, 0f, 0f, 0f, 0f);
    }

    public void PaintSprite(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        if (CanvasScale != Vector2.One && LinearTwinResolver is { } locate)
            texture = locate(texture);

        ClientSpriteSeg seg = TopLayerManner
            ? UpcomingSpriteSeg(_topLayerSpriteSegs, ref _topLayerSegConsumed, texture)
            : UpcomingSpriteSeg(_spriteSegs, ref _segConsumed, texture);
        AffixQuad(seg.Verts, x, y, w, h, u0, v0, u1, v1, tint);
    }

    public void Flush(BitmapFont? typeface)
    {
        bool anyNorm = _segConsumed > 0 || _phraseVerts > 0 || DiagRectVertTally > 0;
        bool anyTopLayer = _topLayerSegConsumed > 0 || _topLayerPhraseVerts > 0 || _topLayerRectVerts > 0;
        if (!anyNorm && !anyTopLayer) return;

        IGpuCycle cycle = _cycleSrc.LatestCycle
            ?? throw new InvalidOperationException(
                "TextRenderer.Flush needs an open IGpuCycle (see GpuDeviceCycleLifespan) - " +
                "the host must drive IGpuDevice.BeginFrame() prior to rendering the retained UI");

        using var coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = "ui-text",
            Color = new GpuTintAffix(
                Target: null,
                Load: GpuPullOp.Load,
                Store: GpuVaultOp.Store,
                ClearColor: default),
            ZDepth = null,
            SampleCount = 1,
        });
        coder.BindPipeline(_pipe);

        PaintStratum(_spriteSegs, _segConsumed, _rectBuffer, DiagRectVertTally, _phraseBuffer, _phraseVerts, typeface, cycle, coder);
        PaintStratum(_topLayerSpriteSegs, _topLayerSegConsumed, _topLayerRectBuffer, _topLayerRectVerts, _topLayerPhraseBuffer, _topLayerPhraseVerts, typeface, cycle, coder);
    }

    public void Dispose() => _pipe.Dispose();

    internal void PaintStringClipped(
        BitmapFont typeface,
        string phrase,
        float x,
        float y,
        Vector4 tint,
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom)
    {
        PaintStringCore(
                typeface, phrase, x, y, tint,
                clip: true, clipLeft, clipTop, clipRight, clipBottom);
    }

    internal static uint LocateExternalTextureSocket(GpuTextureSlot socket) =>
        WidgetTextureChartHandle.FromSocket(socket);

    private void PaintStringCore(
        BitmapFont typeface,
        string phrase,
        float x,
        float y,
        Vector4 tint,
        bool clip,
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom)
    {
        float curX = x;
        float baseline = y + typeface.Ascent;

        for (int idx = 0; idx < phrase.Length; ++idx)
        {
            char c = phrase[idx];
            if (c == '\n')
            {
                curX = x;
                baseline += typeface.LineHeight;
                continue;
            }
            if (!typeface.TryFetchGlyph(c, out var glyph))
            {
                // Unknown glyph - skip its advance width if '?' exists.
                if (typeface.TryFetchGlyph('?', out var q))
                    curX += q.Advance;
                continue;
            }

            float gx = curX + glyph.ShiftX;
            float gy = baseline + glyph.ShiftY;
            float gw = glyph.Width;
            float gh = glyph.Height;
            float u0 = glyph.UvLowerX;
            float v0 = glyph.UvLowerY;
            float u1 = glyph.UvUpperX;
            float v1 = glyph.UvUpperY;

            if (gw > 0 && gh > 0
                && (!clip || ClientQuadClipper.TryClip(
                    clipLeft, clipTop, clipRight, clipBottom,
                    ref gx, ref gy, ref gw, ref gh,
                    ref u0, ref v0, ref u1, ref v1)))
            {
                if (TopLayerManner) { AffixQuad(_topLayerPhraseBuffer, gx, gy, gw, gh, u0, v0, u1, v1, tint); _topLayerPhraseVerts += 6; }
                else { AffixQuad(_phraseBuffer, gx, gy, gw, gh, u0, v0, u1, v1, tint); _phraseVerts += 6; }
            }
            curX += glyph.Advance;
        }
    }

    private static ClientSpriteSeg UpcomingSpriteSeg(List<ClientSpriteSeg> segs, ref int consumed, uint texture)
    {
        if (consumed > 0 && segs[consumed - 1].Texture == texture)
            return segs[consumed - 1];
        if (consumed < segs.Count)
        {
            ClientSpriteSeg s = segs[consumed++];
            s.Texture = texture;
            s.Verts.Clear();
            return s;
        }
        ClientSpriteSeg ns = new ClientSpriteSeg { Texture = texture };
        segs.Add(ns);
        ++consumed;
        return ns;
    }

    private void AffixQuad(List<float> buffer,
        float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        if (CanvasScale != Vector2.One)
        {
            x *= CanvasScale.X;
            y *= CanvasScale.Y;
            w *= CanvasScale.X;
            h *= CanvasScale.Y;
        }
        void V(float px, float py, float pu, float pv)
        {
            buffer.Add(px); buffer.Add(py);
            buffer.Add(pu); buffer.Add(pv);
            buffer.Add(tint.X); buffer.Add(tint.Y); buffer.Add(tint.Z); buffer.Add(tint.W);
        }
        V(x, y, u0, v0);
        V(x + w, y + h, u1, v1);
        V(x + w, y, u1, v0);
        V(x, y, u0, v0);
        V(x, y + h, u0, v1);
        V(x + w, y + h, u1, v1);
    }

    private void PaintStratum(
        List<ClientSpriteSeg> spriteSegs, int segConsumed,
        List<float> rectBuffer, int rectVerts,
        List<float> phraseBuffer, int phraseVerts, BitmapFont? typeface,
        IGpuCycle cycle, IGpuSweepCoder coder)
    {
        if (segConsumed > 0)
        {
            for (int idx = 0; idx < segConsumed; ++idx)
            {
                ClientSpriteSeg seg = spriteSegs[idx];
                if (seg.Verts.Count is 0) continue;
                AssignTextures(coder, tintHnd: seg.Texture, coverageHnd: WidgetTextureChartHandle.None);
                PaintLoop(cycle, coder, seg.Verts);
            }
        }

        if (rectVerts > 0)
        {
            AssignTextures(coder, WidgetTextureChartHandle.None, WidgetTextureChartHandle.None);
            PaintLoop(cycle, coder, rectBuffer);
        }

        if (phraseVerts > 0 && typeface is not null)
        {
            AssignTextures(coder, WidgetTextureChartHandle.None, coverageHnd: typeface.TextureId);
            PaintLoop(cycle, coder, phraseBuffer);
        }
    }

    private void AssignTextures(IGpuSweepCoder coder, uint tintHnd, uint coverageHnd)
    {
        var constants = GpuShoveConstants.Default;
        constants.ParamA = _monitorDims.X;
        constants.ParameterB = _monitorDims.Y;
        constants.TextureIndexA = WidgetTextureChartHandle.ToSocket(tintHnd).Index;
        constants.TextureOrdinalB = WidgetTextureChartHandle.ToSocket(coverageHnd).Index;
        coder.AssignPushConstants(constants);
    }

    private static void PaintLoop(IGpuCycle cycle, IGpuSweepCoder coder, List<float> buffer)
    {
        if (buffer.Count is 0)
            return;
        var alloc = cycle.ReserveLoop(buffer.Count * sizeof(float), GpuLoopPurpose.Vertex);
        CollectionsMarshal.AsSpan(buffer).CopyTo(alloc.AsSpan<float>());
        coder.AttachVertBuf(0, alloc.Buffer, alloc.ShiftOctets);
        coder.Draw((uint)(buffer.Count / FloatsPerVert), 1, 0, 0);
    }
}
