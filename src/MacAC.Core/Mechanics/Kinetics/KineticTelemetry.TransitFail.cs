using System.Globalization;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static partial class KineticTelemetry
{
    private const float PassageFailNonzeroReqXYSq = 0.001f * 0.001f;

    private const float PassageFailZeroXYSq = 0.0001f * 0.0001f;

    [ThreadStatic] private static List<string>? _passageFailBuf;
    [ThreadStatic] private static string? _passageFailAdjustStroke;

    /// <summary>Initial state from <c>MACAC_DUMP_TRANSIT_FAIL=1</c>.</summary>
    public static bool DumpTransitFailEnabled { get; set; } = Mark("MACAC_DUMP_TRANSIT_FAIL");

    private static List<string> PassageFailStrokes => _passageFailBuf ??= [];

    public static void CommencePassageFailTrace()
    {
        if (!DumpTransitFailEnabled) return;
        PassageFailStrokes.Clear();
        _passageFailAdjustStroke = null;
    }

    public static void RecordPassageSlotAttempt(
        uint carrierIdent,
        int attempt,
        string stage,
        ShiftVerdict environPhase,
        ShiftVerdict? structurePhase,
        ShiftVerdict? objectsPhase,
        ShiftVerdict verdict,
        Vector3 impactNorm,
        uint? collidedObjectOid)
    {
        if (!DumpTransitFailEnabled) return;

        CultureInfo info = CultureInfo.InvariantCulture;
        string structurePhrase = structurePhase is { } b ? b.ToString() : "n/a";
        string objectsPhrase = objectsPhase is { } o ? o.ToString() : "n/a";
        string collidedPhrase = verdict == ShiftVerdict.Collided
            ? string.Format(info,
                " collN=({0:F3},{1:F3},{2:F3}) src={3}",
                impactNorm.X, impactNorm.Y, impactNorm.Z,
                stage == "objects"
                    ? (collidedObjectOid is { } oid
                        ? string.Format(info, "object:0x{0:X8}", oid)
                        : "object:none")
                    : stage)
            : "";

        PassageFailStrokes.Add(string.Format(info,
            "[transit-fail-insert] mover=0x{0:X8} attempt={1} phase={2} " +
            "env={3} building={4} objects={5} outcome={6}{7}",
            carrierIdent, attempt, stage, environPhase, structurePhrase, objectsPhrase,
            verdict, collidedPhrase));
    }

    public static void RecordPassageHopUp(
        uint carrierIdent,
        string rim,
        Vector3 feedNorm,
        bool onPassable,
        float hopUpHeight,
        Vector3 spot,
        bool? succeeded,
        Vector3? landedNorm)
    {
        if (!DumpTransitFailEnabled) return;

        CultureInfo info = CultureInfo.InvariantCulture;
        float floor = KineticConstants.FloorZ;
        string verdict = feedNorm.Z >= floor ? "WALKABLE" : "STEEP";
        string verdictPhrase;
        if (succeeded is null)
        {
            verdictPhrase = "";
        }
        else if (succeeded.Value && landedNorm is { } landed)
        {
            string landedVerdict = landed.Z >= floor ? "WALKABLE" : "STEEP";
            verdictPhrase = string.Format(info,
                " outcome=SUCCESS landedN=({0:F3},{1:F3},{2:F3})->{3}",
                landed.X, landed.Y, landed.Z, landedVerdict);
        }
        else
        {
            verdictPhrase = " outcome=FAILED";
        }

        PassageFailStrokes.Add(string.Format(info,
            "[transit-fail-stepup] mover=0x{0:X8} edge={1} " +
            "n=({2:F3},{3:F3},{4:F3})->{5} onWalkable={6} stepUpHeight={7:F3} " +
            "pos=({8:F2},{9:F2},{10:F2}){11}",
            carrierIdent, rim,
            feedNorm.X, feedNorm.Y, feedNorm.Z, verdict,
            onPassable, hopUpHeight,
            spot.X, spot.Y, spot.Z, verdictPhrase));
    }

    public static void RecordPassageVetPassable(
        uint carrierIdent,
        string branch,
        float distance,
        float waterZDepth,
        bool oiLink,
        bool spHopDown,
        bool? guardPassed,
        Vector3 norm,
        ShiftVerdict verdict)
    {
        if (!DumpTransitFailEnabled) return;

        CultureInfo info = CultureInfo.InvariantCulture;
        string guardPhrase = guardPassed is { } g ? g.ToString() : "n/a";

        PassageFailStrokes.Add(string.Format(info,
            "[transit-fail-walk] mover=0x{0:X8} branch={1} dist={2:F5} " +
            "waterDepth={3:F4} oiContact={4} spStepDown={5} guardPassed={6} " +
            "normal=({7:F3},{8:F3},{9:F3}) outcome={10}",
            carrierIdent, branch, distance, waterZDepth, oiLink, spHopDown,
            guardPhrase, norm.X, norm.Y, norm.Z, verdict));
    }

    public static void RecordPassageAdjustShift(
        uint carrierIdent, string branch, Vector3 shiftIn, Vector3 shiftOut)
    {
        if (!DumpTransitFailEnabled) return;

        CultureInfo info = CultureInfo.InvariantCulture;
        _passageFailAdjustStroke = string.Format(info,
            "[transit-fail-adjust] mover=0x{0:X8} branch={1} " +
            "in=({2:F4},{3:F4},{4:F4}) out=({5:F4},{6:F4},{7:F4})",
            carrierIdent, branch,
            shiftIn.X, shiftIn.Y, shiftIn.Z,
            shiftOut.X, shiftOut.Y, shiftOut.Z);
    }

    public static void WritePassageFailIfStuck(
        uint carrierIdent, Vector3 latestSpot, Vector3 markSpot, Vector3 outcomeSpot)
    {
        if (!DumpTransitFailEnabled) return;

        float reqX = markSpot.X - latestSpot.X;
        float reqY = markSpot.Y - latestSpot.Y;
        float reqXYSq = reqX * reqX + reqY * reqY;
        float actX = outcomeSpot.X - latestSpot.X;
        float actY = outcomeSpot.Y - latestSpot.Y;
        float actXYSq = actX * actX + actY * actY;

        bool stuck = reqXYSq >= PassageFailNonzeroReqXYSq
                     && actXYSq <= PassageFailZeroXYSq;

        if (stuck)
        {
            CultureInfo info = CultureInfo.InvariantCulture;
            int strokeTally = (_passageFailBuf?.Count ?? 0)
                + (_passageFailAdjustStroke is null ? 0 : 1);
            Console.WriteLine(string.Format(info,
                "[transit-fail] mover=0x{0:X8} STUCK-TICK " +
                "reqXY=({1:F4},{2:F4}) reqLen={3:F4} " +
                "actXY=({4:F4},{5:F4}) actLen={6:F4} " +
                "in=({7:F3},{8:F3},{9:F3}) tgt=({10:F3},{11:F3},{12:F3}) " +
                "out=({13:F3},{14:F3},{15:F3}) lines={16}",
                carrierIdent, reqX, reqY, MathF.Sqrt(reqXYSq),
                actX, actY, MathF.Sqrt(actXYSq),
                latestSpot.X, latestSpot.Y, latestSpot.Z,
                markSpot.X, markSpot.Y, markSpot.Z,
                outcomeSpot.X, outcomeSpot.Y, outcomeSpot.Z,
                strokeTally));

            if (_passageFailBuf is { Count: > 0 } buf)
            {
                foreach (string stroke in buf)
                    Console.WriteLine(stroke);
            }
            if (_passageFailAdjustStroke is not null)
                Console.WriteLine(_passageFailAdjustStroke);
        }

        _passageFailBuf?.Clear();
        _passageFailAdjustStroke = null;
    }
}
