using System.Globalization;

namespace MacAC.Client.Graphics.Tenancy;

public sealed record TenancyAllowanceKnobs(
    long ObjectMeshGpuBytes,
    int ObjectMeshUnownedEntries,
    long PreparedMeshCpuBytes,
    int PreparedMeshCpuEntries,
    long MeshStagingBytes,
    int MeshStagingEntries,
    long CompositePhysicalBytes,
    long CompositeUnownedBytes,
    long StandaloneUnownedBytes,
    int StandaloneUnownedEntries,
    long AnimationBytes,
    int AnimationEntries,
    long AudioBytes,
    long AlphaScratchBytes)
{
    public const long MiB = 1024L * 1024L;

    public static TenancyAllowanceKnobs Default { get; } = new(
        ObjectMeshGpuBytes: 1024 * MiB,
        ObjectMeshUnownedEntries: 50,
        PreparedMeshCpuBytes: 128 * MiB,
        PreparedMeshCpuEntries: 100,
        MeshStagingBytes: 128 * MiB,
        MeshStagingEntries: 256,
        CompositePhysicalBytes: 128 * MiB,
        CompositeUnownedBytes: 64 * MiB,
        StandaloneUnownedBytes: 32 * MiB,
        StandaloneUnownedEntries: 256,
        AnimationBytes: 64 * MiB,
        AnimationEntries: 512,
        AudioBytes: 32 * MiB,
        AlphaScratchBytes: 16 * MiB);

    internal static TenancyAllowanceKnobs Decode(
        Func<string, string?> environ)
    {
        ArgumentNullException.ThrowIfNull(environ);
        var defaults = Default;
        return new TenancyAllowanceKnobs(
            ObjectMeshGpuBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_MESH_GPU_MIB"),
                defaults.ObjectMeshGpuBytes),
            ObjectMeshUnownedEntries: DecodeTally(
                environ("MACAC_RESIDENCY_MESH_UNOWNED_ENTRIES"),
                defaults.ObjectMeshUnownedEntries),
            PreparedMeshCpuBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_PREPARED_MESH_MIB"),
                defaults.PreparedMeshCpuBytes),
            PreparedMeshCpuEntries: DecodeTally(
                environ("MACAC_RESIDENCY_PREPARED_MESH_ENTRIES"),
                defaults.PreparedMeshCpuEntries),
            MeshStagingBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_MESH_STAGING_MIB"),
                defaults.MeshStagingBytes),
            MeshStagingEntries: DecodeTally(
                environ("MACAC_RESIDENCY_MESH_STAGING_ENTRIES"),
                defaults.MeshStagingEntries),
            CompositePhysicalBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_COMPOSITE_PHYSICAL_MIB"),
                defaults.CompositePhysicalBytes),
            CompositeUnownedBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_COMPOSITE_UNOWNED_MIB"),
                defaults.CompositeUnownedBytes),
            StandaloneUnownedBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_STANDALONE_UNOWNED_MIB"),
                defaults.StandaloneUnownedBytes),
            StandaloneUnownedEntries: DecodeTally(
                environ("MACAC_RESIDENCY_STANDALONE_UNOWNED_ENTRIES"),
                defaults.StandaloneUnownedEntries),
            AnimationBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_ANIMATION_MIB"),
                defaults.AnimationBytes),
            AnimationEntries: DecodeTally(
                environ("MACAC_RESIDENCY_ANIMATION_ENTRIES"),
                defaults.AnimationEntries),
            AudioBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_AUDIO_MIB"),
                defaults.AudioBytes),
            AlphaScratchBytes: DecodeMiB(
                environ("MACAC_RESIDENCY_ALPHA_SCRATCH_MIB"),
                defaults.AlphaScratchBytes));
    }

    private static long DecodeMiB(string? val, long backup)
    {
        return !long.TryParse(
                val,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long mebibytes)
            || mebibytes <= 0
            || mebibytes > long.MaxValue / MiB
            ? backup
            : checked(mebibytes * MiB);
    }

    private static int DecodeTally(string? val, int backup)
    {
        return int.TryParse(
            val,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int tally)
        && tally > 0
            ? tally
            : backup;
    }
}
