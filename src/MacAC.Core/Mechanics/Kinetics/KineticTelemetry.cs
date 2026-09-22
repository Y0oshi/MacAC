using System.Globalization;
using System.Numerics;
using System.Text;

namespace MacAC.Mechanics.Kinetics;

public static partial class KineticTelemetry
{

    public static void TraceDistantWarp(
        uint oid,
        string cause,
        bool tapRan,
        string stanceCondition)
    {
        if (!ProbeRemoteTeleportEnabled) return;
        Console.WriteLine(FormattableString.Invariant(
            $"[remote-teleport] guid=0x{oid:X8} cause={cause} hookRan={tapRan} placement={stanceCondition}"));
    }

    public static void TraceChamberSetAssemble(
        uint seedChamberIdent,
        Vector3 orbMiddle,
        IReadOnlyCollection<uint> chamberSet)
    {
        if (!ProbeCellSetEnabled) return;
        StringBuilder idents = new StringBuilder();
        bool lead = true;
        foreach (uint ident in chamberSet)
        {
            if (!lead) idents.Append(',');
            idents.Append(FormattableString.Invariant($"0x{ident:X8}"));
            lead = false;
        }
        Console.WriteLine(FormattableString.Invariant(
            $"[cellset-build] seed=0x{seedChamberIdent:X8} sphere=({orbMiddle.X:F3},{orbMiddle.Y:F3},{orbMiddle.Z:F3}) count={chamberSet.Count} ids={idents}"));
    }

    public static void TracePolyPrint(uint chamberIdent, SettledPolygon poly)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        StringBuilder builder = new StringBuilder(256);
        builder.AppendFormat(info,
            "[poly-dump] cell=0x{0:X8} polyId=0x{1:X4} numPts={2} sides={3} " +
            "n=({4:F6},{5:F6},{6:F6}) d={7:F6} verts=[",
            chamberIdent, poly.Id, poly.NumPoints, poly.SidesType,
            poly.Plane.Normal.X, poly.Plane.Normal.Y, poly.Plane.Normal.Z, poly.Plane.D);
        TracePolyPrintRest(info, builder, poly);
    }

    private static void TracePolyPrintRest(CultureInfo info, StringBuilder builder, SettledPolygon poly)
    {
        for (int idx = 0; idx < poly.Vertices.Length; ++idx)
        {
            if (idx > 0) builder.Append(',');
            builder.AppendFormat(info, "({0:F6},{1:F6},{2:F6})",
                poly.Vertices[idx].X, poly.Vertices[idx].Y, poly.Vertices[idx].Z);
        }
        builder.Append(']');
        Console.WriteLine(builder.ToString());
    }

    public static void TraceWarp(string pt, uint ident, string extra = "")
    {
        if (!ProbeTeleportEnabled) return;
        Console.WriteLine(FormattableString.Invariant(
            $"[tp-probe] {pt,-6} id=0x{ident:X8} t={System.Environment.TickCount64} {extra}"));
    }

    public static int CollisionShadowSampleEvery { get; set; } = DecodePositiveInt(Env("MACAC_COLLISION_SHADOW_EVERY"));

    public static string ImpactShadeArtifactFolder { get; set; } =
        Env("MACAC_COLLISION_SHADOW_DIR") ?? Path.Combine(System.Environment.CurrentDirectory, ".test-out", "collision-shadow");

    public static bool ProbeResolveEnabled { get; set; } = Mark("MACAC_PROBE_RESOLVE");
    public static bool ProbeCellEnabled { get; set; } = Mark("MACAC_PROBE_CELL");
    public static bool ProbeChildCellEnabled { get; set; } = Mark("MACAC_PROBE_CHILD_CELL");
    public static bool ProbeParkEnabled { get; set; } = Mark("MACAC_PROBE_PARK");
    public static bool ProbeWorldFrameEnabled { get; set; } = Mark("MACAC_PROBE_WORLD_FRAME");
    public static bool DumpMotionEnabled { get; set; } = Mark("MACAC_DUMP_MOTION");
    public static bool ProbeBuildingEnabled { get; set; } = Mark("MACAC_PROBE_BUILDING");
    public static bool ProbeCellSetEnabled { get; set; } = Mark("MACAC_PROBE_CELLSET");
    public static bool ProbeRemoteTeleportEnabled { get; set; } = Mark("MACAC_PROBE_REMOTE_TELEPORT");
    public static bool ProbeUseabilityFallbackEnabled { get; set; } = Mark("MACAC_PROBE_USEABILITY_FALLBACK");
    public static bool DumpSteepRoofEnabled { get; set; } = Mark("MACAC_DUMP_STEEP_ROOF");
    public static bool ProbeIndoorBspEnabled { get; set; } = Mark("MACAC_PROBE_INDOOR_BSP");
    public static bool ProbeCellCacheEnabled { get; set; } = Mark("MACAC_PROBE_CELL_CACHE");
    public static bool ProbeContactPlaneEnabled { get; set; } = Mark("MACAC_PROBE_CONTACT_PLANE");
    public static bool ProbePushBackEnabled { get; set; } = Mark("MACAC_PROBE_PUSH_BACK");
    public static bool ProbePolyDumpEnabled { get; set; } = Mark("MACAC_PROBE_POLY_DUMP");
    public static bool ProbePlacementFailEnabled { get; set; } = Mark("MACAC_PROBE_PLACEMENT_FAIL");
    public static bool ProbeSweptEnabled { get; set; } = Mark("MACAC_PROBE_SWEPT");
    public static bool ProbeJumpEnabled { get; set; } = Mark("MACAC_PROBE_JUMP");
    public static bool ProbeTeleportEnabled { get; set; } = Mark("MACAC_PROBE_TELEPORT");
    public static bool ProbeLocalTeleportEnabled { get; set; } = Mark("MACAC_PROBE_LOCAL_TELEPORT");
    public static bool ProbeStepWalkEnabled { get; set; } = Mark("MACAC_PROBE_STEP_WALK");

    public static string OwnWarpHubSort { get; set; } = "graphical";

    public static SettledPolygon? PreviousBspStrikePoly { get; set; }

    private const string DefaultPrintFolder = "tests/MacAC.Mechanics.Tests/Fixtures/cellar-ascent";

    public static IReadOnlySet<uint> ProbeDumpCellIds { get; set; } = DecodeHexIdentRoster(Env("MACAC_DUMP_CELLS"));
    public static string ProbeDumpCellsPath { get; set; } = Env("MACAC_DUMP_CELLS_DIR") ?? DefaultPrintFolder;
    public static bool ProbeDumpCellsEnabled => ProbeDumpCellIds.Count > 0;

    public static IReadOnlySet<uint> ProbeDumpGfxObjIds { get; set; } = DecodeHexIdentRoster(Env("MACAC_DUMP_GFXOBJS"));
    public static string ProbeDumpGfxObjsPath { get; set; } = Env("MACAC_DUMP_GFXOBJS_DIR") ?? DefaultPrintFolder;
    public static bool ProbeDumpGfxObjsEnabled => ProbeDumpGfxObjIds.Count > 0;

    public static void TraceOwnWarpArrival(
        string cause,
        string stanceCondition,
        long gatewayGen,
        ushort warpSeries,
        uint destChamber,
        uint settledChamber,
        bool tapRearRan,
        bool leashLoaded,
        bool autorunCancelled)
    {
        if (!ProbeLocalTeleportEnabled) return;
        string tapRearPhrase = tapRearRan ? "ran" : "skipped";
        string leashPhrase = leashLoaded ? "armed" : "unarmed";
        string autorunPhrase = autorunCancelled ? "cancelled" : "unchanged";
        Console.WriteLine(FormattableString.Invariant(
            $"[local-tp] cause={cause} host={OwnWarpHubSort} status={stanceCondition} gen={gatewayGen} seq={warpSeries} dest=0x{destChamber:X8} resolved=0x{settledChamber:X8} hookTail={tapRearPhrase} leash={leashPhrase} autorun={autorunPhrase}"));
    }

    public static void TraceStanceFail(
        string src,
        Vector3 orbMiddle,
        float radius,
        int orbIndex,
        uint chamberIdent,
        Vector3 realmOrigin,
        bool ethereal)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        string polyDsc = PreviousStanceFailSolidLeaf
            ? "solid_leaf=1"
            : PreviousStanceFailPolyIdent is not 0
                ? string.Format(info, "polyId=0x{0:X4} n=({1:F4},{2:F4},{3:F4}) d={4:F4}",
                    PreviousStanceFailPolyIdent,
                    PreviousStanceFailPolyNorm.X, PreviousStanceFailPolyNorm.Y, PreviousStanceFailPolyNorm.Z,
                    PreviousStanceFailPolyD)
                : "no_poly_info";

        Console.WriteLine(string.Format(info,
            "[place-fail] source={0} cell=0x{1:X8} sphere=({2:F4},{3:F4},{4:F4}) r={5:F4} " +
            "sphereIdx={6} worldOrigin=({7:F4},{8:F4},{9:F4}) ethereal={10} {11}",
            src, chamberIdent, orbMiddle.X, orbMiddle.Y, orbMiddle.Z, radius,
            orbIndex, realmOrigin.X, realmOrigin.Y, realmOrigin.Z, ethereal, polyDsc));
    }

    public static void RewindForTest()
    {
        ProbeResolveEnabled = false;
        ProbeCellEnabled = false;
        ProbeParkEnabled = false;
        ProbeBuildingEnabled = false;
        RewindForTestRest();
    }

    private static void RewindForTestRest()
    {
        ProbeCellSetEnabled = false;
        ProbeUseabilityFallbackEnabled = false;
        DumpSteepRoofEnabled = false;
        RewindForTestTail();
    }

    private static void RewindForTestTail()
    {
        ProbeIndoorBspEnabled = false;
        ProbeCellCacheEnabled = false;
        ProbeContactPlaneEnabled = false;
        ProbePushBackEnabled = false;
        RewindForTestCoda();
    }

    private static void RewindForTestCoda()
    {
        ProbePolyDumpEnabled = false;
        ProbePlacementFailEnabled = false;
        RewindForTestCoda2();
    }

    private static void RewindForTestCoda2()
    {
        ProbeSweptEnabled = false;
        ProbeStepWalkEnabled = false;
        ProbeTeleportEnabled = false;
        ProbeRemoteTeleportEnabled = false;
        RewindForTestCoda3();
    }

    private static void RewindForTestCoda3()
    {
        RestartDistantSlideForTest();
        PreviousBspStrikePoly = null;
        PreviousStanceFailPolyIdent = 0;
        RewindForTestCoda4();
    }

    private static void RewindForTestCoda4()
    {
        PreviousStanceFailPolyNorm = default;
        PreviousStanceFailPolyD = 0f;
        PreviousStanceFailSolidLeaf = false;
        FinishRestartPerfectClipRearGuardForTest3();
    }

    private static void FinishRestartPerfectClipRearGuardForTest3()
    {
        ProbeDumpCellIds = new HashSet<uint>();
        ProbeDumpGfxObjIds = new HashSet<uint>();
        RestartPerfectClipRearGuardForTest();
    }

    private static string? Env(string label) => System.Environment.GetEnvironmentVariable(label);

    private static bool Mark(string label) => Env(label) == "1";

    public static ushort PreviousStanceFailPolyIdent { get; set; }
    public static Vector3 PreviousStanceFailPolyNorm { get; set; }
    public static float PreviousStanceFailPolyD { get; set; }
    public static bool PreviousStanceFailSolidLeaf { get; set; }

    private static int DecodePositiveInt(string? val)
    {
        return int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out int decoded) && decoded > 0 ? decoded : 0;
    }

    private static IReadOnlySet<uint> DecodeHexIdentRoster(string? raw)
    {
        HashSet<uint> idents = new HashSet<uint>();
        if (string.IsNullOrWhiteSpace(raw))
            return idents;

        foreach (string ticket in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string hex = ticket.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint ident))
                idents.Add(ident);
        }
        return idents;
    }
}
