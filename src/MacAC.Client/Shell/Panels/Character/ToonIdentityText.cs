namespace MacAC.Client.Shell.Panels;

internal static class ToonIdentityText
{
    public const uint GenderPropIdent = 0x71u;
    public const uint LineageClusterPropIdent = 0xBCu;

    public static string StatPreambleStroke(ToonSheet sheet)
    {
        return string.IsNullOrWhiteSpace(sheet.Gender) ? Merge(sheet.Heritage, sheet.Title) : Merge(sheet.Gender, sheet.Heritage, sheet.Title);
    }

    public static string? GenderReadoutLabel(int gender)
    {
        return gender switch
        {
            1 => "Male",
            2 => "Female",
            _ => null,
        };
    }

    public static string? LineageClusterReadoutLabel(int lineageCluster)
    {
        return lineageCluster switch
        {
            1 => "Aluvian",
            2 => "Gharu'ndim",
            3 => "Sho",
            4 => "Viamontian",
            5 => "Umbraen",
            6 => "Gearknight",
            7 => "Tumerok",
            8 => "Lugian",
            9 => "Empyrean",
            10 => "Penumbraen",
            11 => "Undead",
            12 => "Olthoi",
            13 => "Olthoi",
            _ => null,
        };
    }

    public static string GenderLineageReadout(
        int gender,
        int lineageCluster,
        string? beastKindBackup)
    {
        string? lineage = lineageCluster is 0
            ? beastKindBackup
            : LineageClusterReadoutLabel(lineageCluster);
        return Merge(GenderReadoutLabel(gender), lineage);
    }

    private static string Merge(params string?[] pieces)
    {
        return string.Join(" ", pieces
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));
    }
}
