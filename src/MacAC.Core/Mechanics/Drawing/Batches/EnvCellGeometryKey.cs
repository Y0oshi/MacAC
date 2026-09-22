namespace MacAC.Mechanics.Drawing.Batches;

public static class EnvCellGeometryKey
{
    private const ulong CargoBitmask = 0x0FFF_FFFD_FFFF_FFFFUL;
    private const ulong TagNamespace = 0xE000_0002_0000_0000UL;
    private const ulong LegacyNamespace = 0x2_0000_0000UL;

    public static ulong Compute(uint surroundingsIdent, ushort chamberStructure, IReadOnlyList<ushort> canvases)
    {
        Fnv1a64 fnv = new Fnv1a64();
        fnv.Add(surroundingsIdent);
        fnv.Add(chamberStructure);
        fnv.Add(checked((uint)canvases.Count));
        foreach (ushort canvas in canvases)
            fnv.Add(canvas);
        return (fnv.Digest & CargoBitmask) | TagNamespace;
    }

    /// <summary>The older polynomial hash, kept for prepared-package compatibility.</summary>
    public static ulong CalculateLegacyRealmBuilder(uint surroundingsIdent, ushort chamberStructure, IReadOnlyList<ushort> canvases)
    {
        long digest = 17;
        digest = unchecked(digest * 31 + surroundingsIdent);
        digest = unchecked(digest * 31 + chamberStructure);
        foreach (ushort canvas in canvases)
            digest = unchecked(digest * 31 + canvas);
        return (ulong)digest | LegacyNamespace;
    }

    private struct Fnv1a64
    {
        private const ulong ShiftBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _digest = ShiftBasis;

        public Fnv1a64()
        {
        }

        public readonly ulong Digest => _digest;

        public void Add(ushort val)
        {
            Octet((byte)val);
            Octet((byte)(val >> 8));
        }

        public void Add(uint val)
        {
            for (int shift = 0; shift < 32; shift += 8)
                Octet((byte)(val >> shift));
        }

        private void Octet(byte val)
        {
            _digest ^= val;
            _digest = unchecked(_digest * Prime);
        }
    }
}
