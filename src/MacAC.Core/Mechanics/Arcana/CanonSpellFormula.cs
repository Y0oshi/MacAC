using System.Text;

namespace MacAC.Mechanics.Arcana;

public static class CanonSpellFormula
{
    private const uint LeadTaper = 63u;
    private const uint PrismaticTaper = 0xBCu;
    private const uint TaperTally = 12u;
    private const int EquationSockets = 8;

    public static uint DetermineStrengthTierOfModule(uint moduleIdent)
    {
        return moduleIdent switch
        {
            >= 1u and <= 6u => moduleIdent,
            0x6Eu => 7u,
            0x70u => 8u,
            0xC0u => 9u,
            0xC1u => 10u,
            _ => 0u,
        };
    }

    public static int InqArcanumTierByRoughHeuristic(IReadOnlyList<uint> modules)
    {
        uint strength = modules.Count is 0 ? 0u : DetermineStrengthTierOfModule(modules[0]);
        return strength switch
        {
            < 7u => (int)strength,
            >= 9u => (int)strength - 2,
            _ => (int)strength - 1,
        };
    }

    /// <summary>The targeting type comes from the last non-zero component in slots 4..7.</summary>
    public static uint FetchTargetingKind(IReadOnlyList<uint> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count <= 4)
            return 0u;
        int previous = 4;
        while (previous < 7 && previous + 1 < modules.Count && modules[previous + 1] is not 0u)
            ++previous;
        return FetchMarkKindFromModuleIdent(modules[previous]);
    }

    public static uint FetchMarkKindFromModuleIdent(uint moduleIdent)
    {
        return moduleIdent switch
        {
            0x31u or 0x32u or 0x33u or 0x34u or 0x35u or 0x36u
                or 0x37u or 0x38u or 0x3Cu or 0x3Du or 0x3Eu or 0xBEu => 0x10u,
            0x39u => 0x00088B8Fu,
            0x3Bu => 0x10010000u,
            _ => 0u,
        };
    }

    /// <summary>Keeps only the scarabs, then pads with prismatic tapers by the strongest scarab's power.</summary>
    public static IReadOnlyList<uint> InqScarabSoleEquation(IReadOnlyList<uint> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        List<uint> scarabs = new List<uint>(EquationSockets);
        uint strongest = 0u;
        for (int idx = 0; idx < Math.Min(EquationSockets, modules.Count); ++idx)
        {
            uint component = modules[idx];
            if (component is 0u)
                break;
            if (!IsScarab(component))
                continue;
            scarabs.Add(component);
            strongest = Math.Max(strongest, DetermineStrengthTierOfModule(component));
        }

        int tapers = strongest switch
        {
            1u => 1,
            2u => 2,
            3u or 4u or 7u => 3,
            5u or 6u or 8u or 9u or 10u => 4,
            _ => 0,
        };
        while (tapers-- > 0 && scarabs.Count < EquationSockets)
            scarabs.Add(PrismaticTaper);
        return scarabs;
    }

    /// <summary>The client's name hash over the Windows-1252 bytes of the account name.</summary>
    public static uint CalculateLabelDigest(string val)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        long digest = 0;
        foreach (byte raw in Encoding.GetEncoding(1252).GetBytes(val ?? string.Empty))
        {
            digest = unchecked((sbyte)raw) + (digest << 4);
            if ((digest & 0xF0000000L) is not 0)
                digest = (digest ^ ((digest & 0xF0000000L) >> 24)) & 0x0FFFFFFF;
        }
        return unchecked((uint)digest);
    }

    public static IReadOnlyList<uint> CustomizeForAcct(IReadOnlyList<uint> modules, uint equationVer, string acctLabel)
    {
        uint[] sockets = modules.Take(EquationSockets).ToArray();
        return equationVer switch
        {
            1u => ScrambleV1(sockets, acctLabel),
            2u => ScrambleV2(sockets, acctLabel),
            3u => ScrambleV3(sockets, acctLabel),
            _ => sockets,
        };
    }

    private static bool IsScarab(uint component)
    {
        return component is (>= 1u and <= 6u) or 0x6Eu or 0x6Fu or 0x70u or 0xC0u or 0xC1u;
    }

    private static uint Taper(uint val) => val % TaperTally + LeadTaper;

    private static IReadOnlyList<uint> ScrambleV1(uint[] c, string acctLabel)
    {
        int tally = c.Count(v => v != 0);
        if (tally < 5)
            return c;

        uint seed = CalculateLabelDigest(acctLabel) % 0x13D573u;
        uint scarab = c[0];
        int herbAt = tally > 5 ? 2 : 1;
        uint herb = c[herbAt];
        int powderAt = herbAt + 1 + (tally > 6 ? 1 : 0);
        if (powderAt + 1 >= c.Length)
            return c;
        uint powder = c[powderAt];
        uint potion = c[powderAt + 1];
        int talismanAt = powderAt + 2 + (tally > 7 ? 1 : 0);
        if (talismanAt >= c.Length)
            return c;
        uint talisman = c[talismanAt];

        if (tally > 5)
            c[1] = Taper(unchecked(powder + 2u * herb + potion + talisman + scarab));
        if (tally > 6)
        {
            uint denominator = unchecked(scarab + powder + potion);
            if (denominator is not 0)
                c[3] = Taper(unchecked((scarab + herb + talisman + 2u * (powder + potion)) * (seed / denominator)));
        }
        if (tally > 7)
        {
            uint denominator = unchecked(talisman + scarab);
            if (denominator is not 0)
                c[6] = Taper(unchecked((powder + 2u * talisman + potion + herb + scarab) * (seed / denominator)));
        }
        return c;
    }

    private static IReadOnlyList<uint> ScrambleV2(uint[] c, string acctLabel)
    {
        if (c.Length < 8)
            return c;
        uint seed = CalculateLabelDigest(acctLabel) % 0x13D573u;
        uint p1 = c[0], c4 = c[4], x = c[5], a = c[7];
        c[3] = Taper(unchecked(a + 3u * p1 + 2u * c4 * x + c[2] + c[1]));
        uint denominator = unchecked(c[1] * a + 2u * c4);
        if (denominator is not 0)
            c[6] = Taper(unchecked((a + 3u * p1 * c[2] + 2u * x + c4) * (seed / denominator)));
        return c;
    }

    private static IReadOnlyList<uint> ScrambleV3(uint[] c, string acctLabel)
    {
        if (c.Length < 7)
            return c;
        uint digest = CalculateLabelDigest(acctLabel);
        uint h0 = unchecked(digest % 0x13D573u + c[0]) % TaperTally;
        uint h1 = unchecked(digest % 0x4AEFDu + c[1]) % TaperTally;
        uint h2 = unchecked(digest % 0x96A7Fu + c[2]) % TaperTally;
        uint h4 = unchecked(digest % 0x100A03u + c[4]) % TaperTally;
        uint h5 = unchecked(digest % 0xEB2EFu + c[5]) % TaperTally;
        uint h7 = unchecked(digest % 0x121E7Du + (c.Length > 7 ? c[7] : 0u)) % TaperTally;
        c[3] = Taper(unchecked(h0 + h1 + h2 + h4 + h5 + h2 * h5 + h0 * h1 + h7 * (h4 + 1u)));
        c[6] = Taper(unchecked(h0 + h1 + h2 + h4 + digest % 0x65039u % TaperTally
            + h7 * (h4 * (h0 * h1 * h2 * h5 + 7u) + 1u)
            + h5 + 5u * h0 * h1 + 11u * h2 * h5));
        return c;
    }
}
