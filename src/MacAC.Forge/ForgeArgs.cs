using System.Globalization;

namespace MacAC.Forge;

// A parsed macac-bake command line
internal sealed record ForgeInvocation(
    string DatDirectory,
    string OutputPath,
    HashSet<uint>? IdFilter,
    HashSet<uint>? LandblockFilter,
    int Threads,
    bool ProgressJson);

internal static class ForgeArgs
{
    internal const string Usage =
        "usage: macac-bake --dat-dir <path> [--out <file>] "
        + "[--ids 0xId,0xId,...] [--landblocks 0xId,...] "
        + "[--threads <n>] [--progress-json]\n"
        + "       macac-bake --help";

    public static bool WantsHelp(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments is ["--help" or "-h"];
    }

    // Parses the arguments; on a usage error the complaint goes to problem and null comes back
    public static ForgeInvocation? Read(IReadOnlyList<string> arguments, TextWriter problem)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(problem);

        string? datFolder = null;
        string? productTrail = null;
        HashSet<uint>? idents = null;
        HashSet<uint>? lbs = null;
        int threads = Environment.ProcessorCount;
        bool headwayJson = false;

        Cursor cur = new Cursor(arguments);
        while (cur.Next(out string bit))
        {
            switch (bit)
            {
                case "--dat-dir":
                    datFolder = cur.Value();
                    break;
                case "--out":
                    productTrail = cur.Value();
                    break;
                case "--ids":
                    idents = HexRoster(cur.Value(), problem);
                    break;
                case "--landblocks":
                    lbs = HexRoster(cur.Value(), problem);
                    break;
                case "--threads":
                    if (int.TryParse(cur.Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num)
                        && num > 0)
                    {
                        threads = num;
                    }
                    break;
                case "--progress-json":
                    headwayJson = true;
                    break;
                default:
                    problem.WriteLine($"unrecognized argument: {bit}");
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(datFolder))
        {
            problem.WriteLine(Usage);
            return null;
        }

        return new ForgeInvocation(
            datFolder,
            productTrail ?? Path.Combine(datFolder, "macac.pak"),
            idents,
            lbs,
            threads,
            headwayJson);
    }

    private static HashSet<uint> HexRoster(string? raw, TextWriter problem)
    {
        HashSet<uint> decoded = new HashSet<uint>();
        if (string.IsNullOrWhiteSpace(raw))
            return decoded;

        foreach (string ticket in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string digits = ticket.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ticket[2..] : ticket;
            if (uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint ident))
                decoded.Add(ident);
            else
                problem.WriteLine($"warning: could not parse id '{ticket}' - skipped");
        }

        return decoded;
    }

    // Walks the argument list; Value consumes the flag's operand when there is one
    private sealed class Cursor(IReadOnlyList<string> arguments)
    {
        private int _at = -1;

        public bool Next(out string bit)
        {
            if (++_at < arguments.Count)
            {
                bit = arguments[_at];
                return true;
            }

            bit = string.Empty;
            return false;
        }

        public string? Value() => _at + 1 < arguments.Count ? arguments[++_at] : null;
    }
}
