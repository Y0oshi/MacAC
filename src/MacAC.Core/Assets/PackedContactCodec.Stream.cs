using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

/// <summary>Primitive encode/decode layer: little-endian scalars and the small geometric records.</summary>
public static partial class PackedContactCodec
{
    // Growable little-endian writer that polls the token every 256 rows
    private sealed partial class Sink(CancellationToken abort)
    {
        private readonly ArrayBufferWriter<byte> _out = new(256);

        public void Header(Kind sort)
        {
            U32(Magic);
            U8((byte)sort);
            U8(SchemaVersion);
            U16(0);
        }

        public void Count(int val, string label)
        {
            if ((uint)val > CeilingRanks)
                throw new InvalidDataException($"{label} count {val} exceeds {CeilingRanks}.");
            Idx32(val);
        }

        // Row-loop cancellation probe; cheap enough to call per row
        public void Beat(int rank)
        {
            if ((rank & 0xFF) is 0)
                abort.ThrowIfCancellationRequested();
        }

        public byte[] Finish()
        {
            abort.ThrowIfCancellationRequested();
            return _out.WrittenSpan.ToArray();
        }

        public void Mark(bool val) => U8(val ? (byte)1 : (byte)0);

        public void Sphere(PackedContactSphere orb)
        {
            Vec(orb.Origin);
            F32(orb.Radius);
        }

        public void Range(PackedIndexRange span)
        {
            Idx32(span.Start);
            Idx32(span.Count);
        }

        public void Plane(Plane plane)
        {
            Vec(plane.Normal);
            F32(plane.D);
        }

        public void Vec(Vector3 v)
        {
            F32(v.X);
            F32(v.Y);
            F32(v.Z);
        }

        public void F32(float val) => Idx32(BitConverter.SingleToInt32Bits(val));

        public void U8(byte val)
        {
            _out.GetSpan(1)[0] = val;
            _out.Advance(1);
        }

        public void U16(ushort val)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_out.GetSpan(sizeof(ushort)), val);
            _out.Advance(sizeof(ushort));
        }

        public void Idx32(int val)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_out.GetSpan(sizeof(int)), val);
            _out.Advance(sizeof(int));
        }

        public void U32(uint val)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_out.GetSpan(sizeof(uint)), val);
            _out.Advance(sizeof(uint));
        }
    }

    private delegate T RowReader<T>(ref Cursor cursor);

    // Bounds-checked little-endian reader over a span
    private ref partial struct Cursor
    {
        private readonly ReadOnlySpan<byte> _octets;
        private readonly CancellationToken _abort;
        private int _at;

        public Cursor(ReadOnlySpan<byte> octets, CancellationToken abort)
        {
            _octets = octets;
            _abort = abort;
            _at = 0;
            abort.ThrowIfCancellationRequested();
        }

        private readonly int Left => _octets.Length - _at;

        public void Header(Kind anticipated)
        {
            uint magic = U32();
            byte sort = U8();
            byte schema = U8();
            ushort reserved = U16();
            HeaderRest(magic, sort, schema, reserved, anticipated);
        }

        private void HeaderRest(uint magic, byte sort, byte schema, ushort reserved, Kind anticipated)
        {
            if (magic != Magic)
                throw new InvalidDataException($"collision payload magic 0x{magic:X8} doesn't match 0x{Magic:X8}.");
            if (sort != (byte)anticipated)
                throw new InvalidDataException($"collision payload kind {sort} doesn't match {(byte)anticipated}.");
            if (schema != SchemaVersion)
                throw new InvalidDataException($"collision payload schema {schema} is not supported");
            if (reserved is not 0)
                throw new InvalidDataException("collision payload reserved header bits are non-zero");
        }

        // Reads a row count and rejects one whose rows could not possibly fit in what is left
        public int Count(string label, int octetsPerRank)
        {
            int tally = I32();
            if (tally < 0 || tally > CeilingRanks)
                throw new InvalidDataException($"{label} count {tally} is beyond [0,{CeilingRanks}].");
            if (octetsPerRank <= 0 || tally > Left / octetsPerRank)
                throw new InvalidDataException($"{label} count {tally} exceeds the remaining payload");
            return tally;
        }

        public readonly void Earmark(long needed, string label)
        {
            if (needed < 0 || needed > Left)
                throw new InvalidDataException($"{label} require {needed} bytes but only {Left} remain");
        }

        public ImmutableArray<T> Ranks<T>(int tally, RowReader<T> scan)
        {
            var ranks = ImmutableArray.CreateBuilder<T>(tally);
            for (int idx = 0; idx < tally; ++idx)
            {
                if ((idx & 0xFF) is 0)
                    _abort.ThrowIfCancellationRequested();
                ranks.Add(scan(ref this));
            }
            return ranks.MoveToImmutable();
        }

        public readonly void Finish()
        {
            _abort.ThrowIfCancellationRequested();
            if (_at != _octets.Length)
                throw new InvalidDataException($"collision payload contains {_octets.Length - _at} trailing byte(s)");
        }

        public bool Tag()
        {
            return U8() switch
            {
                0 => false,
                1 => true,
                byte another => throw new InvalidDataException($"not valid collision boolean value {another}."),
            };
        }

        public PackedContactSphere Sphere() => new(Vec(), F32());

        public PackedIndexRange Range() => new(I32(), I32());

        public Plane Plane() => new(Vec(), F32());

        public Vector3 Vec() => new(F32(), F32(), F32());

        public float F32() => BitConverter.Int32BitsToSingle(I32());

        public byte U8()
        {
            Need(sizeof(byte));
            return _octets[_at++];
        }

        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Grab(sizeof(ushort)));

        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Grab(sizeof(int)));

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Grab(sizeof(uint)));

        private ReadOnlySpan<byte> Grab(int len)
        {
            Need(len);
            var slice = _octets.Slice(_at, len);
            _at += len;
            return slice;
        }

        private readonly void Need(int len)
        {
            if (len < 0 || len > Left)
                throw new EndOfStreamException("collision payload is truncated");
        }
    }
}
