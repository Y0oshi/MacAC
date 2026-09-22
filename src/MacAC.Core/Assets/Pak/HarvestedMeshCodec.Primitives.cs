using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using BoundingBox =  MacAC.Dat.Bounds3;

namespace MacAC.Assets.Pak;

public static partial class HarvestedMeshCodec
{
    private readonly partial struct Pen
    {
        // One presence byte ahead of an optional block
        private void Presence(object? val) => writer.Write(val is not null);

        // present:byte + value:i32 - a missing value still costs its four bytes
        private void Optional(int? val)
        {
            writer.Write(val.HasValue);
            writer.Write(val.GetValueOrDefault());
        }

        private void Key(BitmapTag tag)
        {
            writer.Write(tag.SurfaceId);
            writer.Write(tag.PaletteId);
            writer.Write((byte)tag.Stippling);
            writer.Write(tag.IsSolid);
        }

        private void Format((int Width, int Height, TexelLayout Format) form)
        {
            writer.Write(form.Width);
            writer.Write(form.Height);
            writer.Write((int)form.Format);
        }

        private void Ball(Orb orb)
        {
            Vec(orb.Center);
            writer.Write(orb.Radius);
        }

        private void Box(BoundingBox bbox)
        {
            Vec(bbox.Min);
            Vec(bbox.Max);
        }

        private void Vec(Vector3 v)
        {
            writer.Write(v.X);
            writer.Write(v.Y);
            writer.Write(v.Z);
        }

        private void Matrix(Matrix4x4 m)
        {
            writer.Write(m.M11); writer.Write(m.M12); writer.Write(m.M13); writer.Write(m.M14);
            writer.Write(m.M21); writer.Write(m.M22); writer.Write(m.M23); writer.Write(m.M24);
            writer.Write(m.M31); writer.Write(m.M32); writer.Write(m.M33); writer.Write(m.M34);
            writer.Write(m.M41); writer.Write(m.M42); writer.Write(m.M43); writer.Write(m.M44);
        }

        // count:i32 followed by the raw element bytes
        private void Blittable<T>(T[] gearList) where T : unmanaged
        {
            writer.Write(gearList.Length);
            if (gearList.Length > 0)
                writer.Write(MemoryMarshal.AsBytes(gearList.AsSpan()));
        }

        private void Blittable<T>(List<T> gearList) where T : unmanaged
        {
            writer.Write(gearList.Count);
            if (gearList.Count > 0)
                writer.Write(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(gearList)));
        }

        private void Octets(byte[] octets)
        {
            writer.Write(octets.Length);
            if (octets.Length > 0)
                writer.Write(octets);
        }
    }

    private readonly partial struct Scan
    {
        private TEnum? Optional<TEnum>() where TEnum : struct, Enum
        {
            bool present = reader.ReadBoolean();
            int val = reader.ReadInt32();
            return present ? (TEnum)Enum.ToObject(typeof(TEnum), val) : null;
        }

        private BitmapTag Key()
        {
            return new()
            {
                SurfaceId = reader.ReadUInt32(),
                PaletteId = reader.ReadUInt32(),
                Stippling = (StippleBits)reader.ReadByte(),
                IsSolid = reader.ReadBoolean(),
            };
        }

        private (int Width, int Height, TexelLayout Format) Format()
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            return (width, height, (TexelLayout)reader.ReadInt32());
        }

        private Orb Ball()
        {
            Vector3 origin = Vec();
            return new Orb { Center = origin, Radius = reader.ReadSingle() };
        }

        private BoundingBox Box()
        {
            Vector3 lower = Vec();
            return new BoundingBox(lower, Vec());
        }

        private Vector3 Vec()
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            return new Vector3(x, y, reader.ReadSingle());
        }

        private Matrix4x4 Matrix()
        {
            return new(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        // count:i32 then count*stride raw bytes, copied straight into a typed array
        private T[] Blittable<T>(int stride) where T : unmanaged
        {
            int tally = reader.ReadInt32();
            if (tally is 0)
                return [];
            T[] gearList = new T[tally];
            reader.ReadBytes(tally * stride).CopyTo(MemoryMarshal.AsBytes(gearList.AsSpan()));
            return gearList;
        }

        private byte[] Octets()
        {
            int tally = reader.ReadInt32();
            return tally is 0 ? [] : reader.ReadBytes(tally);
        }
    }
}
