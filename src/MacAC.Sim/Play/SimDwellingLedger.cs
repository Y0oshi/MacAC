using System.Globalization;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Shell;
using MacAC.Mechanics.Traits;

namespace MacAC.Sim.Play;

public enum DwellingPanelTextColor
{
    Normal = 0,
    RentPaid = 1,
    RentNotPaid = 2,
}

public readonly record struct DwellingPanelLine(string Text, DwellingPanelTextColor Color);

public sealed class SimDwellingLedger(ClientThingChart? objects = null, TimeProvider? momentSupplier = null)
{
    private const long PurchasePausePeriodSecs = 0x278d00;
    private const long SecsPerDay = 86_400L;
    private const uint ApartmentKind = 4u;

    private readonly ClientThingChart? _objects = objects;
    private readonly TimeProvider _clock = momentSupplier ?? TimeProvider.System;
    private readonly object _latch = new();
    private bool _noticed;
    private bool _owns;
    private PlaySignals.HouseBlob? _house;
    private IReadOnlyList<string> _strokes = [];
    private IReadOnlyList<DwellingPanelLine> _boardStrokes = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_latch) return _strokes; }
    }

    public IReadOnlyList<DwellingPanelLine> BoardStrokes
    {
        get { lock (_latch) return _boardStrokes; }
    }

    /// <summary>The house's position for recall, absent for apartments and unplaced houses.</summary>
    public ObjectCreation.RemotePosition? Position
    {
        get
        {
            lock (_latch)
                return _house is { Type: not ApartmentKind, Position: { LandblockId: not 0u } } blob ? blob.Position : null;
        }
    }

    /// <summary>Whether any of the four House notices (0x0225-0x0228) has arrived this session.</summary>
    public bool HasReceivedNotice
    {
        get { lock (_latch) return _noticed; }
    }

    public void ImposeHouseBlob(PlaySignals.HouseBlob blob, uint selfOid)
    {
        lock (_latch)
        {
            _noticed = true;
            _owns = true;
            _house = blob with { Buy = [.. blob.Buy ?? []], Rent = [.. blob.Rent ?? []] };
            Render(selfOid);
        }
    }

    /// <summary>A new rent period: nothing has been paid towards it yet.</summary>
    public void ImposeRentMoment(uint rentMoment, uint selfOid)
    {
        lock (_latch)
        {
            if (_house is not { } blob)
                return;
            _house = blob with { RentTime = rentMoment, Rent = [.. blob.Rent.Select(static due => due with { Paid = 0 })] };
            Render(selfOid);
        }
    }

    public void ImposeRentPayment(IReadOnlyList<PlaySignals.HouseDue> rent, uint selfOid)
    {
        ArgumentNullException.ThrowIfNull(rent);
        lock (_latch)
        {
            if (_house is not { } blob)
                return;
            _house = blob with { Rent = [.. rent] };
            Render(selfOid);
        }
    }

    public void ImposeHouseCondition(uint weenieProblem, uint selfOid)
    {
        _ = weenieProblem;
        lock (_latch)
        {
            _noticed = true;
            _owns = false;
            _house = null;
            Render(selfOid);
        }
    }

    public void ResetSession()
    {
        lock (_latch)
        {
            _noticed = false;
            _owns = false;
            _house = null;
            _strokes = [];
            _boardStrokes = [];
        }
    }

    private static DwellingPanelLine Plain(string phrase) => new(phrase, DwellingPanelTextColor.Normal);

    private void Render(uint selfOid)
    {
        var strokes = new List<DwellingPanelLine>(_owns ? 8 : 2);
        if (_house is { } house)
            DepictHouse(house, strokes);
        else
            strokes.Add(Plain("You do not currently own a house."));
        strokes.Add(PurchaseRestriction(selfOid));

        _boardStrokes = strokes;
        _strokes = [.. strokes.Select(static stroke => stroke.Text)];
    }

    private void DepictHouse(PlaySignals.HouseBlob house, List<DwellingPanelLine> strokes)
    {
        strokes.Add(Plain("The purchase price for this dwelling is:\n" + Dues(house.Buy, unhidePaid: false)));
        strokes.Add(Plain("Rent:\n" + Dues(house.Rent, unhidePaid: true)));
        strokes.Add(Plain("Bought: " + OwnMoment(house.BuyTime)));

        long period = house.Type == ApartmentKind ? 7_776_000L : 2_592_000L;
        bool paid = house.MaintenanceFree || house.Rent.All(static due => due.Paid >= due.Num);
        strokes.Add(Plain("This maintenance period ends: " + OwnMoment((long)house.RentTime + period)));
        strokes.Add(Plain("Maintenance is next due: " + OwnMoment((long)house.RentTime + (paid ? 2L : 1L) * period)));

        if (house.Type != ApartmentKind && RadarCoords.TryFromChamber(house.Position.LandblockId, out RadarCoords coords))
            strokes.Add(Plain($"Location: {coords.YPhrase}, {coords.XPhrase}"));

        strokes.Add(paid
            ? new DwellingPanelLine(
                "The maintenance has already been paid for this period. You may not prepay next period's maintenance.",
                DwellingPanelTextColor.RentPaid)
            : new DwellingPanelLine(
                "Warning!  You have not paid your maintenance costs for the last "
                + (period / SecsPerDay).ToString(CultureInfo.InvariantCulture)
                + " day maintenance period.  Please pay these costs by this deadline"
                + " or you will lose your house, and all your items within it.",
                DwellingPanelTextColor.RentNotPaid));
    }

    // The line about when another landscape house may be bought, based on the last purchase timestamp
    private DwellingPanelLine PurchaseRestriction(uint selfOid)
    {
        int purchased = _objects?.Get(selfOid)?.Properties.FetchInt((uint)TraitInt.HousePurchaseTimestamp) ?? 0;
        long instant = _clock.GetUtcNow().ToUnixTimeSeconds();
        if (instant - purchased > PurchasePausePeriodSecs)
        {
            return Plain(_owns
                ? "You may buy another house immediately after you abandon this one."
                : "You may buy another house immediately.");
        }

        DateTime expiry = ToOwn(DateTimeOffset.FromUnixTimeSeconds(purchased + PurchasePausePeriodSecs));
        return Plain(
            "You may buy another landscape house at " + expiry.ToString(CultureInfo.CurrentCulture)
            + ". This restriction does not apply to apartments.");
    }

    private static string Dues(IReadOnlyList<PlaySignals.HouseDue> dues, bool unhidePaid)
    {
        if (dues.Count is 0)
            return string.Empty;

        string[] pieces = new string[dues.Count];
        for (int idx = 0; idx < pieces.Length; ++idx)
        {
            var due = dues[idx];
            string tally = unhidePaid
                ? $"{due.Paid.ToString(CultureInfo.InvariantCulture)}/{due.Num.ToString(CultureInfo.InvariantCulture)}"
                : due.Num.ToString(CultureInfo.InvariantCulture);
            pieces[idx] = tally + " " + Noun(due);
        }
        return string.Join(", ", pieces);
    }

    // Singular for one, the server's plural when given, else a naive English plural
    private static string Noun(PlaySignals.HouseDue due)
    {
        if (due.Num is 1)
            return due.Name;
        if (!string.IsNullOrEmpty(due.PluralName))
            return due.PluralName;
        return due.Name.EndsWith('s') || due.Name.EndsWith('x') ? due.Name + "es" : due.Name + "s";
    }

    private DateTime ToOwn(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _clock.LocalTimeZone).DateTime;

    private string OwnMoment(long epochSecs)
    {
        return epochSecs is 0L ? "N/A" : ToOwn(DateTimeOffset.FromUnixTimeSeconds(epochSecs)).ToString(CultureInfo.CurrentCulture);
    }
}
