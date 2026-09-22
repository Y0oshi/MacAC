using System.Text;

namespace MacAC.Client.Shell.Panels;

public static class CanonStringEscapes
{
    private const string MetaToons = "[]!{}#\\|^$";

    public static string Unescape(string val)
    {
        ArgumentNullException.ThrowIfNull(val);
        int lead = val.IndexOf('\\');
        if (lead < 0)
            return val;

        StringBuilder outcome = new StringBuilder(val.Length);
        for (int idx = 0; idx < val.Length; ++idx)
        {
            idx = UnescapeLoop(val, idx, outcome);
        }
        return outcome.ToString();
    }

    private static int UnescapeLoop(string val, int idx, StringBuilder outcome)
    {
        char latest = val[idx];
        char upcoming = idx + 1 < val.Length ? val[idx + 1] : '\0';
        char unescaped = FetchUnEscapedChar(upcoming);
        if (latest == '\\' && unescaped != '\0')
        {
            outcome.Append(unescaped);
            ++idx;
        }
        else if (latest != '\0')
        {
            outcome.Append(latest);
        }

        return idx;
    }

    public static string Escape(string val)
    {
        ArgumentNullException.ThrowIfNull(val);
        StringBuilder? outcome = null;
        for (int idx = 0; idx < val.Length; ++idx)
        {
            char latest = val[idx];
            char escaped = FetchEscapedChar(latest);
            if (escaped != '\0')
            {
                outcome ??= new StringBuilder(val.Length + 4)
                    .Append(val, 0, idx);
                outcome.Append('\\').Append(escaped);
            }
            else if (latest != '\0')
            {
                outcome?.Append(latest);
            }
        }
        return outcome?.ToString() ?? val;
    }

    internal static char FetchUnEscapedChar(char val)
    {
        return val switch
        {
            'n' => '\n',
            'q' => '"',
            'r' => '\r',
            't' => '\t',
            not '\0' when MetaToons.Contains(val) => val,
            _ => '\0',
        };
    }

    internal static char FetchEscapedChar(char val)
    {
        return val switch
        {
            '\t' => 't',
            '\n' => 'n',
            '\r' => 'r',
            '"' => 'q',
            not '\0' when MetaToons.Contains(val) => val,
            _ => '\0',
        };
    }
}
