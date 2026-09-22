using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace MacAC.Mechanics.Kinetics;

public static class KineticResolveCapture
{
    private const string TrailVariable = "MACAC_CAPTURE_RESOLVE";

    private static readonly Lock Latch = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static int _beats;
    private static StreamWriter? _drain;

    public static string? GrabTrail { get; set; } = System.Environment.GetEnvironmentVariable(TrailVariable);

    public static bool IsTurnedOn => !string.IsNullOrWhiteSpace(GrabTrail);

    public static KineticBodyFrame Snapshot(KineticBody corpus)
    {
        return new(
        Position: corpus.Position,
        Orientation: corpus.Orientation,
        Velocity: corpus.Velocity,
        Acceleration: corpus.Acceleration,
        Omega: corpus.Omega,
        GroundNormal: corpus.GroundNormal,
        SlidingNormal: corpus.SlidingNormal,
        ContactPlaneValid: corpus.ContactPlaneValid,
        ContactPlane: corpus.ContactPlane,
        ContactPlaneCellId: corpus.ContactPlaneCellId,
        ContactPlaneIsWater: corpus.ContactPlaneIsWater,
        WalkablePolygonValid: corpus.WalkablePolygonValid,
        WalkablePlane: corpus.WalkablePlane,
        WalkableVertices: corpus.WalkableVertices is { } corners ? (Vector3[])corners.Clone() : null,
        WalkableUp: corpus.WalkableUp,
        Elasticity: corpus.Elasticity,
        Friction: corpus.Friction,
        State: (uint)corpus.State,
        TransientState: (uint)corpus.TransientState,
        LastUpdateTime: corpus.PreviousRefreshMoment);
    }

    public static void TraceCall(ResolveCallArgs feed, KineticBodyFrame? corpusPrior, ResolveOutcome outcome, KineticBodyFrame? corpusFollowing)
    {
        if (!IsTurnedOn)
            return;

        ResolveCaptureRow rank = new ResolveCaptureRow(
            Tick: Interlocked.Increment(ref _beats) - 1,
            TimestampMs: (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency),
            Input: feed,
            BodyBefore: corpusPrior,
            Result: outcome,
            BodyAfter: corpusFollowing);
        string stroke = JsonSerializer.Serialize(rank, Json);

        lock (Latch)
        {
            var drain = _drain ??= OpenDrain();
            drain.WriteLine(stroke);
            drain.Flush();
        }
    }

    public static void Close()
    {
        lock (Latch)
        {
            if (_drain is null)
                return;
            _drain.Flush();
            _drain.Dispose();
            _drain = null;
        }
    }

    public static void RestartBeatCounter() => Interlocked.Exchange(ref _beats, 0);

    public static void RestartForTest()
    {
        Close();
        GrabTrail = null;
        Interlocked.Exchange(ref _beats, 0);
    }

    private static StreamWriter OpenDrain()
    {
        string trail = GrabTrail!;
        string? direction = Path.GetDirectoryName(trail);
        if (!string.IsNullOrEmpty(direction))
            Directory.CreateDirectory(direction);

        FileStream flow = new FileStream(trail, FileMode.Append, FileAccess.Write, FileShare.Read);
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => Close();
        return new StreamWriter(flow) { AutoFlush = false };
    }
}

public sealed record ResolveCaptureRow(
    int Tick,
    long TimestampMs,
    ResolveCallArgs Input,
    KineticBodyFrame? BodyBefore,
    ResolveOutcome Result,
    KineticBodyFrame? BodyAfter);

public sealed record ResolveCallArgs(
    Vector3 CurrentPos,
    Vector3 TargetPos,
    uint CellId,
    float SphereRadius,
    float SphereHeight,
    float StepUpHeight,
    float StepDownHeight,
    bool IsOnGround,
    uint MoverFlags,
    uint MovingEntityId);

public sealed record ResolveOutcome(
    Vector3 Position,
    uint CellId,
    bool IsOnGround,
    bool CollisionNormalValid,
    Vector3 CollisionNormal);

public sealed record KineticBodyFrame(
    Vector3 Position,
    Quaternion Orientation,
    Vector3 Velocity,
    Vector3 Acceleration,
    Vector3 Omega,
    Vector3 GroundNormal,
    Vector3 SlidingNormal,
    bool ContactPlaneValid,
    Plane ContactPlane,
    uint ContactPlaneCellId,
    bool ContactPlaneIsWater,
    bool WalkablePolygonValid,
    Plane WalkablePlane,
    Vector3[]? WalkableVertices,
    Vector3 WalkableUp,
    float Elasticity,
    float Friction,
    uint State,
    uint TransientState,
    double LastUpdateTime);
