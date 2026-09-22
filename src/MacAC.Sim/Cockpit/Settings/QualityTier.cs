using System.Globalization;

namespace MacAC.Cockpit.Settings;

public enum QualityTier { Low, Medium, High, Ultra }

public readonly record struct QualityKnobs(int NearRadius, int FarRadius, int MsaaSamples, int AnisotropicLevel, bool AlphaToCoverage, int MaxCompletionsPerFrame)
{
    public static QualityKnobs From(QualityTier preset)
    {
        return preset switch
        {
            QualityTier.Low => new(2, 5, 0, 4, false, 2),
            QualityTier.Medium => new(3, 8, 2, 8, false, 3),
            QualityTier.High => new(4, 12, 4, 16, true, 4),
            QualityTier.Ultra => new(5, 15, 4, 16, true, 6),
            _ => From(QualityTier.High),
        };
    }

    public static QualityKnobs WithEnvironSubstitutions(QualityKnobs basePrefs)
    {
        return new(
        EnvironInt("MACAC_NEAR_RADIUS", basePrefs.NearRadius),
        EnvironInt("MACAC_FAR_RADIUS", basePrefs.FarRadius),
        EnvironInt("MACAC_MSAA_SAMPLES", basePrefs.MsaaSamples),
        EnvironInt("MACAC_ANISOTROPIC", basePrefs.AnisotropicLevel),
        EnvironSwitch("MACAC_A2C", basePrefs.AlphaToCoverage),
        EnvironInt("MACAC_MAX_COMPLETIONS_PER_FRAME", basePrefs.MaxCompletionsPerFrame));
    }

    private static int EnvironInt(string label, int backup)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(label), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) ? val : backup;
    }

    // Anything but empty, "0" or "false" (any case) switches it on
    private static bool EnvironSwitch(string label, bool backup)
    {
        return Environment.GetEnvironmentVariable(label) switch
        {
            null or "" => backup,
            "0" or "false" or "False" or "FALSE" => false,
            _ => true,
        };
    }
}
