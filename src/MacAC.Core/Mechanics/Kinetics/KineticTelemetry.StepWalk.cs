using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Push-back, step-walk and contact-plane write probes.</summary>
public static partial class KineticTelemetry
{
    public static void TracePushBackAdjust(
        Vector3 feedMiddle,
        Vector3 productMiddle,
        Plane plane,
        float radius,
        float strollLerpPrior,
        float strollLerpFollowing,
        float dpSpot,
        float dpRelocate,
        float idxDistance,
        bool imposed)
    {
        Vector3 diff = productMiddle - feedMiddle;
        float diffMag = diff.Length();
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[push-back] site=adjust_sphere " +
            "in=({0:F4},{1:F4},{2:F4}) " +
            "out=({3:F4},{4:F4},{5:F4}) " +
            "delta=({6:F4},{7:F4},{8:F4}) deltaMag={9:F4} " +
            "n=({10:F4},{11:F4},{12:F4}) d={13:F4} " +
            "r={14:F4} winterp={15:F4}->{16:F4} " +
            "dpPos={17:F4} dpMove={18:F4} iDist={19:F4} applied={20}",
            feedMiddle.X, feedMiddle.Y, feedMiddle.Z,
            productMiddle.X, productMiddle.Y, productMiddle.Z,
            diff.X, diff.Y, diff.Z, diffMag,
            plane.Normal.X, plane.Normal.Y, plane.Normal.Z, plane.D,
            radius, strollLerpPrior, strollLerpFollowing,
            dpSpot, dpRelocate, idxDistance, imposed));
    }

    public static void TracePushBackRelay(
        Vector3 orbMiddle,
        Vector3 travel,
        bool collide,
        int slotKind,
        int objRefPhase,
        float strollLerpListing,
        int returnPhase)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[push-back-disp] site=dispatch " +
            "center=({0:F4},{1:F4},{2:F4}) " +
            "mvmt=({3:F4},{4:F4},{5:F4}) " +
            "collide={6} insertType={7} objState=0x{8:X} " +
            "winterp={9:F4} return={10}",
            orbMiddle.X, orbMiddle.Y, orbMiddle.Z,
            travel.X, travel.Y, travel.Z,
            collide, slotKind, objRefPhase,
            strollLerpListing, returnPhase));
    }

    public static void TracePushBackChamberPassage(
        uint primaryChamberIdent,
        uint anotherChamberIdent,
        int bspOutcome,
        bool halted)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[push-back-cell] site=other_cell " +
            "primary=0x{0:X8} other=0x{1:X8} " +
            "bspResult={2} halted={3}",
            primaryChamberIdent, anotherChamberIdent, bspOutcome, halted));
    }

    public static void TraceHopStroll(
        string site,
        int hopOrdinal,
        int hopTally,
        SweepPath path,
        ContactLedger ledger,
        MoverFacts facts,
        Vector3 askedShift,
        Vector3 adjustedShift,
        ShiftVerdict? phase = null,
        string? specifics = null)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        Vector3 verifyDiff = path.CheckPos - path.CurPos;
        string phasePhrase = phase.HasValue ? phase.Value.ToString() : "n/a";
        string hopPhrase = hopOrdinal >= 0 && hopTally > 0
            ? string.Format(culture, "{0}/{1}", hopOrdinal + 1, hopTally)
            : "-";

        Console.WriteLine(string.Format(culture,
            "[step-walk] site={0} step={1} state={2} " +
            "cur=({3:F4},{4:F4},{5:F4}) check=({6:F4},{7:F4},{8:F4}) " +
            "delta=({9:F4},{10:F4},{11:F4}) cell=0x{12:X8}->0x{13:X8} " +
            "req=({14:F4},{15:F4},{16:F4}) adj=({17:F4},{18:F4},{19:F4}) " +
            "winterp={20:F4} stepUp={21} stepDown={22} insert={23} " +
            "oi=0x{24:X} contact={25} onWalkable={26} " +
            "cp={27} lkcp={28} hit={29} slide={30} walkPoly={31} lastWalkPoly={32}{33}",
            site, hopPhrase, phasePhrase,
            path.CurPos.X, path.CurPos.Y, path.CurPos.Z,
            path.CheckPos.X, path.CheckPos.Y, path.CheckPos.Z,
            verifyDiff.X, verifyDiff.Y, verifyDiff.Z,
            path.CurCellId, path.CheckCellId,
            askedShift.X, askedShift.Y, askedShift.Z,
            adjustedShift.X, adjustedShift.Y, adjustedShift.Z,
            path.WalkInterp,
            path.StepUp, path.StepDown, path.InsertType,
            (uint)facts.State, facts.Contact, facts.OnWalkable,
            ComposePlane(ledger.ContactPlaneValid, ledger.ContactPlane, ledger.ContactPlaneCellId, ledger.ContactPlaneIsWater),
            ComposePlane(ledger.LastKnownContactPlaneValid, ledger.LastKnownContactPlane, ledger.LastKnownContactPlaneCellId, ledger.LastKnownContactPlaneIsWater),
            ComposeVector(ledger.CollisionNormalValid, ledger.CollisionNormal),
            ComposeVector(ledger.SlidingNormalValid, ledger.SlidingNormal),
            path.HasPassablePolyg, path.HasPreviousPassablePolyg,
            string.IsNullOrEmpty(specifics) ? string.Empty : " " + specifics));
    }

    public static void TraceHopStrollAdjust(
        string branch,
        Vector3 feed,
        Vector3 product,
        Plane? linkPlane,
        bool slidingValid,
        Vector3 slidingNorm,
        float impactAngle,
        float strollLerp)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;

        string cpDsc = linkPlane is { } cp
            ? string.Format(culture,
                "n=({0:F4},{1:F4},{2:F4}) d={3:F4}",
                cp.Normal.X, cp.Normal.Y, cp.Normal.Z, cp.D)
            : "n/a";

        string slideDsc = slidingValid
            ? string.Format(culture,
                "({0:F4},{1:F4},{2:F4})",
                slidingNorm.X, slidingNorm.Y, slidingNorm.Z)
            : "n/a";

        Console.WriteLine(string.Format(culture,
            "[step-walk-adjust] branch={0} input=({1:F4},{2:F4},{3:F4}) " +
            "output=({4:F4},{5:F4},{6:F4}) zGain={7:F4} " +
            "cp={8} slide={9} colAngle={10:F4} winterp={11:F4}",
            branch,
            feed.X, feed.Y, feed.Z,
            product.X, product.Y, product.Z,
            product.Z - feed.Z,
            cpDsc, slideDsc, impactAngle, strollLerp));
    }

    public static void TraceCpBoolEmit(string field, bool formerVal, bool newVal)
    {
        string caller = FetchCpCallerLabel();
        Console.WriteLine(FormattableString.Invariant(
            $"[cp-write] {field}: {formerVal} -> {newVal} caller={caller}"));
    }

    public static void TraceCpPlaneEmit(string field, Plane formerPlane, Plane newPlane)
    {
        string caller = FetchCpCallerLabel();
        Console.WriteLine(FormattableString.Invariant(
            $"[cp-write] {field}: n=({formerPlane.Normal.X:F3},{formerPlane.Normal.Y:F3},{formerPlane.Normal.Z:F3}) D={formerPlane.D:F3} -> n=({newPlane.Normal.X:F3},{newPlane.Normal.Y:F3},{newPlane.Normal.Z:F3}) D={newPlane.D:F3} caller={caller}"));
    }

    public static void TraceCpChamberIdentEmit(string field, uint formerVal, uint newVal)
    {
        string caller = FetchCpCallerLabel();
        Console.WriteLine(FormattableString.Invariant(
            $"[cp-write] {field}: 0x{formerVal:X8} -> 0x{newVal:X8} caller={caller}"));
    }

    private static string ComposeVector(bool valid, Vector3 val)
    {
        if (!valid)
            return "n/a";

        return string.Format(CultureInfo.InvariantCulture,
            "({0:F4},{1:F4},{2:F4})",
            val.X, val.Y, val.Z);
    }

    private static string ComposePlane(bool valid, Plane plane, uint chamberIdent, bool isWater)
    {
        if (!valid)
            return "n/a";

        float zAtOrigin = MathF.Abs(plane.Normal.Z) > KineticConstants.EPSILON
            ? -plane.D / plane.Normal.Z
            : float.NaN;

        return string.Format(CultureInfo.InvariantCulture,
            "cell=0x{0:X8},water={1},n=({2:F4},{3:F4},{4:F4}),d={5:F4},z0={6:F4}",
            chamberIdent, isWater,
            plane.Normal.X, plane.Normal.Y, plane.Normal.Z, plane.D,
            zAtOrigin);
    }

    private static string FetchCpCallerLabel()
    {
        StackTrace trace = new StackTrace(2, fNeedFileInfo: true);
        for (int idx = 0; idx < trace.FrameCount; ++idx)
        {
            StackFrame? frame = trace.GetFrame(idx);
            var m = frame?.GetMethod();
            if (m is null) continue;
            string kindLabel = m.DeclaringType?.Name ?? "?";
            if (kindLabel == "ContactLedger" || kindLabel == "KineticTelemetry") continue;
            int stroke = frame?.GetFileLineNumber() ?? 0;
            return stroke > 0 ? $"{kindLabel}.{m.Name}:{stroke}" : $"{kindLabel}.{m.Name}";
        }
        return "?";
    }
}
