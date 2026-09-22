using System.Globalization;
using MacAC.Mechanics.Shell;

namespace MacAC.Wire.Messages;

public static partial class CommandReplies
{
    public static IReadOnlyList<string>? DecodeLaneOrdinal(ReadOnlySpan<byte> cargo) => Texts(cargo);

    public static IReadOnlyList<string>? DecodeLaneRoster(ReadOnlySpan<byte> cargo) => Texts(cargo);

    public static IEnumerable<string> ComposeLaneOrdinalStrokes(IReadOnlyList<string> lanes) =>
        lanes.Prepend("The following channels are available to you:");

    public static IEnumerable<string> ComposeLaneRosterStrokes(IReadOnlyList<string> labels)
    {
        return labels.Prepend("The following characters are currently listening on the channel:");
    }

    public static HousesAvailableReply? DecodeOnHandHouses(ReadOnlySpan<byte> cargo)
    {
        try
        {
            WireCursor cursor = new WireCursor(cargo);
            uint houseKind = cursor.Word();
            uint tally = cursor.Word();
            uint[] locales = new uint[tally];
            for (uint idx = 0; idx < tally; ++idx)
                locales[idx] = cursor.Word();
            return new HousesAvailableReply(houseKind, locales, unchecked((int)cursor.Word()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public readonly record struct HousesAvailableReply(uint HouseType, IReadOnlyList<uint> Locations, int TotalAvailable);

    public static IEnumerable<string> ComposeOnHandHousesStrokes(HousesAvailableReply response)
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"There are {response.TotalAvailable} {HouseKindLabel(response.HouseType)} available.");
        if (response.HouseType == ApartmentHouseKind)
            yield break;

        foreach (uint lbIdent in response.Locations)
        {
            if (RadarCoords.TryFromChamber(lbIdent, out var coords))
                yield return $"     {coords.YPhrase}, {coords.XPhrase}";
        }
        if (response.TotalAvailable > HouseRosterCap)
            yield return "There were too many houses to display all the locations. Only the first 400 locations are displayed here.";
    }

    private const int HouseRosterCap = 0x190;
    private const uint ApartmentHouseKind = 4u;

    // count, then that many String16Ls
    private static List<string>? Texts(ReadOnlySpan<byte> cargo)
    {
        try
        {
            WireCursor cursor = new WireCursor(cargo);
            uint tally = cursor.Word();
            List<string> roster = new List<string>();
            for (uint idx = 0; idx < tally; ++idx)
                roster.Add(cursor.String16L());
            return roster;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string HouseKindLabel(uint houseKind)
    {
        return houseKind switch
        {
            1u => "cottages",
            2u => "villas",
            3u => "mansions",
            4u => "apartments",
            _ => "",
        };
    }
}
