using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Drawing.Batches;
using BoundingBox =  MacAC.Dat.Bounds3;
using CullMode =  MacAC.Dat.FaceCulling;
using Sphere =  MacAC.Dat.Orb;

namespace MacAC.Assets;

public sealed partial class MeshHarvester
{
    private HarvestedMesh? HarvestOutlines(ulong ident, Dictionary<uint, ShellCell> chamberStructs, Matrix4x4 xform, CancellationToken token)
    {
        if (chamberStructs.Count is 0)
            return null;

        Extent reach = new Extent();
        List<Vector3> strokes = new List<Vector3>();
        foreach (ShellCell chamberStruct in chamberStructs.Values)
        {
            foreach (Vector3 pt in OutlineBuilder.AssembleRimStrokes(chamberStruct))
                strokes.Add(Vector3.Transform(pt, xform));
            foreach (MeshVertex vert in chamberStruct.Vertices.ByIndex.Values)
                reach.Shield(Vector3.Transform(vert.Position, xform));
        }
        if (strokes.Count is 0)
            return null;

        BoundingBox bbox = new BoundingBox(reach.Min, reach.Max);
        byte[] wipe = TextureTools.BuildSolidTintTexture(new Argb { Alpha = 0, Red = 255, Green = 255, Blue = 255 }, 1, 1);
        BitmapTag placeholderTag = new BitmapTag
        {
            SurfaceId = 0xFFFFFFFF,
            PaletteId = 0,
            Stippling = StippleBits.NoPos,
            IsSolid = true,
        };

        // The GPU path wants at least one batch; a degenerate triangle on a
        // 1×1 clear texel satisfies it without drawing anything.
        return new HarvestedMesh
        {
            ObjectId = ident,
            IsSetup = false,
            Vertices = [new VertLocusNormBitmap { Position = Vector3.Zero, Normal = Vector3.UnitZ, UV = Vector2.Zero }],
            Batches =
            [
                new MeshHarvestBatch
                {
                    Indices = [0, 0, 0],
                    TextureFormat = (1, 1, TexelLayout.RGBA8),
                    TextureKey = placeholderTag,
                    TextureIndex = 0,
                    TextureData = wipe,
                    UploadPixelFormat = PushPixelFmt.Rgba,
                    UploadPixelType = PushPixelKind.UnsignedByte,
                    CullMode = CullMode.None,
                },
            ],
            TextureBatches = new Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>>
            {
                [(1, 1, TexelLayout.RGBA8)] =
                [
                    new TextureHarvestBatch
                    {
                        Indices = [0, 0, 0],
                        Key = placeholderTag,
                        TextureData = wipe,
                        UploadPixelFormat = PushPixelFmt.Rgba,
                        UploadPixelType = PushPixelKind.UnsignedByte,
                        CullMode = CullMode.None,
                        IsTransparent = false, // opaque pass, but the texel itself is clear
                    },
                ],
            },
            BoundingBox = bbox,
            SelectionSphere = new Sphere { Center = bbox.Center, Radius = Vector3.Distance(bbox.Max, bbox.Min) / 2.0f },
            EdgeLines = [.. strokes],
        };
    }
}
