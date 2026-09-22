using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Mechanics.Geometry;
using BoundingBox =  MacAC.Dat.Bounds3;
using CullMode =  MacAC.Dat.FaceCulling;

namespace MacAC.Assets;

/// <summary>Vertex format for scenery mesh rendering: position, normal, UV.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VertLocusNormBitmap(Vector3 locus, Vector3 norm, Vector2 uv)
{
    public Vector3 Position = locus;
    public Vector3 Normal = norm;
    public Vector2 UV = uv;

    public static int Size => 8 * sizeof(float); // 3+3+2 = 8 floats = 32 bytes
}

/// <summary>A particle emitter waiting to be attached to a part once the mesh is live.</summary>
public struct QueuedEmitter
{
    public EmitterDesc Emitter;
    public uint PartIndex;
    public Matrix4x4 Offset;
}

/// <summary>Everything harvested from a GfxObj, Setup or EnvCell that the renderer will upload.</summary>
public class HarvestedMesh
{
    private const long RigOverheadOctets = 1024L;

    private long _pushEstimate = -1;

    public ulong ObjectId { get; set; }
    public bool IsSetup { get; set; }
    public VertLocusNormBitmap[] Vertices { get; set; } = [];
    public List<MeshHarvestBatch> Batches { get; set; } = [];

    public int UploadAttempts;

    public HarvestedMesh? EnvCellGeometry { get; set; }

    public List<(ulong GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = [];

    public List<QueuedEmitter> ParticleEmitters { get; set; } = [];

    public Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>> TextureBatches { get; set; } = new();

    public BoundingBox BoundingBox { get; set; }

    public Vector3 SortCenter { get; set; }

    public uint DIDDegrade { get; set; }

    public Orb? SelectionSphere { get; set; }

    public Vector3[] EdgeLines { get; set; } = [];

    /// <summary>Bytes the GPU upload will take, computed once and memoised.</summary>
    public long FetchEstimatedPushOctets()
    {
        long recognized = Volatile.Read(ref _pushEstimate);
        if (recognized >= 0)
            return recognized;

        long octets = IsSetup ? RigOverheadOctets : 0L;
        octets = checked(octets + (long)Vertices.Length * VertLocusNormBitmap.Size);
        foreach (List<TextureHarvestBatch> lots in TextureBatches.Values)
        {
            foreach (TextureHarvestBatch lot in lots)
            {
                octets = checked(octets + lot.TextureData.LongLength);
                octets = checked(octets + (long)lot.Indices.Count * sizeof(ushort));
            }
        }
        if (EnvCellGeometry is not null)
            octets = checked(octets + EnvCellGeometry.FetchEstimatedPushOctets());

        Interlocked.CompareExchange(ref _pushEstimate, octets, -1);
        return Volatile.Read(ref _pushEstimate);
    }
}

/// <summary>CPU-side data for a single rendering batch (indices + texture reference).</summary>
public class MeshHarvestBatch
{
    public ushort[] Indices { get; set; } = [];
    public (int Width, int Height, TexelLayout Format) TextureFormat { get; set; }
    public BitmapTag TextureKey { get; set; }
    public int TextureIndex { get; set; }
    public byte[] TextureData { get; set; } = [];
    public PushPixelFmt? UploadPixelFormat { get; set; }
    public PushPixelKind? UploadPixelType { get; set; }
    public CullMode CullMode { get; set; }
}

public class TextureHarvestBatch
{
    public BitmapTag Key { get; set; }
    public byte[] TextureData { get; set; } = [];
    public PushPixelFmt? UploadPixelFormat { get; set; }
    public PushPixelKind? UploadPixelType { get; set; }
    public List<ushort> Indices { get; set; } = [];
    public CullMode CullMode { get; set; }
    public SeeThroughKind Translucency { get; set; } = SeeThroughKind.Opaque;
    public bool IsTransparent { get; set; }
    public bool IsAdditive { get; set; }
    public bool HasWrappingUVs { get; set; }

    public float SurfaceOpacity { get; set; } = 1f;

    public CanonSurfaceMaterialState MaterialState { get; set; } = CanonSurfaceMaterialState.Opaque;

    public int SourceSurfaceIndex { get; set; } = -1;

    public byte RetailSurfaceMask { get; set; }

    public uint RawSurfaceType { get; set; }

    public bool IsCellShell { get; set; }
}

public static class CellSurfaceGroups
{
    /// <summary>The cell-shell batches of a mesh in the order the retail client drew their surfaces.</summary>
    public static IEnumerable<TextureHarvestBatch> InAscendingCanvasOrdering(HarvestedMesh triMesh)
    {
        var shell = new List<TextureHarvestBatch>();
        foreach (List<TextureHarvestBatch> lots in triMesh.TextureBatches.Values)
        {
            foreach (TextureHarvestBatch lot in lots)
            {
                if (lot.IsCellShell)
                    shell.Add(lot);
            }
        }
        return shell.OrderBy(static batch => batch.SourceSurfaceIndex);
    }
}
