using System.Collections.Concurrent;

namespace MacAC.Client.Graphics.Batching
{
    /// <summary>Resource types for GPU memory tracking.</summary>
    public enum GpuAssetKind
    {
        Texture,
        Buffer,
        Shader,
        VAO,
        FBO,
        RBO,
        Other
    }

    /// <summary>Details about a GPU resource type.</summary>
    public record GpuAssetParticulars(GpuAssetKind Type, int Count, long Bytes);

    /// <summary>Details about a specific named buffer.</summary>
    public record NamedBufferParticulars(string Name, long CapacityBytes, long UsedBytes);

    /// <summary>Tracks manual VRAM allocations for buffers and textures.</summary>
    public static class GpuMemoryLedger
    {
        private static long _allocatedOctets;
        private static readonly long[] _allocatedOctetsByKind = new long[Enum.GetValues<GpuAssetKind>().Length];
        private static readonly int[] _assetCountsByKind = new int[Enum.GetValues<GpuAssetKind>().Length];
        private static readonly ConcurrentDictionary<string, NamedBufferParticulars> _namedBufs = new();

        public static long AllocatedBytes => Interlocked.Read(ref _allocatedOctets);

        public static int VaoTally => _assetCountsByKind[(int)GpuAssetKind.VAO];
        public static int ShaderTally => _assetCountsByKind[(int)GpuAssetKind.Shader];
        public static int BufTally => _assetCountsByKind[(int)GpuAssetKind.Buffer];
        public static int TextureTally => _assetCountsByKind[(int)GpuAssetKind.Texture];
        public static int FboTally => _assetCountsByKind[(int)GpuAssetKind.FBO];
        public static int RboTally => _assetCountsByKind[(int)GpuAssetKind.RBO];

        public static void FollowAlloc(long dimsInOctets, GpuAssetKind kind = GpuAssetKind.Other)
        {
            Interlocked.Add(ref _allocatedOctets, dimsInOctets);
            Interlocked.Add(ref _allocatedOctetsByKind[(int)kind], dimsInOctets);
        }

        public static void FollowDeallocation(long dimsInOctets, GpuAssetKind kind = GpuAssetKind.Other)
        {
            Interlocked.Add(ref _allocatedOctets, -dimsInOctets);
            Interlocked.Add(ref _allocatedOctetsByKind[(int)kind], -dimsInOctets);
        }

        public static void FollowAssetAlloc(GpuAssetKind kind) => Interlocked.Increment(ref _assetCountsByKind[(int)kind]);
        public static void FollowAssetDeallocation(GpuAssetKind kind) => Interlocked.Decrement(ref _assetCountsByKind[(int)kind]);

        public static void FollowNamedBuf(string label, long capOctets, long consumedOctets)
        {
            _namedBufs[label] = new NamedBufferParticulars(label, capOctets, consumedOctets);
        }

        public static void UntrackNamedBuf(string label) => _namedBufs.TryRemove(label, out _);

        public static IEnumerable<NamedBufferParticulars> FetchNamedBufParticulars() => _namedBufs.Values.OrderBy(details => details.Name);

        public static IEnumerable<GpuAssetParticulars> FetchParticulars()
        {
            GpuAssetKind[] kinds = Enum.GetValues<GpuAssetKind>();
            foreach (var kind in kinds)
            {
                yield return new GpuAssetParticulars(
                    kind,
                    _assetCountsByKind[(int)kind],
                    Interlocked.Read(ref _allocatedOctetsByKind[(int)kind])
                );
            }
        }
    }
}
