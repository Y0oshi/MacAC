using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Play;

internal static class AvatarKineticsQuantumCapture
{
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static long s_series;

    internal static string? GrabTrail { get; set; } = Environment.GetEnvironmentVariable("MACAC_CAPTURE_PLAYER_QUANTA");

    internal static bool IsTurnedOn => !string.IsNullOrWhiteSpace(GrabTrail);

    internal static AvatarKineticsHullTraceCapture Freeze(KineticBody corpus)
    {
        return new(
        Position: corpus.Position,
        Orientation: corpus.Orientation,
        Velocity: corpus.Velocity,
        Acceleration: corpus.Acceleration,
        GroundNormal: corpus.GroundNormal,
        ContactPlaneValid: corpus.ContactPlaneValid,
        ContactPlane: corpus.ContactPlane,
        Friction: corpus.Friction,
        Elasticity: corpus.Elasticity,
        State: (uint)corpus.State,
        TransientState: (uint)corpus.TransientState,
        FramesStationaryFall: corpus.FramesStationaryFall);
    }

    internal static void Trace(
        float dt,
        uint chamberPrior,
        uint chamberFollowing,
        LocomotionInput feed,
        Vector3 trunkAndKeeperDiff,
        bool contenderMoved,
        AvatarKineticsHullTraceCapture quantumBegin,
        AvatarKineticsHullTraceCapture preIntegration,
        AvatarKineticsHullTraceCapture postIntegration,
        AvatarKineticsResolveTraceCapture locate,
        AvatarKineticsHullTraceCapture postSeal)
    {
        string? trail = GrabTrail;
        if (string.IsNullOrWhiteSpace(trail))
            return;

        var capture = new AvatarKineticsQuantumTraceRecord(
            Sequence: Interlocked.Increment(ref s_series) - 1,
            TimestampTicks: Stopwatch.GetTimestamp(),
            Dt: dt,
            CellBefore: chamberPrior,
            CellAfter: chamberFollowing,
            Input: new AvatarLocomotionInputTraceCapture(
                feed.Forward, feed.Backward, feed.StrafeLeft, feed.StrafeRight,
                feed.TurnLeft, feed.TurnRight, feed.Run, feed.Jump),
            RootAndManagerDelta: trunkAndKeeperDiff,
            CandidateMoved: contenderMoved,
            QuantumStart: quantumBegin,
            PreIntegration: preIntegration,
            PostIntegration: postIntegration,
            Resolve: locate,
            PostCommit: postSeal);

        Sink.WriteLine(trail, JsonSerializer.Serialize(capture, Json));
    }

    internal static void Close() => Sink.Close();

    internal static void RestartForTest()
    {
        Close();
        GrabTrail = null;
        Interlocked.Exchange(ref s_series, 0);
    }

    // The append-only file, opened on first write and closed at process exit
    private static class Sink
    {
        private static readonly object Latch = new();
        private static StreamWriter? s_writer;
        private static bool s_quitHooked;

        public static void WriteLine(string trail, string stroke)
        {
            lock (Latch)
            {
                s_writer ??= Open(trail);
                s_writer.WriteLine(stroke);
                s_writer.Flush();
            }
        }

        public static void Close()
        {
            lock (Latch)
            {
                s_writer?.Dispose();
                s_writer = null;
            }
        }

        private static StreamWriter Open(string trail)
        {
            string? folder = Path.GetDirectoryName(trail);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            StreamWriter writer = new StreamWriter(new FileStream(trail, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = false };
            if (!s_quitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => Close();
                s_quitHooked = true;
            }
            return writer;
        }
    }
}

internal sealed record AvatarKineticsQuantumTraceRecord(
    long Sequence,
    long TimestampTicks,
    float Dt,
    uint CellBefore,
    uint CellAfter,
    AvatarLocomotionInputTraceCapture Input,
    Vector3 RootAndManagerDelta,
    bool CandidateMoved,
    AvatarKineticsHullTraceCapture QuantumStart,
    AvatarKineticsHullTraceCapture PreIntegration,
    AvatarKineticsHullTraceCapture PostIntegration,
    AvatarKineticsResolveTraceCapture Resolve,
    AvatarKineticsHullTraceCapture PostCommit);

internal readonly record struct AvatarLocomotionInputTraceCapture(
    bool Forward, bool Backward, bool StrafeLeft, bool StrafeRight, bool TurnLeft, bool TurnRight, bool Run, bool Jump);

internal readonly record struct AvatarKineticsHullTraceCapture(
    Vector3 Position,
    Quaternion Orientation,
    Vector3 Velocity,
    Vector3 Acceleration,
    Vector3 GroundNormal,
    bool ContactPlaneValid,
    Plane ContactPlane,
    float Friction,
    float Elasticity,
    uint State,
    uint TransientState,
    int FramesStationaryFall);

internal readonly record struct AvatarKineticsResolveTraceCapture(
    Vector3 Position,
    uint CellId,
    bool Ok,
    bool IsOnGround,
    bool InContact,
    bool OnWalkable,
    bool CollisionNormalValid,
    Vector3 CollisionNormal);
