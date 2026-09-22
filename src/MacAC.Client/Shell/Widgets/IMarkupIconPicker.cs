using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public interface IMarkupIconPicker
{
    (uint tex, int w, int h) LocateDid(uint did);

    (uint tex, int w, int h) LocateArcanum(uint arcanumIdent);

    (uint tex, int w, int h) LocateGear(uint objectIdent);
}

public sealed class CanonMarkupIconPicker(
    IDatAccess dats,
    GlyphComposer icons,
    ClientThingChart objects) : IMarkupIconPicker
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly GlyphComposer _glyphs = icons ?? throw new ArgumentNullException(nameof(icons));
    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));

    private const int UpperStashedMisses = 256;

    private readonly Dictionary<uint, (uint tex, int w, int h)> _settledDidStash = [];

    private readonly Queue<uint> _missOrdering = new();

    public (uint tex, int w, int h) LocateDid(uint did)
    {
        if (did is 0u)
            return (0u, 0, 0);
        if (_settledDidStash.TryGetValue(did, out (uint tex, int w, int h) stashed))
            return stashed;

        (uint tex, int w, int h) outcome;
        bool isMiss;
        if (!_datFiles.Portal.TryGet<Bitmap>(did, out _)
            && !_datFiles.HighRes.TryGet<Bitmap>(did, out _))
        {
            outcome = (0u, 0, 0);
            isMiss = true;
        }
        else
        {
            outcome = _glyphs.FetchKeyedGlyph(did);
            isMiss = outcome.tex == 0u;
        }

        _settledDidStash[did] = outcome;
        if (isMiss)
        {
            _missOrdering.Enqueue(did);
            if (_missOrdering.Count > UpperStashedMisses)
                _settledDidStash.Remove(_missOrdering.Dequeue());
        }
        return outcome;
    }

    public (uint tex, int w, int h) LocateArcanum(uint arcanumIdent)
    {
        if (arcanumIdent is 0u) return (0u, 0, 0);
        uint bmp = _glyphs.FetchArcanumGlyph(arcanumIdent);
        return bmp is 0u ? (0u, 0, 0) : (bmp, 32, 32);
    }

    public (uint tex, int w, int h) LocateGear(uint objectIdent)
    {
        if (objectIdent is 0u) return (0u, 0, 0);
        var gear = _objects.Get(objectIdent);
        if (gear is null || gear.IconId is 0u) return (0u, 0, 0);
        uint bmp = _glyphs.FetchGlyph(
            gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
        return bmp is 0u ? (0u, 0, 0) : (bmp, 32, 32);
    }
}
