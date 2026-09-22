using System.Collections.Frozen;

namespace MacAC.Sim.Comms;

public static class CanonChannelTagChart
{
    private const uint Abuse = 0x00000001u;
    private const uint Admin = 0x00000002u;
    private const uint Audit = 0x00000004u;
    private const uint Advocate1 = 0x00000008u;
    private const uint Advocate2 = 0x00000010u;
    private const uint Advocate3 = 0x00000020u;
    private const uint Sentinel = 0x00000200u;
    private const uint Fellowship = 0x00000800u;
    private const uint Vassals = 0x00001000u;
    private const uint Patron = 0x00002000u;
    private const uint Monarch = 0x00004000u;
    private const uint CoVassals = 0x01000000u;
    private const uint Allegiance = 0x02000000u;
    private const uint CelestialHand = 0x08000000u;
    private const uint EldrytchWeb = 0x10000000u;
    private const uint RadiantBlood = 0x20000000u;
    private const uint Olthoi = 0x40000000u;

    // (tag, channel, reachable as a registered verb)
    private static readonly (string Tag, uint Channel, bool Verb)[] Ranks =
    [
        ("abuse", Abuse, false), ("ad", Admin, false), ("admin", Admin, false), ("au", Audit, false), ("audit", Audit, false),
        ("av", Advocate1, false), ("av1", Advocate1, false), ("advocate", Advocate1, false), ("advocate1", Advocate1, false),
        ("av2", Advocate2, false), ("advocate2", Advocate2, false), ("av3", Advocate3, false), ("advocate3", Advocate3, false),
        ("sent", Sentinel, false), ("sentinel", Sentinel, false),
        ("celestialhand", CelestialHand, false), ("celhan", CelestialHand, false),
        ("eldrytchweb", EldrytchWeb, false), ("eldweb", EldrytchWeb, false),
        ("radiantblood", RadiantBlood, false), ("radblo", RadiantBlood, false),
        ("ol", Olthoi, false), ("olthoi", Olthoi, true),
        ("fellowship", Fellowship, true), ("fellow", Fellowship, true), ("fellows", Fellowship, true), ("f", Fellowship, true),
        ("group", Fellowship, true), ("g", Fellowship, true), ("party", Fellowship, true),
        ("vassals", Vassals, true), ("vassal", Vassals, true), ("v", Vassals, true),
        ("patron", Patron, true), ("p", Patron, true),
        ("monarch", Monarch, true), ("m", Monarch, true),
        ("covassals", CoVassals, true), ("covassal", CoVassals, true), ("co-vassals", CoVassals, true), ("c", CoVassals, true),
        ("a", Allegiance, true), ("ab", Allegiance, true), ("allegiance", Allegiance, true),
    ];

    private static readonly FrozenDictionary<string, uint> ByTag =
        Ranks.ToFrozenDictionary(static r => r.Tag, static r => r.Channel, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> VerbTags =
        Ranks.Where(static r => r.Verb).Select(static r => r.Tag).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryResolve(string tag, out uint laneIdent) => ByTag.TryGetValue(tag, out laneIdent);

    public static bool IsUnregisteredBackupTag(string tag) => ByTag.ContainsKey(tag) && !VerbTags.Contains(tag);
}
