using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed class CanonAssayNamePicker(
    IReadOnlyDictionary<uint, string> materials,
    CreatureDisplayNamePicker creatures)
{
    private const uint MatlClientEnum = 0x10000001u;
    private const uint MatlSubEnum = 1u;

    public static CanonAssayNamePicker Empty { get; } = new(
        new Dictionary<uint, string>(),
        new CreatureDisplayNamePicker(new Dictionary<uint, string>()));

    private readonly IReadOnlyDictionary<uint, string> _matls = materials
            ?? throw new ArgumentNullException(nameof(materials));
    private readonly CreatureDisplayNamePicker _beasts = creatures
            ?? throw new ArgumentNullException(nameof(creatures));

    public static CanonAssayNamePicker Load(
        IDatAccess datFiles,
        CreatureDisplayNamePicker beasts)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(beasts);

        var matls = new Dictionary<uint, string>();
        uint masterDid = (uint)datFiles.Portal.Db.Header.MasterMapId;
        IdNameMap? master = masterDid is 0u
            ? null
            : datFiles.Get<IdNameMap>(masterDid);
        if (master is not null
            && master.ClientEnumToId.TryGetValue(
                MatlClientEnum,
                out uint matlTrunkLookupDid)
            && datFiles.Get<IdNameMap>(matlTrunkLookupDid) is { } matlTrunkLookup
            && matlTrunkLookup.ClientEnumToId.TryGetValue(
                MatlSubEnum,
                out uint matlLookupDid)
            && datFiles.Get<DualIdNameMap>(matlLookupDid) is { } matlLookup)
        {
            foreach ((uint ident, string phrase)
                     in matlLookup.ClientEnumToName)
            {
                matls.TryAdd(ident, Standardize(phrase));
            }
        }

        return new CanonAssayNamePicker(matls, beasts);
    }

    public string LocateBeast(int beastKind)
        => _beasts.Resolve(beastKind);

    public string LocateLineage(int lineageCluster)
    {
        return ToonIdentityText.LineageClusterReadoutLabel(lineageCluster) ?? string.Empty;
    }

    public string LocateMatl(int matlKind)
    {
        return matlKind > 0
               && _matls.TryGetValue((uint)matlKind, out string? label)
                ? label
                : string.Empty;
    }

    public string LocateAppropriateLabel(ClientThing objRef)
    {
        ArgumentNullException.ThrowIfNull(objRef);

        string baseLabel = objRef.FetchAppropriateLabel();
        if (objRef.MaterialType is not { } matlKind)
            return baseLabel;

        string matl = LocateMatl(unchecked((int)matlKind));
        if (string.IsNullOrEmpty(matl))
            return baseLabel;

        string remainder = baseLabel
            .Replace(matl, string.Empty, StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrEmpty(remainder)
            ? matl
            : $"{matl} {remainder}";
    }

    private static string Standardize(string val)
        => val.Replace('_', ' ');
}
