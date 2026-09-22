using System.Globalization;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static partial class KineticTelemetry
{
    private const long DistantSlideBeatThrottleMsec = 200;

    private static readonly string? DistantSlideSensorRaw = Env("MACAC_PROBE_REMOTE_SLIDE");

    [ThreadStatic] private static uint _distantSlideAttributionOid;

    [ThreadStatic] private static Dictionary<uint, (long Ms, int Signature)>? _distantSlideBeatLatch;

    public static bool ProbeRemoteSlideEnabled { get; set; } = !string.IsNullOrWhiteSpace(DistantSlideSensorRaw);

    /// <summary>Empty means "every guid"; <c>MACAC_PROBE_REMOTE_SLIDE=1</c> is the same as empty.</summary>
    public static IReadOnlySet<uint> ProbeRemoteSlideGuids { get; set; } =
        DistantSlideSensorRaw is null || DistantSlideSensorRaw.Trim() == "1" ? new HashSet<uint>() : DecodeHexIdentRoster(DistantSlideSensorRaw);

    public static bool ShouldTraceDistantSlide(uint oid)
    {
        return ProbeRemoteSlideEnabled && (ProbeRemoteSlideGuids.Count is 0 || ProbeRemoteSlideGuids.Contains(oid));
    }

    public static void CommenceDistantSlideAttribution(uint oid)
    {
        if (ProbeRemoteSlideEnabled)
            _distantSlideAttributionOid = oid;
    }

    public static uint DistantSlideAttributionOid => _distantSlideAttributionOid;

    public static bool ShouldEmitDistantSlideBeat(uint oid, int signature)
    {
        if (!ShouldTraceDistantSlide(oid)) return false;
        _distantSlideBeatLatch ??= new Dictionary<uint, (long, int)>();
        long instant = System.Environment.TickCount64;
        if (_distantSlideBeatLatch.TryGetValue(oid, out var earlier)
            && earlier.Signature == signature
            && instant - earlier.Ms < DistantSlideBeatThrottleMsec)

            return false;
        _distantSlideBeatLatch[oid] = (instant, signature);
        return true;
    }

    public static void TraceDistantSlideUp(
        uint oid,
        bool wireGrounded,
        Vector3? wireVelocity,
        string disposition,
        float? avatarGap,
        float corpusToMark,
        float corpusSnapThreshold,
        bool willBeDrTicked,
        bool leadUp,
        bool airborne,
        bool link,
        bool onPassable,
        bool gravity,
        Vector3 corpusVel,
        bool linkPlaneValid,
        float linkPlaneNormZ,
        Vector3 wireLocus,
        Vector3 corpusLocus,
        int lerpFifoZDepth,
        int lerpFailTally)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        string wireVel = wireVelocity is { } wv
            ? string.Format(info, "({0:F3},{1:F3},{2:F3})", wv.X, wv.Y, wv.Z)
            : "null";
        string avatarDistance = avatarGap is { } pd
            ? pd.ToString("F2", info)
            : "n/a";
        Console.WriteLine(string.Format(info,
            "[remote-slide-up] guid=0x{0:X8} t={1} wireGrounded={2} wireVel={3} " +
            "disp={4} playerDist={5} bodyToTarget={6:F3} snapThreshold={7:F3} " +
            "willBeDrTicked={8} firstUpAtEntry={9} airborne={10} contact={11} " +
            "onWalkable={12} gravity={13} bodyVel=({14:F3},{15:F3},{16:F3}) " +
            "cpValid={17} cpNz={18:F4} floorZ={19:F4} steep={20} " +
            "wirePos=({21:F3},{22:F3},{23:F3}) bodyPos=({24:F3},{25:F3},{26:F3}) " +
            "queueDepth={27} failCount={28}",
            oid, System.Environment.TickCount64, wireGrounded, wireVel,
            disposition, avatarDistance, corpusToMark, corpusSnapThreshold,
            willBeDrTicked, leadUp, airborne, link,
            onPassable, gravity,
            corpusVel.X, corpusVel.Y, corpusVel.Z,
            linkPlaneValid, linkPlaneNormZ, KineticConstants.FloorZ,
            linkPlaneValid && linkPlaneNormZ < KineticConstants.FloorZ,
            wireLocus.X, wireLocus.Y, wireLocus.Z,
            corpusLocus.X, corpusLocus.Y, corpusLocus.Z,
            lerpFifoZDepth, lerpFailTally));
    }

    public static void TraceDistantSlideVector(
        uint oid,
        Vector3 wireVel,
        Vector3 wireOmega,
        bool willFlagAirborne,
        bool airbornePrior,
        bool link,
        bool onPassable,
        bool gravity,
        Vector3 corpusVel,
        bool linkPlaneValid,
        float linkPlaneNormZ)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[remote-slide-vec] guid=0x{0:X8} t={1} " +
            "wireVel=({2:F3},{3:F3},{4:F3}) wireOmega=({5:F3},{6:F3},{7:F3}) " +
            "willMarkAirborne={8} airborneBefore={9} contact={10} " +
            "onWalkable={11} gravity={12} bodyVel=({13:F3},{14:F3},{15:F3}) " +
            "cpValid={16} cpNz={17:F4} floorZ={18:F4} steep={19}",
            oid, System.Environment.TickCount64,
            wireVel.X, wireVel.Y, wireVel.Z,
            wireOmega.X, wireOmega.Y, wireOmega.Z,
            willFlagAirborne, airbornePrior, link,
            onPassable, gravity,
            corpusVel.X, corpusVel.Y, corpusVel.Z,
            linkPlaneValid, linkPlaneNormZ, KineticConstants.FloorZ,
            linkPlaneValid && linkPlaneNormZ < KineticConstants.FloorZ));
    }

    public static void TraceDistantSlideCorpusSnap(
        uint oid,
        bool leadUp,
        bool willBeDrTicked,
        float corpusToMark,
        float threshold,
        Vector3 corpusLocus,
        Vector3 markLocus,
        int lerpFifoZDepth,
        int lerpFailTally)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[remote-slide-snap] producer=ap87-4m guid=0x{0:X8} t={1} " +
            "firstUp={2} willBeDrTicked={3} bodyToTarget={4:F3} threshold={5:F3} " +
            "body=({6:F3},{7:F3},{8:F3}) target=({9:F3},{10:F3},{11:F3}) " +
            "queueDepth={12} failCount={13}",
            oid, System.Environment.TickCount64,
            leadUp, willBeDrTicked, corpusToMark, threshold,
            corpusLocus.X, corpusLocus.Y, corpusLocus.Z,
            markLocus.X, markLocus.Y, markLocus.Z,
            lerpFifoZDepth, lerpFailTally));
    }

    public static void TraceDistantSlideQueue(
        uint oid,
        float corpusToMark,
        Vector3 markLocus,
        int lerpFifoZDepth,
        int lerpFailTally)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[remote-slide-enq] guid=0x{0:X8} t={1} bodyToTarget={2:F3} " +
            "target=({3:F3},{4:F3},{5:F3}) queueDepth={6} failCount={7}",
            oid, System.Environment.TickCount64, corpusToMark,
            markLocus.X, markLocus.Y, markLocus.Z,
            lerpFifoZDepth, lerpFailTally));
    }

    public static void TraceDistantSlideStallSnap(
        int failTally,
        int threshold,
        int fifoZDepth,
        Vector3 corpusLocus,
        Vector3 rearLocus,
        float gapToFront)
    {
        uint oid = _distantSlideAttributionOid;
        if (!ShouldTraceDistantSlide(oid)) return;
        Vector3 rearDiff = rearLocus - corpusLocus;
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[remote-slide-snap] producer=interp-stall guid=0x{0:X8} t={1} " +
            "failCount={2} threshold={3} queueDepth={4} " +
            "body=({5:F3},{6:F3},{7:F3}) tail=({8:F3},{9:F3},{10:F3}) " +
            "tailDelta=({11:F3},{12:F3},{13:F3}) tailDeltaLen={14:F3} " +
            "distToHead={15:F3}",
            oid, System.Environment.TickCount64,
            failTally, threshold, fifoZDepth,
            corpusLocus.X, corpusLocus.Y, corpusLocus.Z,
            rearLocus.X, rearLocus.Y, rearLocus.Z,
            rearDiff.X, rearDiff.Y, rearDiff.Z, rearDiff.Length(),
            gapToFront));
    }

    public static void TraceDistantSlideBeat(
        uint oid,
        bool airborne,
        bool forcedLink,
        bool forcedPassable,
        Vector3 velPriorZero,
        bool settled,
        bool locateInLink,
        bool locateOnPassable,
        bool locateIsOnTerrain,
        bool locateLinkPlaneValid,
        float locateLinkPlaneNormZ,
        bool corpusLinkPlaneValid,
        float corpusLinkPlaneNormZ,
        bool link,
        bool onPassable,
        bool gravity,
        Vector3 vel,
        Vector3 acceleration,
        Vector3 preIntegrateLocus,
        Vector3 postIntegrateLocus,
        Vector3 settledLocus)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(info,
            "[remote-slide-tick] guid=0x{0:X8} t={1} airborne={2} " +
            "entryNoContact={3} entryNoWalkable={4} " +
            "velBeforeZero=({5:F3},{6:F3},{7:F3}) resolved={8} " +
            "rsInContact={9} rsOnWalkable={10} rsIsOnGround={11} " +
            "rsCpValid={12} rsCpNz={13:F4} " +
            "bodyCpValid={14} bodyCpNz={15:F4} floorZ={16:F4} steep={17} " +
            "contact={18} onWalkable={19} gravity={20} " +
            "vel=({21:F3},{22:F3},{23:F3}) accel=({24:F3},{25:F3},{26:F3}) " +
            "pre=({27:F3},{28:F3},{29:F3}) post=({30:F3},{31:F3},{32:F3}) " +
            "out=({33:F3},{34:F3},{35:F3}) moved={36:F4}",
            oid, System.Environment.TickCount64, airborne,
            forcedLink, forcedPassable,
            velPriorZero.X, velPriorZero.Y, velPriorZero.Z,
            settled,
            locateInLink, locateOnPassable, locateIsOnTerrain,
            locateLinkPlaneValid, locateLinkPlaneNormZ,
            corpusLinkPlaneValid, corpusLinkPlaneNormZ, KineticConstants.FloorZ,
            corpusLinkPlaneValid && corpusLinkPlaneNormZ < KineticConstants.FloorZ,
            link, onPassable, gravity,
            vel.X, vel.Y, vel.Z,
            acceleration.X, acceleration.Y, acceleration.Z,
            preIntegrateLocus.X, preIntegrateLocus.Y, preIntegrateLocus.Z,
            postIntegrateLocus.X, postIntegrateLocus.Y, postIntegrateLocus.Z,
            settledLocus.X, settledLocus.Y, settledLocus.Z,
            Vector3.Distance(preIntegrateLocus, settledLocus)));
    }

    private static void RestartDistantSlideForTest()
    {
        ProbeRemoteSlideEnabled = false;
        ProbeRemoteSlideGuids = new HashSet<uint>();
        _distantSlideAttributionOid = 0;
        _distantSlideBeatLatch = null;
    }
}
