using System.Globalization;
using System.Numerics;
using System.Text;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Testing;

public sealed record CanonWidgetProbeElement(
    int Index,
    int Depth,
    string Path,
    string TypeName,
    string? Name,
    uint EventId,
    uint DatElementId,
    float X,
    float Y,
    float Width,
    float Height,
    bool Visible,
    bool Enabled,
    uint ItemId,
    int SlotIndex,
    GearDragSource? SourceKind)
{
    public int CenterX => (int)MathF.Round(X + Width * 0.5f);
    public int CenterY => (int)MathF.Round(Y + Height * 0.5f);
}

public sealed record CanonWidgetProbeAssertion(bool Success, string Message);

public sealed class CanonWidgetAutopilotProbe(
    WidgetTrunk root,
    ClientThingChart objects,
    Action<string>? trace = null)
{
    private readonly WidgetTrunk _trunk = root ?? throw new ArgumentNullException(nameof(root));
    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly Action<string>? _trace = trace;
    private long _instantMsec = 1;

    public IReadOnlyList<CanonWidgetProbeElement> Snapshot(bool shownSole = true)
    {
        var ranks = new List<CanonWidgetProbeElement>();
        Traverse(_trunk, "root", zDepth: 0, ancestorsShown: true, shownSole, ranks);
        return ranks;
    }

    public string PrintPhrase(bool shownSole = true)
    {
        StringBuilder builder = new StringBuilder();
        foreach (var element in Snapshot(shownSole))
        {
            builder.Append('[').Append(element.Index.ToString(CultureInfo.InvariantCulture)).Append("] ");
            builder.Append(new string(' ', Math.Max(0, element.Depth) * 2));
            builder.Append(element.TypeName);
            if (element.DatElementId is not 0) builder.Append(" dat=0x").Append(element.DatElementId.ToString("X8", CultureInfo.InvariantCulture));
            if (element.EventId is not 0) builder.Append(" event=0x").Append(element.EventId.ToString("X8", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(element.Name)) builder.Append(" name=").Append(element.Name);
            builder.Append(" rect=(").Append(F(element.X)).Append(',').Append(F(element.Y)).Append(',')
              .Append(F(element.Width)).Append(',').Append(F(element.Height)).Append(')');
            if (!element.Visible) builder.Append(" hidden");
            if (!element.Enabled) builder.Append(" disabled");
            if (element.ItemId is not 0)
            {
                builder.Append(" item=0x").Append(element.ItemId.ToString("X8", CultureInfo.InvariantCulture));
                builder.Append(" slot=").Append(element.SlotIndex.ToString(CultureInfo.InvariantCulture));
                if (element.SourceKind is { } src) builder.Append(" source=").Append(src);
            }
            builder.Append(" path=").Append(element.Path);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    public CanonWidgetProbeElement? SeekByDatElemIdent(uint datElemIdent, bool shownSole = true)
    {
        foreach (var element in Snapshot(shownSole))
            if (element.DatElementId == datElemIdent && HasArea(element))
                return element;
        return null;
    }

    public IReadOnlyList<CanonWidgetProbeElement> SeekAllByDatElemIdent(uint datElemIdent, bool shownSole = true)
    {
        var fits = new List<CanonWidgetProbeElement>();
        foreach (var element in Snapshot(shownSole))
            if (element.DatElementId == datElemIdent)
                fits.Add(element);
        return fits;
    }

    public CanonWidgetProbeElement? SeekByGearIdent(
        uint gearOid,
        GearDragSource? srcSort = null,
        bool shownSole = true)
    {
        if (gearOid is 0) return null;
        foreach (var element in Snapshot(shownSole))
        {
            if (element.ItemId != gearOid) continue;
            if (srcSort is { } src && element.SourceKind != src) continue;
            if (!HasArea(element)) continue;
            return element;
        }
        return null;
    }

    public bool PressElem(uint datElemIdent)
    {
        CanonWidgetProbeElement? mark = SeekByDatElemIdent(datElemIdent);
        if (mark is null) return Fail($"element 0x{datElemIdent:X8} not found");
        PressAt(mark.CenterX, mark.CenterY);
        return true;
    }

    public bool PressAtPt(int x, int y)
    {
        PressAt(x, y);
        return true;
    }

    public bool PullAtPt(int beginX, int beginY, int finishX, int finishY)
    {
        PullAt(beginX, beginY, finishX, finishY);
        return true;
    }

    public bool HoverElem(uint datElemIdent)
    {
        CanonWidgetProbeElement? mark = SeekByDatElemIdent(datElemIdent);
        if (mark is null) return Fail($"element 0x{datElemIdent:X8} not found");
        (int x, int y) = CanvasToPane((int)mark.CenterX, (int)mark.CenterY);
        _trunk.OnPointerRelocate(x, y);
        return true;
    }

    public void ShiftPointer(int x, int y) => _trunk.OnPointerRelocate(x, y);

    public bool PressGear(uint gearOid, GearDragSource? srcSort = null)
    {
        CanonWidgetProbeElement? mark = SeekByGearIdent(gearOid, srcSort);
        if (mark is null) return Fail($"item 0x{gearOid:X8} not found");
        PressAt(mark.CenterX, mark.CenterY);
        return true;
    }

    public bool DoublePressGear(uint gearOid, GearDragSource? srcSort = null)
    {
        CanonWidgetProbeElement? mark = SeekByGearIdent(gearOid, srcSort);
        if (mark is null) return Fail($"item 0x{gearOid:X8} not found");
        PressAt(mark.CenterX, mark.CenterY);
        Advance(80);
        PressAt(mark.CenterX, mark.CenterY);
        return true;
    }

    public bool PullGearToElem(uint gearOid, uint datElemIdent, GearDragSource? srcSort = null)
    {
        CanonWidgetProbeElement? src = SeekByGearIdent(gearOid, srcSort);
        if (src is null) return Fail($"drag source item 0x{gearOid:X8} not found");
        CanonWidgetProbeElement? mark = SeekByDatElemIdent(datElemIdent);
        if (mark is null) return Fail($"drop target element 0x{datElemIdent:X8} not found");
        PullAt(src.CenterX, src.CenterY, mark.CenterX, mark.CenterY);
        return true;
    }

    public bool PullGearToGear(uint srcOid, uint markOid, GearDragSource? srcSort = null)
    {
        CanonWidgetProbeElement? src = SeekByGearIdent(srcOid, srcSort);
        if (src is null) return Fail($"drag source item 0x{srcOid:X8} not found");
        CanonWidgetProbeElement? mark = SeekByGearIdent(markOid);
        if (mark is null) return Fail($"drop target item 0x{markOid:X8} not found");
        PullAt(src.CenterX, src.CenterY, mark.CenterX, mark.CenterY);
        return true;
    }

    public bool PullGearBeyond(uint gearOid, int x, int y, GearDragSource? srcSort = null)
    {
        CanonWidgetProbeElement? src = SeekByGearIdent(gearOid, srcSort);
        if (src is null) return Fail($"drag source item 0x{gearOid:X8} not found");
        PullAt(src.CenterX, src.CenterY, x, y);
        return true;
    }

    public CanonWidgetProbeAssertion InsistGear(
        uint gearOid,
        WieldBitmask? equippedLocale = null,
        uint? vesselIdent = null,
        int? socket = null)
    {
        ClientThing? gear = _objects.Get(gearOid);
        if (gear is null)
            return Result(false, $"item 0x{gearOid:X8} missing from object table");

        if (equippedLocale is { } wield && gear.CurrentlyEquippedLocale != wield)
            return Result(false,
                $"item 0x{gearOid:X8} equip expected 0x{((uint)wield):X8}, got 0x{((uint)gear.CurrentlyEquippedLocale):X8}");

        if (vesselIdent is { } vessel && gear.VesselTag != vessel)
            return Result(false,
                $"item 0x{gearOid:X8} container expected 0x{vessel:X8}, got 0x{gear.VesselTag:X8}");

        return socket is { } socketVal && gear.VesselSlot != socketVal
            ? Result(false,
                $"item 0x{gearOid:X8} slot expected {socketVal}, got {gear.VesselSlot}")
            : Result(true, $"item 0x{gearOid:X8} matched object-table assertion");
    }

    private void Traverse(
        WidgetElem elem,
        string trail,
        int zDepth,
        bool ancestorsShown,
        bool shownSole,
        List<CanonWidgetProbeElement> ranks)
    {
        bool netShown = ancestorsShown && elem.Visible;
        if (shownSole && !netShown) return;

        if (elem is WidgetGearRoster roster)
            roster.ArrangementChambers();

        Vector2 spot = elem.MonitorLocus;
        uint gearIdent = 0;
        int socketOrdinal = -1;
        GearDragSource? srcSort = null;
        if (elem is WidgetGearSlot socket)
        {
            gearIdent = socket.GearIdent;
            socketOrdinal = socket.SocketIdx;
            srcSort = socket.SrcSort;
        }

        ranks.Add(new CanonWidgetProbeElement(
            ranks.Count,
            zDepth,
            trail,
            elem.GetType().Name,
            elem.Name,
            elem.SignalIdent,
            elem.DatElemIdent,
            spot.X,
            spot.Y,
            elem.Width,
            elem.Height,
            netShown,
            elem.Enabled,
            gearIdent,
            socketOrdinal,
            srcSort));

        var descendants = elem.Children;
        for (int idx = 0; idx < descendants.Count; ++idx)
        {
            WidgetElem descendant = descendants[idx];
            var descendantTrail = $"{trail}/{descendant.GetType().Name}[{idx}]";
            if (descendant.DatElemIdent is not 0)
                descendantTrail += $"#0x{descendant.DatElemIdent:X8}";
            Traverse(descendant, descendantTrail, zDepth + 1, netShown, shownSole, ranks);
        }
    }

    private (int x, int y) CanvasToPane(int x, int y)
    {
        Vector2 scaling = _trunk.CanvasScale;
        return scaling == Vector2.One
            ? (x, y)
            : ((int)MathF.Round(x * scaling.X), (int)MathF.Round(y * scaling.Y));
    }

    private void PressAt(int x, int y)
    {
        (x, y) = CanvasToPane(x, y);
        Advance(16);
        _trunk.OnPointerRelocate(x, y);
        _trunk.OnPointerDown(WidgetMouseButton.Left, x, y);
        Advance(50);
        _trunk.OnPointerUp(WidgetMouseButton.Left, x, y);
        Advance(16);
    }

    private void PullAt(int beginX, int beginY, int finishX, int finishY)
    {
        (beginX, beginY) = CanvasToPane(beginX, beginY);
        (finishX, finishY) = CanvasToPane(finishX, finishY);
        Advance(16);
        _trunk.OnPointerRelocate(beginX, beginY);
        _trunk.OnPointerDown(WidgetMouseButton.Left, beginX, beginY);
        Advance(16);
        _trunk.OnPointerRelocate(beginX + 5, beginY + 5);
        Advance(16);
        _trunk.OnPointerRelocate(finishX, finishY);
        Advance(16);
        _trunk.OnPointerUp(WidgetMouseButton.Left, finishX, finishY);
        Advance(16);
    }

    private void Advance(int msec)
    {
        _instantMsec += msec;
        _trunk.Tick(msec / 1000.0, _instantMsec);
    }

    private static bool HasArea(CanonWidgetProbeElement elem)
        => elem.Width > 0f && elem.Height > 0f;

    private bool Fail(string msg)
    {
        _trace?.Invoke(msg);
        return false;
    }

    private CanonWidgetProbeAssertion Result(bool success, string msg)
    {
        _trace?.Invoke(msg);
        return new CanonWidgetProbeAssertion(success, msg);
    }

    private static string F(float val)
        => val.ToString("0.##", CultureInfo.InvariantCulture);
}
