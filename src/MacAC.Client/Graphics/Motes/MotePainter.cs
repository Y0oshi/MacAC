using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Assets;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Geometry;
using RuntimeParticleEmitter = MacAC.Mechanics.Effects.MoteSpout;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter : IDisposable
{
    private readonly record struct LotTag(bool Additive);

    private readonly record struct MoteDraw(LotTag Key, MoteInstance Instance);

    private readonly record struct TriMeshLotTag(uint GfxObjId, int BatchIndex);

    private readonly record struct MeshMoteDraw(
        TriMeshLotTag Key,
        ThingRasterizeLot Batch,
        MeshMoteInstance Instance);

    private readonly record struct DeferredMoteDraw(
        MoteSubmissionKind Kind,
        MoteDraw Billboard,
        MeshMoteDraw Mesh,
        Matrix4x4 LensMirror);

    private readonly struct MoteInstance(
        Vector3 locus,
        Vector3 axisX,
        Vector3 axisY,
        uint tintArgb,
        MacAC.Client.Graphics.Gpu.GpuTextureSlot textureSocket,
        float gapSq,
        uint clipSocket)
    {
        public readonly Vector3 Position = locus;
        public readonly Vector3 AxisX = axisX;
        public readonly Vector3 AxisY = axisY;
        public readonly uint TintArgb = tintArgb;
        public readonly MacAC.Client.Graphics.Gpu.GpuTextureSlot TextureSlot = textureSocket;
        public readonly float DistanceSq = gapSq;
        public readonly uint ClipSocket = clipSocket;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BillboardGpuInst
    {
        public Vector4 Middle;
        public Vector4 AxisX;
        public Vector4 AxisY;
        public Vector4 Color;
        public uint TextureIndex;
        public uint ClipSlot;
    }

    // Vertex-instance ABI shared with particle_mesh.vert
    [StructLayout(LayoutKind.Sequential)]
    internal struct MeshMoteGpuInstance
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public uint ClipSlot;
    }

    private readonly struct MeshMoteInstance(
        Matrix4x4 model,
        uint tintArgb,
        float gapSq,
        uint clipSocket)
    {
        public readonly Matrix4x4 Model = model;
        public readonly uint ColorArgb = tintArgb;
        public readonly float DistanceSq = gapSq;
        public readonly uint ClipSlot = clipSocket;
    }

    private readonly BitmapStash? _textures;

    private readonly IDatAccess? _datFiles;

    private readonly RealmTriMeshBridge? _triMeshBridge;

    private readonly MoteSys _motes;

    private readonly CanonAlphaFifo? _alphaFifo;

    private readonly AlphaPaintSource _alphaSrc;

    private readonly ReserveDeferredParticleDraw _allocatePostponedMotePaint;

    private readonly DrawImmediateParticle _drawImmediateParticle;

    private DeferredMoteDraw _relayPostponedMote;

    private readonly Dictionary<uint, MoteGfxInfo> _moteGfxDetailsByGfxObjRef = [];

    private readonly Dictionary<int, MoteGfxInfo> _moteGfxDetailsBySpout = [];

    private readonly Dictionary<uint, CanonMoteGeometryKind> _geoSortByGfxObjRef = [];

    private readonly Dictionary<uint, uint?> _leadDowngradeMannerByGfxObjRef = [];

    private readonly Dictionary<uint, SeeThroughKind> _triMeshBlendByCanvas = [];

    private readonly MoteMeshReferenceLedger? _triMeshReferences;

    private readonly MoteEmitterRetirementLedger _spoutRetirements;

    private RetryableAssetFreeRegister? _teardownAssetList;

    private bool _disposing;

    private bool _destroyed;

    private readonly HashSet<uint> _triMeshPullAskedThisCycle = [];

    private bool _dynamicCycleBegun;

    private BillboardGpuInst[] _instTemp = new BillboardGpuInst[256];

    private MeshMoteGpuInstance[] _triMeshInstTemp = new MeshMoteGpuInstance[256];

    private readonly List<MoteDraw> _paintRosterTemp = new(64);

    private readonly List<MoteInstance> _execTemp = new(64);

    private readonly List<MeshMoteDraw> _meshDrawListScratch = new(64);

    private readonly List<MeshMoteInstance> _triMeshExecTemp = new(64);

    private readonly List<MoteSubmission> _submissionTemp = new(128);

    private readonly List<PreparedMoteAlphaSubmission> _readiedChamberAlphaTemp = new(128);

    private readonly List<RuntimeParticleEmitter> _scopedSpoutTemp = new(64);

    private readonly List<DeferredMoteDraw> _deferredAlpha = new(128);

    private DeferredMoteDraw[] _readiedAlpha = new DeferredMoteDraw[256];

    private uint[] _readiedInstShifts = new uint[256];

    private int _readiedAlphaTally;

    private readonly RetainedScratchCapacityRule _alphaTempRule;

    private sealed class AlphaPaintSource(MotePainter holder) : ICanonAlphaDrawSource
    {
        public void ReadyAlphaDraws(ReadOnlySpan<int> tickets)
            => holder.ReadyPostponedAlphaDraws(tickets);

        public void SketchReadiedAlphaLot(int leadReadiedPaint, int paintTally)
            => holder.PaintReadiedAlphaLot(leadReadiedPaint, paintTally);

        public void RestartAlphaSubmissions()
            => holder.RestartPostponedAlpha();
    }

    internal delegate int ReserveDeferredParticleDraw();

    internal delegate void DrawImmediateParticle(
        Matrix4x4 viewProjection,
        MoteSubmissionKind kind,
        int drawIndex,
        bool opaqueDepthState);

    internal readonly record struct ReadiedChamberAlphaActs(
        bool Retain,
        bool DrawImmediate);

    private const uint NoTextureSlot = 0xFFFFFFFFu;

    private readonly record struct MoteGfxInfo(
        MacAC.Client.Graphics.Gpu.GpuTextureSlot TextureSlot,
        Vector2 Size,
        Vector3 AxisX,
        Vector3 AxisY,
        Vector3 CenterOffset,
        Vector3 SortCenter,
        bool IsBillboard,
        bool Additive,
        bool HasMaterial,
        uint SurfaceId,
        uint DegradeMode)
    {
        public static MoteGfxInfo Default { get; } =
            Billboard(
                MacAC.Client.Graphics.Gpu.GpuTextureSlot.Unassigned,
                Vector2.One,
                Vector3.Zero,
                Vector3.Zero,
                additive: false,
                hasMatl: false,
                canvasIdent: 0);

        public static MoteGfxInfo Billboard(
            MacAC.Client.Graphics.Gpu.GpuTextureSlot textureSocket,
            Vector2 dims,
            Vector3 middleShift,
            Vector3 orderMiddle,
            bool additive,
            bool hasMatl,
            uint canvasIdent)
        {
            return new(
                textureSocket,
                dims,
                Vector3.UnitX,
                Vector3.UnitY,
                middleShift,
                orderMiddle,
                true,
                additive,
                hasMatl,
                canvasIdent,
                DegradeMode: 2u);
        }
    }
}
