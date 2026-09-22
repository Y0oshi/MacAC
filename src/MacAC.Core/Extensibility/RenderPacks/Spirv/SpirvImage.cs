using System.Buffers.Binary;
using System.Text;

namespace MacAC.Extensibility.RenderPacks.Spirv;

internal enum SpirvStorageClass : uint
{
    UniformConstant = 0,
    Uniform = 2,
    PushConstant = 9,
    StorageBuffer = 12,
}

internal enum SpirvDecoration : uint
{
    Block = 2,
    ColMajor = 5,
    ArrayStride = 6,
    MatrixStride = 7,
    NonWritable = 24,
    Binding = 33,
    DescriptorSet = 34,
    Offset = 35,
}

internal readonly record struct SpirvEntryPoint(uint ExecutionModel, string Name);

internal readonly record struct SpirvVariable(uint ResultType, uint Id, SpirvStorageClass StorageClass);

internal sealed class SpirvImage
{
    private const uint MagicWord = 0x0723_0203;
    private const int PreambleWords = 5;

    private static class Op
    {
        public const uint EntryPoint = 15;
        public const uint TypeInt = 21;
        public const uint TypeFloat = 22;
        public const uint TypeVector = 23;
        public const uint TypeMatrix = 24;
        public const uint TypeImage = 25;
        public const uint TypeSampledImage = 27;
        public const uint TypeArray = 28;
        public const uint TypeRuntimeArray = 29;
        public const uint TypeStruct = 30;
        public const uint TypePointer = 32;
        public const uint Constant = 43;
        public const uint Variable = 59;
        public const uint Decorate = 71;
        public const uint MemberDecorate = 72;
        public const uint ImageWrite = 99;
    }

    private sealed record TypeNode(uint Opcode, uint[] Operands);

    private readonly List<SpirvEntryPoint> _listingPts = [];
    private readonly List<SpirvVariable> _variables = [];
    private readonly Dictionary<uint, uint> _descriptorSets = [];
    private readonly Dictionary<uint, uint> _bindings = [];
    private readonly HashSet<uint> _nonWritable = [];
    private readonly HashSet<uint> _chunks = [];
    private readonly Dictionary<uint, TypeNode> _kinds = [];
    private readonly Dictionary<uint, uint> _constants = [];
    private readonly Dictionary<(uint Id, SpirvDecoration Decoration), uint> _decorations = [];
    private readonly Dictionary<(uint Id, int Member, SpirvDecoration Decoration), uint> _participantDecorations = [];

    private SpirvImage()
    {
    }

    internal IReadOnlyList<SpirvEntryPoint> EntryPoints => _listingPts;

    internal IReadOnlyList<SpirvVariable> Variables => _variables;

    internal bool WritesImages { get; private set; }

    // Decode a module
    internal static SpirvImage? TryUnpack(ReadOnlySpan<byte> octets, out string? miss)
    {
        miss = null;
        if (octets.Length < PreambleWords * 4 || (octets.Length & 3) is not 0)
        {
            miss = "the module is not a word-aligned SPIR-V binary";
            return null;
        }

        uint[] words = new uint[octets.Length / 4];
        for (int idx = 0; idx < words.Length; ++idx)
            words[idx] = BinaryPrimitives.ReadUInt32LittleEndian(octets.Slice(idx * 4, 4));
        if (words[0] != MagicWord)
        {
            miss = "the module does not have the SPIR-V magic word";
            return null;
        }

        SpirvImage image = new SpirvImage();
        int cur = PreambleWords;
        while (cur < words.Length)
        {
            uint preamble = words[cur];
            int len = (int)(preamble >> 16);
            if (len <= 0 || cur + len > words.Length)
            {
                miss = $"malformed SPIR-V instruction at word {cur}";
                return null;
            }

            try
            {
                image.Absorb(preamble & 0xffff, words.AsSpan(cur, len));
            }
            catch (InvalidDataException problem)
            {
                miss = problem.Message;
                return null;
            }

            cur += len;
        }

        return image;
    }

    internal bool TryDescriptorSocket(uint variableIdent, out uint set, out uint mapping)
    {
        mapping = 0;
        return _descriptorSets.TryGetValue(variableIdent, out set)
            && _bindings.TryGetValue(variableIdent, out mapping);
    }

    internal bool IsChunk(uint structIdent) => _chunks.Contains(structIdent);

    internal bool PtsAtSingleChunk(SpirvVariable variable)
    {
        return TryPointeeStruct(variable, out uint ident, out _) && _chunks.Contains(ident);
    }

    internal bool IsScanSoleDepot(SpirvVariable variable)
    {
        if (_nonWritable.Contains(variable.Id))
            return true;
        if (!TryPointeeStruct(variable, out uint ident, out uint[] participants) || participants.Length is 0)
            return false;
        for (int participant = 0; participant < participants.Length; ++participant)
        {
            if (ParticipantDecoration(ident, participant, SpirvDecoration.NonWritable) is null)
                return false;
        }
        return true;
    }

    internal bool TryPointeeStruct(SpirvVariable variable, out uint ident, out uint[] participants)
    {
        ident = 0;
        participants = [];
        if (!TryPointee(variable.ResultType, out ident))
            return false;
        if (!_kinds.TryGetValue(ident, out TypeNode? joint) || joint.Opcode != Op.TypeStruct)
            return false;
        participants = joint.Operands[1..];
        return true;
    }

    // A runtime array of combined 2-D-array samplers: the global texture table
    internal bool IsSampledTextureChart(SpirvVariable variable)
    {
        if (!TryPointee(variable.ResultType, out uint arrIdent))
            return false;
        if (!Node(arrIdent, Op.TypeRuntimeArray, preciseOperands: 2, out TypeNode? arr))
            return false;
        if (!Node(arr.Operands[1], Op.TypeSampledImage, preciseOperands: 2, out TypeNode? sampled))
            return false;
        if (!_kinds.TryGetValue(sampled.Operands[1], out TypeNode? image)
            || image.Opcode != Op.TypeImage
            || image.Operands.Length < 8)
            return false;
        // Dim = 2D (1), Arrayed = true, Sampled = used with a sampler (1)
        return image.Operands[2] is 1 && image.Operands[4] is 1 && image.Operands[6] is 1;
    }

    internal uint? ParticipantShift(uint ident, int participant) =>
        ParticipantDecoration(ident, participant, SpirvDecoration.Offset);

    internal uint? ParticipantDecoration(uint ident, int participant, SpirvDecoration decoration)
    {
        return _participantDecorations.TryGetValue((ident, participant, decoration), out uint val) ? val : null;
    }

    internal bool IsFloat32(uint ident) => IsScalar(ident, Op.TypeFloat, 32, signed: null);

    internal bool IsInt32(uint ident, bool signed) => IsScalar(ident, Op.TypeInt, 32, signed);

    internal bool IsFloatVector(uint ident, uint lanes)
    {
        return IsVector(ident, lanes, static (image, elem) => image.IsFloat32(elem));
    }

    internal bool IsUIntVector(uint ident, uint lanes)
    {
        return IsVector(ident, lanes, static (image, elem) => image.IsInt32(elem, signed: false));
    }

    internal bool IsFloatMatrix(uint ident, uint ranks, uint columns)
    {
        return Node(ident, Op.TypeMatrix, preciseOperands: 3, out TypeNode? matrix)
        && matrix.Operands[2] == columns
        && IsFloatVector(matrix.Operands[1], ranks);
    }

    internal bool IsArrOf(uint ident, uint len, uint stride, Func<SpirvImage, uint, bool> elem)
    {
        return Node(ident, Op.TypeArray, preciseOperands: 3, out TypeNode? arr)
        && _constants.TryGetValue(arr.Operands[2], out uint actualLen)
        && actualLen == len
        && _decorations.TryGetValue((ident, SpirvDecoration.ArrayStride), out uint actualStride)
        && actualStride == stride
        && elem(this, arr.Operands[1]);
    }

    private void Absorb(uint opcode, ReadOnlySpan<uint> words)
    {
        switch (opcode)
        {
            case Op.EntryPoint:
                Demand(words, 4, "malformed SPIR-V OpEntryPoint");
                _listingPts.Add(new SpirvEntryPoint(words[1], ScanLiteralString(words[3..])));
                break;

            case >= Op.TypeInt and <= Op.TypePointer:
                Demand(words, 2, "malformed SPIR-V type instruction");
                _kinds[words[1]] = new TypeNode(opcode, words[1..].ToArray());
                break;

            case Op.Constant when words.Length >= 4:
                _constants[words[2]] = words[3];
                break;

            case Op.Variable:
                Demand(words, 4, "malformed SPIR-V OpVariable");
                _variables.Add(new SpirvVariable(words[1], words[2], (SpirvStorageClass)words[3]));
                break;

            case Op.Decorate:
                Demand(words, 3, "malformed SPIR-V OpDecorate");
                CaptureDecoration(words[1], (SpirvDecoration)words[2], words.Length >= 4 ? words[3] : 1u);
                break;

            case Op.MemberDecorate:
                Demand(words, 4, "malformed SPIR-V OpMemberDecorate");
                _participantDecorations[(words[1], checked((int)words[2]), (SpirvDecoration)words[3])] =
                    words.Length >= 5 ? words[4] : 1u;
                break;

            case Op.ImageWrite:
                WritesImages = true;
                break;
        }
    }

    private void CaptureDecoration(uint mark, SpirvDecoration decoration, uint val)
    {
        _decorations[(mark, decoration)] = val;
        switch (decoration)
        {
            case SpirvDecoration.DescriptorSet:
                _descriptorSets[mark] = val;
                break;
            case SpirvDecoration.Binding:
                _bindings[mark] = val;
                break;
            case SpirvDecoration.NonWritable:
                _nonWritable.Add(mark);
                break;
            case SpirvDecoration.Block:
                _chunks.Add(mark);
                break;
        }
    }

    private static void Demand(ReadOnlySpan<uint> words, int floor, string msg)
    {
        if (words.Length < floor)
            throw new InvalidDataException(msg);
    }

    private static string ScanLiteralString(ReadOnlySpan<uint> words)
    {
        List<byte> octets = new List<byte>(words.Length * 4);
        foreach (uint word in words)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                byte octet = (byte)(word >> shift);
                if (octet is 0)
                    return Encoding.UTF8.GetString([.. octets]);
                octets.Add(octet);
            }
        }

        throw new InvalidDataException("unterminated SPIR-V string");
    }

    private bool TryPointee(uint ptrIdent, out uint pointee)
    {
        pointee = 0;
        if (!_kinds.TryGetValue(ptrIdent, out TypeNode? ptr)
            || ptr.Opcode != Op.TypePointer
            || ptr.Operands.Length < 3)
            return false;
        pointee = ptr.Operands[2];
        return true;
    }

    private bool Node(uint ident, uint opcode, int preciseOperands, out TypeNode joint)
    {
        joint = null!;
        if (!_kinds.TryGetValue(ident, out TypeNode? located)
            || located.Opcode != opcode
            || located.Operands.Length != preciseOperands)
            return false;
        joint = located;
        return true;
    }

    private bool IsScalar(uint ident, uint opcode, uint width, bool? signed)
    {
        if (!_kinds.TryGetValue(ident, out TypeNode? joint)
            || joint.Opcode != opcode
            || joint.Operands.Length < 2
            || joint.Operands[1] != width)
            return false;
        return signed is not { } wantSigned
            || (joint.Operands.Length >= 3 && joint.Operands[2] == (wantSigned ? 1u : 0u));
    }

    private bool IsVector(uint ident, uint lanes, Func<SpirvImage, uint, bool> elem)
    {
        return Node(ident, Op.TypeVector, preciseOperands: 3, out TypeNode? vector)
        && vector.Operands[2] == lanes
        && elem(this, vector.Operands[1]);
    }
}
