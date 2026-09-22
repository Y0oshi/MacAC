using System.Numerics;
using System.Text;
using MacAC.Dat;
using MacAC.Mechanics.Geometry;
using BoundingBox =  MacAC.Dat.Bounds3;
using CullMode =  MacAC.Dat.FaceCulling;

namespace MacAC.Assets.Pak;

public static partial class HarvestedMeshCodec
{
    public static void Write(HarvestedMesh blob, Stream flow)
    {
        using BinaryWriter writer = new BinaryWriter(flow, Encoding.UTF8, leaveOpen: true);
        new Pen(writer, null).Mesh(blob);
    }

    public static void EmitExternalTextures(HarvestedMesh blob, Stream flow, Func<BitmapTag, byte[], ulong> enrollTexture)
    {
        ArgumentNullException.ThrowIfNull(enrollTexture);
        using BinaryWriter writer = new BinaryWriter(flow, Encoding.UTF8, leaveOpen: true);
        new Pen(writer, enrollTexture).Mesh(blob);
    }

    public static HarvestedMesh Read(byte[] octets)
    {
        using MemoryStream flow = new MemoryStream(octets, writable: false);
        using BinaryReader reader = new BinaryReader(flow, Encoding.UTF8, leaveOpen: true);
        return new Scan(reader, null).Mesh();
    }

    public static HarvestedMesh Read(ReadOnlySpan<byte> octets) => Read(octets.ToArray());

    public static HarvestedMesh ScanExternalTextures(byte[] octets, Func<ulong, byte[]> locateTexture)
    {
        ArgumentNullException.ThrowIfNull(locateTexture);
        using MemoryStream flow = new MemoryStream(octets, writable: false);
        using BinaryReader reader = new BinaryReader(flow, Encoding.UTF8, leaveOpen: true);
        return new Scan(reader, locateTexture).Mesh();
    }

    // Serialising half: composite records on top of the primitive layer
    private readonly partial struct Pen(BinaryWriter writer, Func<BitmapTag, byte[], ulong>? textures)
    {
        public void Mesh(HarvestedMesh blob)
        {
            writer.Write(blob.ObjectId);
            writer.Write(blob.IsSetup);
            Blittable(blob.Vertices);

            writer.Write(blob.Batches.Count);
            foreach (MeshHarvestBatch lot in blob.Batches)
                Batch(lot);

            writer.Write(blob.UploadAttempts);

            // Nested env-cell geometry recurses through the same layout
            Presence(blob.EnvCellGeometry);
            if (blob.EnvCellGeometry is { } interior)
                Mesh(interior);

            writer.Write(blob.SetupParts.Count);
            foreach ((ulong gfxObjRefIdent, Matrix4x4 xform) in blob.SetupParts)
            {
                writer.Write(gfxObjRefIdent);
                Matrix(xform);
            }

            writer.Write(blob.ParticleEmitters.Count);
            foreach (QueuedEmitter spout in blob.ParticleEmitters)
                Emitter(spout);

            TextureClusters(blob.TextureBatches);

            Box(blob.BoundingBox);
            Vec(blob.SortCenter);
            writer.Write(blob.DIDDegrade);

            Presence(blob.SelectionSphere);
            if (blob.SelectionSphere is { } orb)
                Ball(orb);

            Blittable(blob.EdgeLines);
        }

        private void Batch(MeshHarvestBatch lot)
        {
            Blittable(lot.Indices);
            Format(lot.TextureFormat);
            Key(lot.TextureKey);
            writer.Write(lot.TextureIndex);
            Cargo(lot.TextureKey, lot.TextureData);
            Optional(lot.UploadPixelFormat is { } format ? (int)format : null);
            Optional(lot.UploadPixelType is { } pt ? (int)pt : null);
            writer.Write((int)lot.CullMode);
        }

        private void TextureLot(TextureHarvestBatch lot)
        {
            Key(lot.Key);
            Cargo(lot.Key, lot.TextureData);
            Optional(lot.UploadPixelFormat is { } format ? (int)format : null);
            Optional(lot.UploadPixelType is { } pt ? (int)pt : null);
            Blittable(lot.Indices);
            writer.Write((int)lot.CullMode);
            writer.Write((int)lot.Translucency);
            writer.Write(lot.IsTransparent);
            writer.Write(lot.IsAdditive);
            writer.Write(lot.HasWrappingUVs);
            writer.Write(lot.SourceSurfaceIndex);
            writer.Write(lot.RetailSurfaceMask);
            writer.Write(lot.RawSurfaceType);
            writer.Write(lot.IsCellShell);
            // Recipe 9 added the authored opacity; recipe 10 the resolved
            // SetSurface state byte. Older payloads never reach this codec -
            // the recipe identity rejects them first.
            writer.Write(lot.SurfaceOpacity);
            writer.Write(lot.MaterialState.ToDenseByte());
        }

        // Groups go out ordered by (width, height, format) so insertion order never leaks into the bytes
        private void TextureClusters(Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>> clusters)
        {
            var ordering = clusters.Keys.ToList();
            ordering.Sort(static (a, b) =>
            {
                int c = a.Width.CompareTo(b.Width);
                if (c is 0) c = a.Height.CompareTo(b.Height);
                return c is 0 ? ((int)a.Format).CompareTo((int)b.Format) : c;
            });

            writer.Write(ordering.Count);
            foreach ((int Width, int Height, TexelLayout Format) form in ordering)
            {
                Format(form);
                var participants = clusters[form];
                writer.Write(participants.Count);
                foreach (TextureHarvestBatch participant in participants)
                    TextureLot(participant);
            }
        }

        private void Cargo(BitmapTag tag, byte[] octets)
        {
            if (textures is null)
                Octets(octets);
            else
                writer.Write(textures(tag, octets));
        }

        private void Emitter(QueuedEmitter spout)
        {
            writer.Write(spout.PartIndex);
            Matrix(spout.Offset);
            Presence(spout.Emitter);
            if (spout.Emitter is { } pe)
                Emitter(pe);
        }

        private void Emitter(EmitterDesc emitter)
        {
            writer.Write(emitter.Id);
            writer.Write(emitter.Category);
            writer.Write(emitter.Version);
            writer.Write((int)emitter.Shape);
            writer.Write((int)emitter.Motion);
            writer.Write(emitter.PartMeshId);
            writer.Write(emitter.HwPartMeshId);
            writer.Write(emitter.Birthrate);
            writer.Write(emitter.MaxParticles);
            writer.Write(emitter.InitialParticles);
            writer.Write(emitter.TotalParticles);
            writer.Write(emitter.TotalSeconds);
            writer.Write(emitter.Lifespan);
            writer.Write(emitter.LifespanRand);
            Vec(emitter.OffsetDir);
            writer.Write(emitter.MinOffset);
            writer.Write(emitter.MaxOffset);
            Vec(emitter.A);
            writer.Write(emitter.MinA);
            writer.Write(emitter.MaxA);
            Vec(emitter.B);
            writer.Write(emitter.MinB);
            writer.Write(emitter.MaxB);
            Vec(emitter.C);
            writer.Write(emitter.MinC);
            writer.Write(emitter.MaxC);
            writer.Write(emitter.StartScale);
            writer.Write(emitter.FinalScale);
            writer.Write(emitter.ScaleRand);
            writer.Write(emitter.StartTrans);
            writer.Write(emitter.FinalTrans);
            writer.Write(emitter.TransRand);
            writer.Write(emitter.IsParentLocal);
        }
    }

    // Parsing half; mirrors Pen field for field
    private readonly partial struct Scan(BinaryReader reader, Func<ulong, byte[]>? textures)
    {
        public HarvestedMesh Mesh()
        {
            HarvestedMesh blob = new HarvestedMesh
            {
                ObjectId = reader.ReadUInt64(),
                IsSetup = reader.ReadBoolean(),
                Vertices = Blittable<VertLocusNormBitmap>(VertLocusNormBitmap.Size),
            };

            int lotTally = reader.ReadInt32();
            var lots = new List<MeshHarvestBatch>(lotTally);
            for (int idx = 0; idx < lotTally; ++idx)
                lots.Add(Batch());
            blob.Batches = lots;

            blob.UploadAttempts = reader.ReadInt32();
            blob.EnvCellGeometry = reader.ReadBoolean() ? Mesh() : null;

            int pieceTally = reader.ReadInt32();
            var pieces = new List<(ulong GfxObjId, Matrix4x4 Transform)>(pieceTally);
            for (int idx = 0; idx < pieceTally; ++idx)
            {
                ulong gfxObjRefIdent = reader.ReadUInt64();
                pieces.Add((gfxObjRefIdent, Matrix()));
            }
            blob.SetupParts = pieces;

            int spoutTally = reader.ReadInt32();
            List<QueuedEmitter> spouts = new List<QueuedEmitter>(spoutTally);
            for (int idx = 0; idx < spoutTally; ++idx)
                spouts.Add(Emitter());
            blob.ParticleEmitters = spouts;

            blob.TextureBatches = TextureClusters();
            blob.BoundingBox = Box();
            blob.SortCenter = Vec();
            blob.DIDDegrade = reader.ReadUInt32();
            blob.SelectionSphere = reader.ReadBoolean() ? Ball() : null;
            blob.EdgeLines = Blittable<Vector3>(12);
            return blob;
        }

        private MeshHarvestBatch Batch()
        {
            ushort[] ordinals = Blittable<ushort>(sizeof(ushort));
            (int, int, TexelLayout) fmt = Format();
            BitmapTag tag = Key();
            MeshHarvestBatch lot = new MeshHarvestBatch
            {
                Indices = ordinals,
                TextureFormat = fmt,
                TextureKey = tag,
                TextureIndex = reader.ReadInt32(),
                TextureData = Cargo(),
            };
            lot.UploadPixelFormat = Optional<PushPixelFmt>();
            lot.UploadPixelType = Optional<PushPixelKind>();
            lot.CullMode = (CullMode)reader.ReadInt32();
            return lot;
        }

        private TextureHarvestBatch TextureLot()
        {
            BitmapTag tag = Key();
            TextureHarvestBatch lot = new TextureHarvestBatch
            {
                Key = tag,
                TextureData = Cargo(),
            };
            lot.UploadPixelFormat = Optional<PushPixelFmt>();
            lot.UploadPixelType = Optional<PushPixelKind>();
            lot.Indices = [.. Blittable<ushort>(sizeof(ushort))];
            lot.CullMode = (CullMode)reader.ReadInt32();
            lot.Translucency = (SeeThroughKind)reader.ReadInt32();
            lot.IsTransparent = reader.ReadBoolean();
            lot.IsAdditive = reader.ReadBoolean();
            lot.HasWrappingUVs = reader.ReadBoolean();
            lot.SourceSurfaceIndex = reader.ReadInt32();
            lot.RetailSurfaceMask = reader.ReadByte();
            lot.RawSurfaceType = reader.ReadUInt32();
            lot.IsCellShell = reader.ReadBoolean();
            lot.SurfaceOpacity = reader.ReadSingle();
            lot.MaterialState = CanonSurfaceMaterialState.FromDenseByte(reader.ReadByte());
            return lot;
        }

        private Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>> TextureClusters()
        {
            int clusterTally = reader.ReadInt32();
            var clusters = new Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>>(clusterTally);
            for (int idx = 0; idx < clusterTally; ++idx)
            {
                (int, int, TexelLayout) form = Format();
                int participantTally = reader.ReadInt32();
                var participants = new List<TextureHarvestBatch>(participantTally);
                for (int jdx = 0; jdx < participantTally; ++jdx)
                    participants.Add(TextureLot());
                clusters[form] = participants;
            }
            return clusters;
        }

        private byte[] Cargo() => textures is null ? Octets() : textures(reader.ReadUInt64());

        private QueuedEmitter Emitter()
        {
            uint pieceOrdinal = reader.ReadUInt32();
            Matrix4x4 shift = Matrix();
            EmitterDesc? emitter = reader.ReadBoolean() ? ParticleEmitter() : null;
            return new QueuedEmitter
            {
                PartIndex = pieceOrdinal,
                Offset = shift,
                Emitter = emitter!,
            };
        }

        private EmitterDesc ParticleEmitter()
        {
            EmitterDesc emitter = new EmitterDesc
            {
                Id = reader.ReadUInt32(),
                Category = reader.ReadUInt32(),
            };
            emitter.Version = reader.ReadUInt32();
            emitter.Shape = (EmitterShape)reader.ReadInt32();
            emitter.Motion = (ParticleMotion)reader.ReadInt32();
            emitter.PartMeshId = reader.ReadUInt32();
            emitter.HwPartMeshId = reader.ReadUInt32();
            emitter.Birthrate = reader.ReadDouble();
            emitter.MaxParticles = reader.ReadInt32();
            emitter.InitialParticles = reader.ReadInt32();
            emitter.TotalParticles = reader.ReadInt32();
            emitter.TotalSeconds = reader.ReadDouble();
            emitter.Lifespan = reader.ReadDouble();
            emitter.LifespanRand = reader.ReadDouble();
            emitter.OffsetDir = Vec();
            emitter.MinOffset = reader.ReadSingle();
            emitter.MaxOffset = reader.ReadSingle();
            emitter.A = Vec();
            emitter.MinA = reader.ReadSingle();
            emitter.MaxA = reader.ReadSingle();
            emitter.B = Vec();
            emitter.MinB = reader.ReadSingle();
            emitter.MaxB = reader.ReadSingle();
            emitter.C = Vec();
            emitter.MinC = reader.ReadSingle();
            emitter.MaxC = reader.ReadSingle();
            emitter.StartScale = reader.ReadSingle();
            emitter.FinalScale = reader.ReadSingle();
            emitter.ScaleRand = reader.ReadSingle();
            emitter.StartTrans = reader.ReadSingle();
            emitter.FinalTrans = reader.ReadSingle();
            emitter.TransRand = reader.ReadSingle();
            emitter.IsParentLocal = reader.ReadBoolean();
            return emitter;
        }
    }
}
