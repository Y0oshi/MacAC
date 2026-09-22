using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public sealed class ToonTitlePicker(IDatAccess dats)
{
    public const uint BannerEnumMapperIdent = 0x22000041u;

    public const uint BannerStringChartIdent = 0x2300000Eu;

    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly DatStringPicker _texts = new DatStringPicker(dats);
    private readonly Dictionary<uint, string?> _settledStash = [];
    private NameMap? _bannerEnumMapper;
    private bool _fetchedMapper;

    public string? Resolve(uint bannerIdent)
    {
        if (bannerIdent is 0u)
            return null;

        if (_settledStash.TryGetValue(bannerIdent, out string? stashed))
            return stashed;

        if (!_fetchedMapper)
        {
            _datFiles.Portal.TryGet<NameMap>(BannerEnumMapperIdent, out _bannerEnumMapper);
            _fetchedMapper = true;
        }

        string? settled = null;
        if (_bannerEnumMapper is not null
            && _bannerEnumMapper.Names.TryGetValue(bannerIdent, out var rawLabelVal))
        {
            string rawLabel = rawLabelVal.ToString();
            if (!string.IsNullOrEmpty(rawLabel))
                settled = _texts.Resolve(BannerStringChartIdent, DatStringPicker.CalculateDigest(rawLabel));
        }

        _settledStash[bannerIdent] = settled;
        return settled;
    }
}
