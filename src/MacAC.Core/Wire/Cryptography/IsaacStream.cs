namespace MacAC.Wire.Cryptography;

public sealed class IsaacStream
{
    private const int Words = 256;
    private const uint Phi = 0x9E3779B9u;

    private readonly uint[] _phase = new uint[Words];
    private readonly uint[] _chunk = new uint[Words];
    private uint _a, _b, _c;
    private int _cur;

    public IsaacStream(ReadOnlySpan<byte> seedBytes)
    {
        if (seedBytes.Length < 4)
            throw new ArgumentException("seed has to be no fewer than 4 bytes", nameof(seedBytes));

        SeedPhase();
        _a = _b = _c = (uint)seedBytes[0] | ((uint)seedBytes[1] << 8) | ((uint)seedBytes[2] << 16) | ((uint)seedBytes[3] << 24);
        Churn();
        _cur = Words - 1;
    }

    public uint Next()
    {
        uint word = _chunk[_cur];
        if (_cur > 0)
        {
            --_cur;
        }
        else
        {
            Churn();
            _cur = Words - 1;
        }
        return word;
    }

    // The standard 8-word avalanche step
    private static void Stir(Span<uint> s)
    {
        s[0] ^= s[1] << 11; s[3] += s[0]; s[1] += s[2];
        s[1] ^= s[2] >> 2; s[4] += s[1]; s[2] += s[3];
        s[2] ^= s[3] << 8; s[5] += s[2]; s[3] += s[4];
        s[3] ^= s[4] >> 16; s[6] += s[3]; s[4] += s[5];
        s[4] ^= s[5] << 10; s[7] += s[4]; s[5] += s[6];
        s[5] ^= s[6] >> 4; s[0] += s[5]; s[6] += s[7];
        s[6] ^= s[7] << 8; s[1] += s[6]; s[7] += s[0];
        s[7] ^= s[0] >> 9; s[2] += s[7]; s[0] += s[1];
    }

    private void SeedPhase()
    {
        Span<uint> s = stackalloc uint[8];
        s.Fill(Phi);
        for (int round = 0; round < 4; ++round)
            Stir(s);

        // Two passes: the first folds the (all-zero) result block in, the
        // second folds the freshly written state back into itself.
        Fold(s, _chunk);
        Fold(s, _phase);
    }

    private void Fold(Span<uint> s, uint[] src)
    {
        for (int at = 0; at < Words; at += 8)
        {
            for (int kdx = 0; kdx < 8; ++kdx)
                s[kdx] += src[at + kdx];
            Stir(s);
            s.CopyTo(_phase.AsSpan(at, 8));
        }
    }

    private void Churn()
    {
        ++_c;
        _b += _c;
        for (int idx = 0; idx < Words; ++idx)
        {
            uint x = _phase[idx];
            _a ^= (idx & 3) switch
            {
                0 => _a << 13,
                1 => _a >> 6,
                2 => _a << 2,
                _ => _a >> 16,
            };
            _a += _phase[(idx + 128) & 0xFF];

            uint y = _phase[(int)((x >> 2) & 0xFF)] + _a + _b;
            _phase[idx] = y;
            _b = _phase[(int)((y >> 10) & 0xFF)] + x;
            _chunk[idx] = _b;
        }
    }
}
