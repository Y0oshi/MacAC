using System.Security.Cryptography;

namespace MacAC.Forge;

// Finds an already-written blob whose bytes are identical to a new payload, so the new key can
// alias it
internal sealed class ByteTwinIndex
{
    private readonly Dictionary<string, List<(ulong Key, byte[] Bytes)>> _bins = new(StringComparer.Ordinal);

    public bool Match(ReadOnlySpan<byte> cargo, out ulong primaryTag)
    {
        if (_bins.TryGetValue(Bin(cargo), out var twins))
        {
            foreach (var (tag, octets) in twins)
            {
                if (cargo.SequenceEqual(octets))
                {
                    primaryTag = tag;
                    return true;
                }
            }
        }

        primaryTag = 0;
        return false;
    }

    public void Remember(ulong primaryTag, byte[] cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        string bin = Bin(cargo);
        if (!_bins.TryGetValue(bin, out var twins))
            _bins.Add(bin, twins = []);
        twins.Add((primaryTag, cargo));
    }

    private static string Bin(ReadOnlySpan<byte> cargo) =>
        Convert.ToHexString(SHA256.HashData(cargo));
}
